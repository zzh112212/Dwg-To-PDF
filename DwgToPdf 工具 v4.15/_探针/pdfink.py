"""pdfink.py -- quantify how dark/heavy a plotted PDF is, without a renderer.

Why: the complaint is "打印发浅（文字可读但图形线条浅）". File size alone cannot tell
light linework from dark linework and no PDF renderer is installed here, so we read the
page content stream and count what decides how the page prints:
  * stroke/fill colours - white strokes are invisible on white paper (= 发浅)
  * line widths (`w`)  - with lineweights off, everything falls back to the plot style
                         default (0.25 mm), which reads thin/pale
  * painting operators  - S stroke, f/B fill, Tj/TJ text

Usage:
    python pdfink.py a.pdf b.pdf
    python pdfink.py --csv *.pdf        (one line per file, easy to diff)
"""
import hashlib
import re
import sys
import zlib
from collections import Counter

NUM = r"[-+]?[0-9]*\.?[0-9]+"


def _objects(data):
    """Return {objnum: raw_body} for every indirect object in the file."""
    objs = {}
    for m in re.finditer(rb"(\d+)\s+(\d+)\s+obj\b", data):
        end = data.find(b"endobj", m.end())
        if end != -1:
            objs[int(m.group(1))] = data[m.end():end]
    return objs


def _stream(body):
    """Return the decoded stream bytes of an object body, or None."""
    m = re.search(rb"stream\r?\n", body)
    if not m:
        return None
    raw = body[m.end():]
    e = raw.rfind(b"endstream")
    if e != -1:
        raw = raw[:e]
    if b"/FlateDecode" in body[:m.start()]:
        try:
            return zlib.decompress(raw)
        except zlib.error:
            return None
    return raw


def _looks_like_content(b):
    """Heuristic: a page content stream contains path/text operators, not just bytes."""
    return (b"\nS\n" in b or b"\ns\n" in b or b"\nf\n" in b or b"\nre\n" in b
            or b" l\n" in b or b" c\n" in b or b"\nTj" in b or b" TJ" in b
            or b"\nBT" in b)


def _content(objs):
    """Concatenate the page content streams.

    Handles the three shapes seen in practice:
      /Contents 12 0 R          - classic
      /Contents [12 0 R 13 0 R] - array
      neither (objects packed in object streams) - fall back to "every decompressed
      stream that actually looks like page content", which is enough for the
      before/after comparison this tool exists for.
    """
    chunks = []
    for num in sorted(objs):
        body = objs[num]
        m = re.search(rb"/Contents\s+(\d+)\s+\d+\s+R", body)
        if m:
            s = _stream(objs.get(int(m.group(1)), b""))
            if s:
                chunks.append(s)
            continue
        m = re.search(rb"/Contents\s*\[(.*?)\]", body, re.S)
        if m:
            for r in re.finditer(rb"(\d+)\s+\d+\s+R", m.group(1)):
                s = _stream(objs.get(int(r.group(1)), b""))
                if s:
                    chunks.append(s)

    if chunks:
        return b"\n".join(chunks)

    # fallback: scan every stream in the file
    for num in sorted(objs):
        s = _stream(objs[num])
        if s and _looks_like_content(s):
            chunks.append(s)
    return b"\n".join(chunks)


def _norm(g):
    return " ".join((x.rstrip("0").rstrip(".") or "0") for x in g)


