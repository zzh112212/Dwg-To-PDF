#!/usr/bin/env python3
# -*- coding: ascii -*-
"""pdfruns.py -- how thick are the lines AutoCAD actually drew?

Why this exists (measured 2026-10-07)
-------------------------------------
PISCES 2A0 sheets "print light". Comparing three PDFs of the SAME drawing:

    GUI "shade plot = as displayed" : content stream 2,105,280 B -> VECTOR output
    GUI "shade plot = rendered"      : content stream     4,715 B -> raster
    tool output (accoreconsole)      : content stream    20,832 B -> raster

So the tool reproduces the *rendered* (raster) look, and the raster path is the one
that looks pale. To pin down WHY, we need the drawn line width in mm, not file size.

pdfink.py already reported mean=0.2 for these files, which was a bug on my side: every
image XObject carries

    /DecodeParms << /Predictor 15 /Colors 3 /BitsPerComponent 8 /Columns 18410 >>

so the Flate stream is PNG-*filtered* per row and must be un-predicted before any
histogram means anything. This module does that and then measures:

  * ink          : mean / dark<128 / nonwhite<250 over decoded samples
  * run lengths  : horizontal runs of dark pixels -> drawn line width in px -> mm
  * vector widths: for vector PDFs, the values of the `w` (line width) operator

Run:  python pdfruns.py <file.pdf> [more.pdfs...]        (ASCII-only console output)
"""
import re
import sys
import zlib

NUM = r"[-+]?[0-9]*\.?[0-9]+"


def _objects(data):
    for m in re.finditer(rb"(\d+)\s+(\d+)\s+obj\b", data):
        end = data.find(b"endobj", m.end())
        if end != -1:
            yield data[m.end():end]


def _content(data):
    """Concatenate every stream that is NOT an image: those hold the vector path data."""
    parts = []
    for body in _objects(data):
        if re.search(rb"/Subtype\s*/Image", body):
            continue
        m = re.search(rb"stream\r?\n", body)
        if not m:
            continue
        raw = body[m.end():body.rfind(b"endstream")]
        try:
            parts.append(zlib.decompress(raw))
        except zlib.error:
            parts.append(raw)
    return b"\n".join(parts)


def _predictor(body):
    m = re.search(rb"/Predictor\s+(\d+)", body)
    return int(m.group(1)) if m else 1


