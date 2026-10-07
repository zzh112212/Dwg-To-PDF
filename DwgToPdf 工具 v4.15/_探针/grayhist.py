# grayhist.py -- is the ink BLACK or GRAY?  ("黑变浅了" = the user's own words)
#
# Why: pdfink.py says our raster product has MORE dark pixels (ink<128 = 3.15%) than the
# user's vector reference (2.90%), yet the user reports the print got LIGHTER. That can only
# happen if the line pixels are not black: a rasteriser spreads one thin black line over
# several grey pixels (anti-aliasing). Grey pixels are NOT counted by ink<128, and they
# print as light grey -> the line reads as "浅" even though the page has more ink.
# Vector output (hairline) prints crisp black instead.
#
# So the metric that matters is the grey histogram of the inked pixels:
#   core black (<32) = solid line, prints black
#   dark   (32-96)   = good
#   mid(96-160)      = getting grey
#   light (160-224)   = washed out -> this is what "变浅" looks like
#
# Usage: python grayhist.py <file.pdf> [more.pdfs...]
import sys
import fitz  # PyMuPDF, same dependency pdfink.py already uses

DPI = 150
BUCKETS = [(0, 32, "core black <32"), (32, 96, "dark 32-96"), (96, 160, "mid 96-160"),
           (160, 224, "light 160-224"), (224, 256, "paper >=224")]


def hist(path):
    doc = fitz.open(path)
    pg = doc[0]
    pm = pg.get_pixmap(dpi=DPI, colorspace=fitz.csGRAY)
    data = pm.samples
    counts = [0] * len(BUCKETS)
    for b in data:
        for i, (lo, hi, _) in enumerate(BUCKETS):
            if lo <= b < hi:
                counts[i] += 1
                break
    total = float(len(data)) or 1.0
    print("== %s" % path.rsplit("\\", 1)[-1].rsplit("/", 1)[-1])
    print("   page %.0fx%.0f pt   rendered %d px @%d dpi" % (pg.rect.width, pg.rect.height, len(data), DPI))
    for i, (_, _, name) in enumerate(BUCKETS):
        pct = 100.0 * counts[i] / total
        bar = "#" * int(pct * 2)
        print("   %-16s %8.4f%%  %s" % (name, pct, bar))
    inked = total - counts[-1]
    if inked > 0:
        # average darkness over the inked area, and the share of "washed out" ink pixels
        washed = 100.0 * counts[3] / inked
        print("   inked area = %.4f%% of page; of that, %.1f%% are light-grey(160-224)"
              % (100.0 * inked / total, washed))
    print()
    doc.close()


if __name__ == "__main__":
    for p in sys.argv[1:]:
        hist(p)