def analyse(path):
    """Return a dict of everything we can measure about a plotted PDF."""
    data = open(path, "rb").read()
    info = {"file": path, "bytes": len(data)}

    mb = re.search(("/MediaBox\\s*\\[\\s*(%s)\\s+(%s)\\s+(%s)\\s+(%s)"
                    % (NUM, NUM, NUM, NUM)).encode("latin-1"), data)
    if mb:
        w = float(mb.group(3)) - float(mb.group(1))
        h = float(mb.group(4)) - float(mb.group(2))
        info["page_pt"] = "%.0fx%.0f" % (w, h)
        info["page_mm"] = "%.0fx%.0f" % (w / 72.0 * 25.4, h / 72.0 * 25.4)
    else:
        info["page_pt"] = info["page_mm"] = "?"
    info["pages"] = len(re.findall(rb"/Type\s*/Page[^s]", data))

    content = _content(_objects(data))
    info["content_bytes"] = len(content)
    # md5 of the page content only: proves whether two PDFs differ in the actual drawing
    # or just in metadata (AutoCAD stamps XMP/ID timestamps, so byte-identical size with
    # different file hashes is normal and says nothing on its own).
    info["content_md5"] = hashlib.md5(content).hexdigest()[:12] if content else "-"
    if not content:
        info.update(stroke_ops=0, fill_ops=0, text_ops=0, stroke_colors={},
                    fill_colors={}, widths={}, stroke_color_total=0,
                    white_strokes=0, white_ratio="n/a")
        return info

    t = content.decode("latin-1", "replace")
    sc, fc, wd = Counter(), Counter(), Counter()

    for m in re.finditer(r"(%s)\s+(%s)\s+(%s)\s+RG" % (NUM, NUM, NUM), t):
        sc[_norm(m.groups())] += 1
    for m in re.finditer(r"(%s)\s+(%s)\s+(%s)\s+rg" % (NUM, NUM, NUM), t):
        fc[_norm(m.groups())] += 1
    # greyscale forms: "g G" strokes, "g g" fills. Only non-white greys are interesting.
    for m in re.finditer(r"(?:^|[\s])(%s)\s+G(?=[\s])" % NUM, t):
        if m.group(1) not in ("1", "1.0"):
            sc["grey " + m.group(1)] += 1
    for m in re.finditer(r"(?:^|[\s])(%s)\s+g(?=[\s])" % NUM, t):
        fc["grey " + m.group(1)] += 1
    for m in re.finditer(r"(?:^|[\s])(%s)\s+w(?=[\s])" % NUM, t):
        try:
            wd["%.3f" % float(m.group(1))] += 1
        except ValueError:
            pass

    info["stroke_ops"] = len(re.findall(r"(?:^|[\s])S(?=[\s])", t))
    info["fill_ops"] = len(re.findall(r"(?:^|[\s])(?:f\*?|B\*?|b\*?)(?=[\s])", t))
    info["text_ops"] = len(re.findall(r"(?:^|[\s])(?:Tj|TJ)(?=[\s])", t))
    info["stroke_colors"] = dict(sc)
    info["fill_colors"] = dict(fc)
    info["widths"] = dict(wd)

    tot = sum(sc.values())
    white = sum(v for k, v in sc.items() if k == "1 1 1" or k == "grey 1")
    info["stroke_color_total"] = tot
    info["white_strokes"] = white
    info["white_ratio"] = ("%.1f%%" % (100.0 * white / tot)) if tot else "n/a"

    tiles = _images(data)
    info["image_tiles"] = len(tiles)
    ink = _ink_profile(data)
    info["ink"] = ink
    info["scale"] = _page_scale(data)
    if tiles:
        px = sum(w * h for w, h in tiles)
        info["image_px"] = px
        m = re.search(("/MediaBox\\s*\\[\\s*(%s)\\s+(%s)\\s+(%s)\\s+(%s)" % (NUM, NUM, NUM, NUM))
                      .encode("latin-1"), data)
        if m:
            pw = float(m.group(3)) - float(m.group(1))
            ph = float(m.group(4)) - float(m.group(2))
            mm2 = (pw / 72.0 * 25.4) * (ph / 72.0 * 25.4)
            info["eff_dpi"] = (px / mm2) ** 0.5 * 25.4
    return info


def _img_objects(data):
    """Yield the raw body of every image XObject.

    NOTE: match "/Subtype /Image" with the regex, not a bare b"/Image" substring -
    a page object contains "/ProcSet [ /PDF /ImageC]" and would otherwise be picked up.
    """
    for m in re.finditer(rb"(\d+)\s+(\d+)\s+obj\b", data):
        end = data.find(b"endobj", m.end())
        if end == -1:
            continue
        body = data[m.end():end]
        if re.search(rb"/Subtype\s*/Image", body):
            yield body


def _images(data):
    """Raster tile statistics - decisive for 'plots come out light'.

    AutoCAD's PDF driver tiles a large page into images when it plots in raster mode.
    The effective dpi decides whether a 0.25 mm line survives: at ~100 dpi a hairline is
    one anti-aliased grey pixel (prints pale), at 600 dpi it stays crisp black.
    """
    tiles = []
    for body in _img_objects(data):
        w = re.search(rb"/Width\s+(\d+)", body)
        h = re.search(rb"/Height\s+(\d+)", body)
        if w and h:
            tiles.append((int(w.group(1)), int(h.group(1))))
    return tiles


