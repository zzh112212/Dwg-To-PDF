# -*- coding: utf-8 -*-
"""
渲染预览 —— 把 PDF 批量渲染成 PNG，便于人工查看。
用法:  python 渲染预览.py <PDF目录> [DPI] [输出目录]
依赖:  pip install pymupdf
"""
import os, sys

try:
    import fitz  # PyMuPDF
except ImportError:
    print("缺少 PyMuPDF:  pip install pymupdf")
    sys.exit(2)


def find_pdfs(root):
    if os.path.isfile(root) and root.lower().endswith(".pdf"):
        return [root]
    out = []
    for dirpath, _d, files in os.walk(root):
        for f in files:
            if f.lower().endswith(".pdf"):
                out.append(os.path.join(dirpath, f))
    return sorted(out)


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else "."
    dpi = int(sys.argv[2]) if len(sys.argv) > 2 else 120
    out = sys.argv[3] if len(sys.argv) > 3 else os.path.join(
        root if os.path.isdir(root) else os.path.dirname(os.path.abspath(root)), "PNG预览")
    os.makedirs(out, exist_ok=True)
    pdfs = find_pdfs(root)
    if not pdfs:
        print("未找到 PDF: " + root)
        return 1
    ok = 0
    for p in pdfs:
        try:
            doc = fitz.open(p)
            doc[0].get_pixmap(matrix=fitz.Matrix(dpi / 72.0, dpi / 72.0)).save(
                os.path.join(out, os.path.splitext(os.path.basename(p))[0] + ".png"))
            doc.close()
            ok += 1
        except Exception as e:
            print("失败 %s: %s" % (os.path.basename(p), e))
    print("渲染 %d/%d 张 -> %s (DPI=%d)" % (ok, len(pdfs), out, dpi))
    return 0


if __name__ == "__main__":
    sys.exit(main())
