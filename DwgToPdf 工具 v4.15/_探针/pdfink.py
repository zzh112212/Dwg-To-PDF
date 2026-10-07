#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""pdfink.py -- render PDFs and measure how dark the sheet actually looks.

Why (2026-10-07):
    The whole "打印发浅" investigation hinges on a visual claim. Measuring PDF
    operators (pdfruns.py) shows *what* is written, but not how dark the page
    ends up. Rendering the page and averaging pixel darkness answers the
    acceptance question directly: "is A darker than B?".

What it reports, per PDF:
    - page size (pt / mm)
    - ink<128   : share of pixels darker than mid-grey   (thick strokes => up)
    - mean ink  : average (255 - grey) over the whole page, 0..255
    - dark pixels count, at the chosen DPI

Usage:
    python pdfink.py [--dpi 150] file1.pdf file2.pdf ...
"""
import sys

try:
    import fitz  # PyMuPDF
except ImportError:
    sys.exit("PyMuPDF (fitz) is required:  python -m pip install pymupdf")


def measure(path, dpi):
    doc = fitz.open(path)
    page = doc[0]
    w_pt, h_pt = page.rect.width, page.rect.height
    # render with anti-aliasing off is NOT wanted: the eye sees AA output,
    # and AA is exactly how a hairline ends up looking lighter than a 0.35mm stroke.
    pix = page.get_pixmap(dpi=dpi, colorspace=fitz.csGRAY, alpha=False)
    buf = pix.samples
    n = len(buf)
    dark = 0
    total = 0
    for v in buf:
        total += 255 - v
        if v < 128:
            dark += 1
    doc.close()
    return {
        "path": path,
        "w_pt": w_pt, "h_pt": h_pt,
        "w_mm": w_pt * 25.4 / 72.0, "h_mm": h_pt * 25.4 / 72.0,
        "px": n,
        "dark": dark,
        "dark_pct": 100.0 * dark / n if n else 0.0,
        "mean_ink": total / float(n) if n else 0.0,
    }


def main(argv):
    dpi = 150
    files = []
    i = 0
    while i < len(argv):
        if argv[i] == "--dpi":
            i += 1
            dpi = int(argv[i])
        else:
            files.append(argv[i])
        i += 1
    if not files:
        sys.exit(__doc__)

    print("dpi = %d" % dpi)
    rows = []
    for f in files:
        try:
            r = measure(f, dpi)
        except Exception as e:  # noqa: BLE001 - diagnostic tool
            print("!! %s -> %s" % (f, e))
            continue
        rows.append(r)
        name = f.split("\\")[-1].split("/")[-1]
        print("")
        print("== %s" % name)
        print("   page    = %.0fx%.0f pt (%.0fx%.0f mm)" % (r["w_pt"], r["h_pt"], r["w_mm"], r["h_mm"]))
        print("   pixels  = %d" % r["px"])
        print("   ink<128 = %.4f%%   (%d px)" % (r["dark_pct"], r["dark"]))
        print("   mean ink= %.4f / 255" % r["mean_ink"])

    if len(rows) > 1:
        print("")
        print("--- ranking by mean ink (darker = higher) ---")
        for r in sorted(rows, key=lambda x: -x["mean_ink"]):
            name = r["path"].split("\\")[-1].split("/")[-1]
            print("   %8.4f   %s" % (r["mean_ink"], name))


if __name__ == "__main__":
    main(sys.argv[1:])
