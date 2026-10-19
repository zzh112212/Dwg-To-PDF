# -*- coding: utf-8 -*-
"""
纸型审计 —— 纯标准库，不依赖第三方包。
用法:  python 纸型审计.py <PDF目录或其父目录>
递归扫描目录下所有 PDF，读取 /MediaBox 换算成毫米，
与标准幅面对照，标出非标/可疑纸型（含容差 3mm）。
输出: 控制台表格 + 同目录生成 纸型审计.txt
"""
import io, os, re, sys, datetime

MM = 25.4 / 72.0
TOL = 3.0

# 常用标准幅面（短边 x 长边，mm）
STD = {
    "4A0":  (1682.0, 2378.0),
    "2A0":  (1189.0, 1682.0),
    "A0":   (841.0, 1189.0),
    "A1":   (594.0, 841.0),
    "A2":   (420.0, 594.0),
    "A3":   (297.0, 420.0),
    "A4":   (210.0, 297.0),
}

MB_RE = re.compile(rb"/MediaBox\s*\[\s*([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)")

def mediabox_mm(path):
    raw = open(path, "rb").read()
    m = MB_RE.search(raw)
    if not m:
        return None
    x0, y0, x1, y1 = (float(g) for g in m.groups())
    w = abs(x1 - x0) * MM
    h = abs(y1 - y0) * MM
    return round(min(w, h), 1), round(max(w, h), 1)

def classify(short, long_):
    for name, (sw, lw) in STD.items():
        if abs(short - sw) <= TOL and abs(long_ - lw) <= TOL:
            return name
    return "非标?"

def find_pdfs(root):
    if os.path.isfile(root) and root.lower().endswith(".pdf"):
        return [root]
    out = []
    for dirpath, _dirs, files in os.walk(root):
        for f in files:
            if f.lower().endswith(".pdf"):
                out.append(os.path.join(dirpath, f))
    return sorted(out)

def main():
    root = sys.argv[1] if len(sys.argv) > 1 else "."
    pdfs = find_pdfs(root)
    if not pdfs:
        print("未找到 PDF: " + root)
        return 1
    lines = []
    lines.append("纸型审计  %s" % datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"))
    lines.append("扫描目录: %s   PDF 共 %d 张" % (os.path.abspath(root), len(pdfs)))
    lines.append("%-52s %-16s %-6s %s" % ("文件", "MediaBox(mm)", "判定", "备注"))
    lines.append("-" * 100)
    bad = 0
    for p in pdfs:
        try:
            box = mediabox_mm(p)
        except Exception as e:
            box = None
            lines.append("%-52s 读取失败: %s" % (os.path.basename(p)[:52], e))
            bad += 1
            continue
        if box is None:
            lines.append("%-52s %-16s 无MediaBox" % (os.path.basename(p)[:52], ""))
            bad += 1
            continue
        short, long_ = box
        name = classify(short, long_)
        note = "" if name != "非标?" else "← 核对是否预期"
        if name == "非标?":
            bad += 1
        lines.append("%-52s %-13s  %-6s %s" % (os.path.basename(p)[:52],
                                               "%.0fx%.0f" % (short, long_), name, note))
    lines.append("-" * 100)
    lines.append("非标/异常: %d 张" % bad)
    text = "\n".join(lines)
    print(text)
    rpt = os.path.join(os.path.dirname(os.path.abspath(pdfs[0])) or ".", "纸型审计.txt")
    if os.path.isdir(root):
        rpt = os.path.join(root, "纸型审计.txt")
    with io.open(rpt, "w", encoding="utf-8-sig") as fh:
        fh.write(text + "\n")
    print("\n报告: " + rpt)
    return 0

if __name__ == "__main__":
    sys.exit(main())
