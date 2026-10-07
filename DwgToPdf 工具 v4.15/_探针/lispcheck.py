"""lispcheck.py -- paren/quote balance checker for AutoLISP sources.

Usage:
    python lispcheck.py file1.lsp [file2.lsp ...]

Exits 1 and prints the offending line numbers when a file is unbalanced.
Understands: ";" line comments, AutoLISP string literals with \" escapes,
and the doubled-quote form "" used to embed a quote inside a string.
"""
import sys


def check(path: str) -> int:
    with open(path, encoding="utf-8", errors="replace") as fh:
        lines = fh.read().split("\n")

    depth = 0
    stack = []
    in_str = False
    trace = {}

    for lineno, raw in enumerate(lines, 1):
        code = []
        i = 0
        in_str = False
        while i < len(raw):
            ch = raw[i]
            if in_str:
                if ch == '"':
                    if i + 1 < len(raw) and raw[i + 1] == '"':
                        i += 2
                        continue
                    in_str = False
                i += 1
                continue
            if ch == '"':
                in_str = True
                i += 1
                continue
            if ch == ";":
                break
            code.append(ch)
            i += 1

        for ch in code:
            if ch == "(":
                depth += 1
                stack.append(lineno)
            elif ch == ")":
                depth -= 1
                trace[lineno] = depth
                if depth < 0:
                    print("%s: EXTRA ')' at line %d" % (path, lineno))
                    print("%s: depth trace around the failure:" % path)
                    for ln in range(max(1, lineno - 8), min(len(lines), lineno + 2) + 1):
                        mark = ">>" if ln == lineno else "  "
                        print("   %s %4d d=%s | %s" % (mark, ln, trace.get(ln, "?"), lines[ln - 1][:100]))
                    return 1
                if stack:
                    stack.pop()

    if depth != 0:
        print("%s: unbalanced, final depth=%d, unclosed '(' opened at lines: %s"
              % (path, depth, stack))
        return 1

    print("%s: OK (%d lines)" % (path, len(lines)))
    return 0


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    rc = 0
    for p in sys.argv[1:]:
        rc |= check(p)
    sys.exit(rc)