def _ink_profile(data, max_tiles=6):
    """Mean darkness / ink coverage of the raster tiles - the objective 'is it light?' number.

    Rasterized plots cannot be compared by file size: a light page and a dark page of the
    same drawing differ by only a few percent. So we decompress the tiles (Flate/DeviceRGB
    8 bpc) and histogram the bytes:
      mean   - average channel value (255 = blank white, lower = more ink)
      dark   - percentage of samples below 128 (clearly drawn pixels)
      nonwhite - percentage of samples below 250 (anything not pure paper white)
    Memory-safe: each stream is consumed in chunks and only a 256-bin histogram is kept.
    """
    hist = [0] * 256
    total = 0
    tiles = 0
    for body in _img_objects(data):
        if tiles >= max_tiles:
            break
        if b"/FlateDecode" not in body:
            continue
        sm = re.search(rb"stream\r?\n", body)
        if not sm:
            continue
        raw = body[sm.end():body.rfind(b"endstream")]
        d = zlib.decompressobj()
        try:
            chunk = d.decompress(raw, 1 << 22)
        except zlib.error:
            continue
        if not chunk:
            continue
        for b in chunk:
            hist[b] += 1
        total += len(chunk)
        tiles += 1
    if not total:
        return None
    mean = sum(i * hist[i] for i in range(256)) / float(total)
    dark = sum(hist[:128]) / float(total) * 100.0
    nonwhite = sum(hist[:250]) / float(total) * 100.0
    return {"tiles": tiles, "samples": total, "mean": mean,
            "dark_pct": dark, "nonwhite_pct": nonwhite}


def _page_scale(data):
    """Placement matrix of the page content: how much of the sheet the drawing covers.

    The content stream draws each tile with a `w` (cm) matrix. Reading the scale factors
    of those matrices tells us the mm-per-unit actually used, which is how we catch
    "the drawing got shrunk onto an oversized sheet" - the mechanism behind plots that
    look pale even though every lineweight is correct.
    """
    content = _content(_objects(data))
    if not content:
        return None
    t = content.decode("latin-1", "replace")
    scales = []
    for m in re.finditer(r"(%s)\s+(%s)\s+(%s)\s+(%s)\s+(%s)\s+(%s)\s+cm" % ((NUM,) * 6), t):
        a, b, c, d = float(m.group(1)), float(m.group(2)), float(m.group(5)), float(m.group(6))
        if a > 0 and d > 0:
            scales.append((a, d))
    if not scales:
        return None
    a = max(s[0] for s in scales)
    d = max(s[1] for s in scales)
    return {"x_pt_per_unit": a, "y_pt_per_unit": d,
            "mm_per_unit_x": a / 72.0 * 25.4, "mm_per_unit_y": d / 72.0 * 25.4}


def _fmt(i):
    return "\n".join([
        "== %s" % i["file"],
        "   size=%dB pages=%s page=%spt (%smm) content=%dB md5=%s"
        % (i["bytes"], i["pages"], i["page_pt"], i["page_mm"], i["content_bytes"],
           i.get("content_md5", "-")),
        "   ops: stroke=%s fill=%s text=%s"
        % (i.get("stroke_ops"), i.get("fill_ops"), i.get("text_ops")),
        "   stroke colors: %s" % (i.get("stroke_colors") or "{}"),
        "   fill   colors: %s" % (i.get("fill_colors") or "{}"),
        "   line widths  : %s" % (i.get("widths") or "{}"),
        "   WHITE strokes: %s / %s = %s"
        % (i.get("white_strokes"), i.get("stroke_color_total"), i.get("white_ratio")),
        "   raster tiles : %s   total px=%s   effective dpi=%s"
        % (i.get("image_tiles", 0),
           ("{:,}".format(i["image_px"]) if i.get("image_px") else "-"),
           ("{0:.0f}".format(i["eff_dpi"]) if i.get("eff_dpi") else "n/a (vector)")),
        "   INK (sampled %s tiles, %s samples): mean=%.1f  dark<128=%.2f%%  nonwhite<250=%.2f%%"
        % (i["ink"]["tiles"], "{:,}".format(i["ink"]["samples"]), i["ink"]["mean"],
           i["ink"]["dark_pct"], i["ink"]["nonwhite_pct"])
        if i.get("ink") else "   INK: n/a (no raster tiles)",
        "   scale: %.4f x %.4f mm per drawing unit  (1.0 = true size)"
        % (i["scale"]["mm_per_unit_x"], i["scale"]["mm_per_unit_y"])
        if i.get("scale") else "   scale: n/a",
    ])


def main(argv):
    csv = "--csv" in argv
    files = [a for a in argv[1:] if a != "--csv"]
    if not files:
        print(__doc__)
        return 2
    res = [analyse(f) for f in files]
    if csv:
        keys = ["file", "bytes", "pages", "page_mm", "content_bytes", "stroke_ops",
                "fill_ops", "text_ops", "white_strokes", "stroke_color_total", "white_ratio"]
        print(",".join(keys))
        for r in res:
            print(",".join(str(r.get(k, "")) for k in keys))
    else:
        print("\n\n".join(_fmt(r) for r in res))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