def _unpredict(buf, columns, colors):
    """Undo PNG row filters (PDF /Predictor 15). Returns (pixels, rows) bytes object."""
    bpp = colors
    stride = columns * bpp
    out = bytearray()
    prev = bytearray(stride)
    pos = 0
    while pos + stride + 1 <= len(buf):
        ft = buf[pos]
        pos += 1
        row = bytearray(buf[pos:pos + stride])
        pos += stride
        if ft == 1:
            for i in range(bpp, stride):
                row[i] = (row[i] + row[i - bpp]) & 0xFF
        elif ft == 2:
            for i in range(stride):
                row[i] = (row[i] + prev[i]) & 0xFF
        elif ft == 3:
            for i in range(stride):
                left = row[i - bpp] if i >= bpp else 0
                row[i] = (row[i] + ((left + prev[i]) >> 1)) & 0xFF
        elif ft == 4:
            for i in range(stride):
                a = row[i - bpp] if i >= bpp else 0
                b = prev[i]
                c = prev[i - bpp] if i >= bpp else 0
                p = a + b - c
                pa = abs(p - a)
                pb = abs(p - b)
                pc = abs(p - c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                row[i] = (row[i] + pr) & 0xFF
        out += row
        prev = row
    return bytes(out)


def _first_image(data, max_rows=48):
    """Decode the top rows of the first image XObject. Returns dict or None."""
    for body in _objects(data):
        if not re.search(rb"/Subtype\s*/Image", body):
            continue
        w = re.search(rb"/Width\s+(\d+)", body)
        h = re.search(rb"/Height\s+(\d+)", body)
        if not (w and h):
            continue
        width = int(w.group(1))
        height = int(h.group(1))
        sm = re.search(rb"stream\r?\n", body)
        if not sm:
            continue
        raw = body[sm.end():body.rfind(b"endstream")]
        try:
            dec = zlib.decompressobj().decompress(raw)
        except zlib.error:
            continue
        pred = _predictor(body)
        cols = re.search(rb"/Columns\s+(\d+)", body)
        colors = re.search(rb"/Colors\s+(\d+)", body)
        ncol = int(cols.group(1)) if cols else width
        ncol_c = int(colors.group(1)) if colors else 1
        if pred == 15:
            px = _unpredict(dec, ncol, ncol_c)
        else:
            px = dec
        stride = ncol * ncol_c
        rows = min(height, max_rows, len(px) // stride if stride else 0)
        return {"w": width, "h": height, "stride": stride, "bpc": ncol_c,
                "rows": rows, "px": px[:rows * stride]}
    return None


def _ink_and_runs(t):
    """Gray-ish value per pixel (DeviceRGB 8bpc only) + dark run lengths per row."""
    stride, bpc, rows = t["stride"], t["bpc"], t["rows"]
    px = t["px"]
    total = 0
    dark = 0
    nonwhite = 0
    runs = {}
    for r in range(rows):
        base = r * stride
        run = 0
        for i in range(0, stride, bpc):
            if bpc >= 3:
                v = (px[base + i] * 299 + px[base + i + 1] * 587 + px[base + i + 2] * 114) // 1000
            else:
                v = px[base + i]
            total += 1
            if v < 128:
                dark += 1
                run += 1
            else:
                if v < 250:
                    nonwhite += 1
                if run:
                    runs[run] = runs.get(run, 0) + 1
                    run = 0
        if run:
            runs[run] = runs.get(run, 0) + 1
    mean = 255.0 * (1.0 - dark / float(total or 1))
    return {"samples": total, "dark_pct": 100.0 * dark / float(total or 1),
            "nonwhite_pct": 100.0 * nonwhite / float(total or 1),
            "ink_mean_darkness": 255.0 - mean, "runs": runs}


def _paint_ops(data):
    """Count path/paint operators, so we can tell stroked output from filled output.

    MEASURED 2026-10-07: helpful for sanity-checking that geometry is vector at all.
    The `S` count barely moves between lineweights on/off, which rules out the
    "thick lines become filled quadrangles" guess - see _stroke_widths for what
    actually differs (the line width carried in the graphics state).
    """
    txt = _content(data).decode("latin-1", "replace")
    ops = {}
    for name in ("S", "s", "f", "F", "f*", "B", "B*", "b", "b*", "n", "re", "m", "l", "c", "W"):
        pat = r"(?<![A-Za-z*])" + re.escape(name) + r"(?![A-Za-z*])"
        ops[name] = len(re.findall(pat, txt))
    return ops


def _stroke_widths(data):
    """Effective line width of every stroked path, in PDF points.

    MEASURED 2026-10-07 (this replaces a misleading metric):
        The previous version counted `w` OPERATOR occurrences. That is not the
        same thing as "how wide are the strokes", because PDF line width is
        graphics state: AutoCAD emits `6 w` ONCE and thousands of subsequent
        `S` calls inherit it. Counting operators made the lineweights-on page
        look like "only 294 strokes have a width", when in truth 9,967 of its
        13,290 strokes are drawn at 6pt (= 0.25mm on paper, the AutoCAD default
        lineweight). Meanwhile the lineweights-off page sets `0 w` before every
        single stroke, so it reported 13,843 - and it really is all hairline.

    So: walk the stream in order, remember the current width (PDF default 1.0),
    and attribute it to each `S` (stroke). That histogram is the honest one.

    Note the scale: the content stream starts with `0.12 0 0 0.12 0 0 cm`, so
    6pt here is 0.72pt / 0.254mm on paper.
    """
    txt = _content(data).decode("latin-1", "replace")
    cur = 1.0
    hist = {}
    strokes = 0
    for m in re.finditer(r"(?:(?<![A-Za-z*])(" + NUM + r")\s+w(?![A-Za-z*]))"
                         r"|(?:(?<![A-Za-z*])S(?![A-Za-z*]))", txt):
        if m.group(1) is not None:
            try:
                cur = float(m.group(1))
            except ValueError:
                pass
        else:
            strokes += 1
            if 0 <= cur < 20:
                k = "%.3f" % cur
                hist[k] = hist.get(k, 0) + 1
    return hist, strokes


def _w_ops(data):
    """Raw count of `w` operators - kept only to show the driver's emit style."""
    txt = _content(data).decode("latin-1", "replace")
    return len(re.findall(r"(?<![A-Za-z*])" + NUM + r"\s+w(?![A-Za-z*])", txt))


def report(path):
    data = open(path, "rb").read()
    name = path.rsplit("\\", 1)[-1].rsplit("/", 1)[-1]
    mb = re.search(("/MediaBox\\s*\\[\\s*(%s)\\s+(%s)\\s+(%s)\\s+(%s)" % (NUM, NUM, NUM, NUM)).encode(), data)
    page = ""
    if mb:
        # groups: 1=x0 2=y0 3=x1 4=y1
        wpt = float(mb.group(3)) - float(mb.group(1))
        hpt = float(mb.group(4)) - float(mb.group(2))
        page = "%.0fx%.0fpt (%.0fx%.0fmm)" % (wpt, hpt, wpt / 72.0 * 25.4, hpt / 72.0 * 25.4)
    print("== %s" % name)
    print("   size=%dB  page=%s" % (len(data), page or "?"))

    content = _content(data)
    imgs = [b for b in _objects(data) if re.search(rb"/Subtype\s*/Image", b)]
    sw, strokes = _stroke_widths(data)
    print("   vector content=%dB   image tiles=%d   strokes=%d   stroke width/pt=%s   (w-ops=%d)"
          % (len(content), len(imgs), strokes,
             (sorted(sw.items(), key=lambda kv: -kv[1])[:6] or "none"), _w_ops(data)))
    ops = _paint_ops(data)
    print("   paint: " + "  ".join("%s=%d" % (k, ops[k]) for k in ("S", "f", "B", "n", "re", "m", "l", "c")
                                   if ops.get(k)))

    t = _first_image(data)
    if not t:
        print("   no raster tile to measure")
        return
    px_per_mm = t["w"] / max(1.0, float(re.search(r"(\d+(?:\.\d+)?)x(\d+(?:\.\d+)?)", "0x0") and 1 or 1))
    st = _ink_and_runs(t)
    print("   tile %dx%d  measured rows=%d  samples=%d" % (t["w"], t["h"], t["rows"], st["samples"]))
    print("   ink: darkness=%.1f/255  dark<128=%.2f%%  nonwhite<250=%.2f%%"
          % (st["ink_mean_darkness"], st["dark_pct"], st["nonwhite_pct"]))
    tot = sum(st["runs"].values())
    if tot:
        avg = sum(k * v for k, v in st["runs"].items()) / float(tot)
        print("   dark horizontal runs: n=%d  avg=%.2fpx  histogram=%s"
              % (tot, avg, sorted(st["runs"].items())[:10]))
    return t


if __name__ == "__main__":
    for p in sys.argv[1:]:
        report(p)
