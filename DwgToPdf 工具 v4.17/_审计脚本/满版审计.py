# -*- coding: utf-8 -*-
"""
满版审计 —— 用 PyMuPDF 渲染缩略图，统计墨迹（非白像素）覆盖范围，
计算四边留白，识别"图纸偏小/偏移/缺内容"问题。
用法:  python 满版审计.py <PDF目录或其父目录> [DPI]
依赖:  pip install pymupdf
输出: 控制台表格 + 同目录 满版审计.txt + 可疑张渲染 PNG 到 _满版可疑\
"""
import io, os, sys, datetime

try:
    import fitz  # PyMuPDF
except ImportError:
    print("缺少 PyMuPDF:  pip install pymupdf")
    sys.exit(2)

DPI_DEFAULT = 72
FLAG_RATIO = 0.60   # 墨迹包围盒占页面对边比 < 60% 视为"偏小"
FLAG_EDGE = 8.0     # 任一边留白 > 8% 页边视为"偏移"


def find_pdfs(root):
    if os.path.isfile(root) and root.lower().endswith(".pdf"):
        return [root]
    out = []
    for dirpath, _d, files in os.walk(root):
        for f in files:
            if f.lower().endswith(".pdf"):
                out.append(os.path.join(dirpath, f))
    return sorted(out)


def analyze(path, dpi):
    doc = fitz.open(path)
    page = doc[0]
    zoom = dpi / 72.0
    pix = page.get_pixmap(matrix=fitz.Matrix(zoom, zoom), colorspace=fitz.csGRAY)
    w, h = pix.width, pix.height
    buf = pix.samples  # 每像素 1 字节灰度
    minx, miny, maxx, maxy = w, h, -1, -1
    for y in range(h):
        row = buf[y * w:(y + 1) * w]
        # 找该行非白(<=245)像素
        xs = [i for i, v in enumerate(row) if v <= 245]
        if xs:
            if xs[0] < minx:
                minx = xs[0]
            if xs[-1] > maxx:
                maxx = xs[-1]
            if y < miny:
                miny = y
            maxy = y
    doc.close()
    if maxx < 0:
        return None  # 空白页
    ink_w = (maxx - minx + 1) / zoom
    ink_h = (maxy - miny + 1) / zoom
    pw, ph = page.rect.width, page.rect.height
    return {
        "page": (round(pw, 1), round(ph, 1)),
        "ink": (round(ink_w, 1), round(ink_h, 1)),
        "margins": (round(minx / zoom, 1), round(miny / zoom, 1),
                    round((w - 1 - maxx) / zoom, 1), round((h - 1 - maxy) / zoom, 1)),
        "ratio": (ink_w / pw, ink_h / ph),
    }


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else "."
    dpi = int(sys.argv[2]) if len(sys.argv) > 2 else DPI_DEFAULT
    pdfs = find_pdfs(root)
    if not pdfs:
        print("未找到 PDF: " + root)
        return 1
    out_dir = root if os.path.isdir(root) else os.path.dirname(os.path.abspath(root))
    sus_dir = os.path.join(out_dir, "_满版可疑")
    lines = []
    lines.append("满版审计  %s   DPI=%d   PDF=%d 张" %
                 (datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"), dpi, len(pdfs)))
    lines.append("%-46s %-14s %-14s %-22s %s" %
                 ("文件", "页面(mm当量)", "墨迹范围", "四边留白 L,T,R,B", "判定"))
    lines.append("-" * 120)
    sus = 0
    for p in pdfs:
        name = os.path.basename(p)
        try:
            r = analyze(p, dpi)
        except Exception as e:
            lines.append("%-46s 分析失败: %s" % (name[:46], e))
            sus += 1
            continue
        if r is None:
            lines.append("%-46s 空白页 <<<<" % name[:46])
            sus += 1
            if not os.path.isdir(sus_dir):
                os.makedirs(sus_dir)
            doc = fitz.open(p)
            doc[0].get_pixmap(matrix=fitz.Matrix(dpi / 72.0, dpi / 72.0)).save(
                os.path.join(sus_dir, os.path.splitext(name)[0] + ".png"))
            doc.close()
            continue
        flag = "OK"
        notes = []
        if r["ratio"][0] < FLAG_RATIO or r["ratio"][1] < FLAG_RATIO:
            flag = "偏小"
            notes.append("墨迹占比 %.0f%%x%.0f%%" % (r["ratio"][0] * 100, r["ratio"][1] * 100))
        ml, mt, mr, mb = r["margins"]
        pw, ph = r["page"]
        if max(ml, mt, mr, mb) / max(pw, ph) > FLAG_EDGE / 100.0:
            flag = flag if flag != "OK" else "偏移"
            notes.append("留白不均 L%.1f T%.1f R%.1f B%.1f" % (ml, mt, mr, mb))
        if flag != "OK":
            sus += 1
            if not os.path.isdir(sus_dir):
                os.makedirs(sus_dir)
            doc = fitz.open(p)
            doc[0].get_pixmap(matrix=fitz.Matrix(dpi / 72.0, dpi / 72.0)).save(
                os.path.join(sus_dir, os.path.splitext(name)[0] + ".png"))
            doc.close()
        lines.append("%-46s %-13s %-13s %-21s %s" %
                     (name[:46],
                      "%.0fx%.0f" % r["page"],
                      "%.0fx%.0f" % r["ink"],
                      "%.0f/%.0f/%.0f/%.0f" % (ml, mt, mr, mb),
                      flag + ("  " + "; ".join(notes) if notes else "")))
    lines.append("-" * 120)
    lines.append("可疑: %d 张（PNG 已存 _满版可疑\\）" % sus)
    text = "\n".join(lines)
    print(text)
    rpt = os.path.join(out_dir, "满版审计.txt")
    with io.open(rpt, "w", encoding="utf-8-sig") as fh:
        fh.write(text + "\n")
    print("\n报告: " + rpt)
    return 0


if __name__ == "__main__":
    sys.exit(main())
