import os, re, glob, json

OUT = r"e:\TRAE工作文件夹\DWG TO PDF\DwgToPdf 工具 v4.16\_对比探针\out"

def parse(path):
    rows = []
    with open(path, "r", encoding="latin-1") as f:
        lines = f.read().splitlines()
    d = {"file": os.path.basename(path)}
    model = {}
    g370m = {}
    g370curv = {}
    types = {}
    layers = []
    cur = None
    for ln in lines:
        s = ln.strip()
        if s.startswith("LAYOUT "):
            cur = s[7:]
            model.setdefault(cur, {})
            continue
        m = re.match(r"g(\d+) = (.*)$", s)
        if m and cur is not None:
            model[cur][int(m.group(1))] = m.group(2)
            continue
        if s.startswith("SYSVAR "):
            k, _, v = s[7:].partition(" = ")
            d[k] = v
        elif s.startswith("MODEL with370"):
            mo = re.search(r"with370 = (\d+) / without370 = (\d+)", s)
            if mo:
                d["model_with370"] = int(mo.group(1)); d["model_no370"] = int(mo.group(2))
        elif s.startswith("MODEL entities with440"):
            d["n440"] = int(s.split("=")[1])
        elif s.startswith("SSGET_X TOTAL"):
            d["total"] = int(s.split("=")[1])
        elif s.startswith("TYPE "):
            mo = re.match(r"TYPE (.*?) x(\d+)$", s)
            if mo: types[mo.group(1)] = int(mo.group(2))
        elif s.startswith("G370M ") and cur is None:
            mo = re.match(r"G370M (.*?) x(\d+)$", s)
            if mo: g370m[mo.group(1)] = int(mo.group(2))
        elif s.startswith("G370CURV "):
            mo = re.match(r"G370CURV (.*?) x(\d+)$", s)
            if mo: g370curv[mo.group(1)] = int(mo.group(2))
        elif s.startswith("LAYER ") and "|" in s:
            layers.append(s[6:])
    # G370M lines need tracking inside the section regardless of cur; re-scan
    g370m = {}
    g370curv = {}
    for ln in lines:
        s = ln.strip()
        mo = re.match(r"G370M (.*?) x(\d+)$", s)
        if mo: g370m[mo.group(1)] = int(mo.group(2))
        mo = re.match(r"G370CURV (.*?) x(\d+)$", s)
        if mo: g370curv[mo.group(1)] = int(mo.group(2))
    d["model_layout"] = model.get("Model", {})
    d["g370m"] = g370m
    d["g370curv"] = g370curv
    d["types"] = types
    d["layers"] = layers
    return d

files = sorted(glob.glob(os.path.join(OUT, "*.txt")))
res = [parse(f) for f in files]

def bit(v, b):
    try: return (int(v) >> b) & 1
    except: return "?"

print("idx file | CECOLOR | PSTYLEMODE | LWDEFAULT | LWDISPLAY | model g70 | 0x80 | 0x10 | 0x04 | g73/g74 | g76 | g4 | g7 | total | with370 | no370 | %no370 | n440 | LINE ELLIP ARC PLINE LWP OLE IMG HATCH")
for i, d in enumerate(res, 1):
    m = d["model_layout"]
    g70 = m.get(70, "?")
    tot = d.get("total", 0)
    w = d.get("model_with370", 0); n = d.get("model_no370", 0)
    pct = (100.0 * n / (w + n)) if (w + n) else 0
    t = d["types"]
    print("%2d %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %d | %d | %d | %.1f%% | %d | %d %d %d %d %d %d %d %d" % (
        i, d["file"][:34],
        d.get("CECOLOR", "?"), d.get("PSTYLEMODE", "?"), d.get("LWDEFAULT", "?"), d.get("LWDISPLAY", "?"),
        g70, bit(g70, 7), bit(g70, 4), bit(g70, 2),
        "%s/%s" % (m.get(73, "?"), m.get(74, "?")), m.get(76, "?"), m.get(4, "?"), m.get(7, "?"),
        tot, w, n, pct, d.get("n440", "?"),
        t.get("LINE", 0), t.get("ELLIPSE", 0), t.get("ARC", 0), t.get("POLYLINE", 0),
        t.get("LWPOLYLINE", 0), t.get("OLE2FRAME", 0), t.get("IMAGE", 0), t.get("HATCH", 0)))

print()
print("=== G370M histograms (model space entity lineweight) ===")
for i, d in enumerate(res, 1):
    print("%2d %-34s %s" % (i, d["file"][:34], d["g370m"]))
print()
print("=== LAYER TABLE rows ===")
for i, d in enumerate(res, 1):
    print("%2d %-34s %s" % (i, d["file"][:34], d["layers"][:8]))
print()
print("=== G370CURV ===")
for i, d in enumerate(res, 1):
    print("%2d %-34s %s" % (i, d["file"][:34], d["g370curv"]))
