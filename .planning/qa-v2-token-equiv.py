# -*- coding: utf-8 -*-
"""
QA 独立 V2 token 等价校验（qw 复现，不信任工程师的 token等价校验.py）
====================================================================
判据（design-b1.md 附录 C）：对 B1 变更的 18 个源码 .cs，
    git show 65d5fe4:<path>   vs   工作区文件
剥除全部注释（// 行/行尾、/* */ 块，字符串字面量内的 // 与 /* 不算注释）
并压平全部空白后，逐字符必须完全相等。

本脚本与工程师版本的差异（独立性保证）：
  1. 自带 C#-lite 词法分析器：正确处理普通字符串 "..."（\\ 转义）、
     逐字字符串 @"..."（"" 转义）、插值字符串 $"..."（{{}} 转义 + {洞} 括号配平，
     洞内嵌套字符串）、字符字面量 '...'——注释识别只在代码态生效。
  2. 顺带核查「字符串内无 // 与 /*」这一前提假设（两侧分别统计）。
  3. 顺带做字节稳态检查（无 BOM + 纯 CRLF，GEN-WIN-003/004）。
  4. 仅依据 git 对象 65d5fe4 与工作区文件，不读任何 .planning 产物。

用法：python3 qa-v2-token-equiv.py
退出码：0 = 18/18 等价；1 = 存在不等价。
"""
import io
import os
import subprocess
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BASE = "65d5fe4"

FILES = [
    "DwgToPdf 工具 v4.17/源码工程/BgLogger.cs",
    "DwgToPdf 工具 v4.17/源码工程/CadConverter.cs",
    "DwgToPdf 工具 v4.17/源码工程/ComStarter.cs",
    "DwgToPdf 工具 v4.17/源码工程/Concurrency.cs",
    "DwgToPdf 工具 v4.17/源码工程/ConverterEngine.Plot.cs",
    "DwgToPdf 工具 v4.17/源码工程/ConverterEngine.cs",
    "DwgToPdf 工具 v4.17/源码工程/CoreConsoleLocator.cs",
    "DwgToPdf 工具 v4.17/源码工程/EnvironmentSelfCheck.cs",
    "DwgToPdf 工具 v4.17/源码工程/FrameCache.cs",
    "DwgToPdf 工具 v4.17/源码工程/FrameExtractor.cs",
    "DwgToPdf 工具 v4.17/源码工程/LogWriter.cs",
    "DwgToPdf 工具 v4.17/源码工程/MainForm.PhaseLog.cs",
    "DwgToPdf 工具 v4.17/源码工程/MainForm.cs",
    "DwgToPdf 工具 v4.17/源码工程/PageSetupPreprocessor.cs",
    "DwgToPdf 工具 v4.17/源码工程/PhaseChipBar.cs",
    "DwgToPdf 工具 v4.17/源码工程/PlotSession.cs",
    "DwgToPdf 工具 v4.17/源码工程/RasterPlotterFactory.cs",
    "DwgToPdf 工具 v4.17/源码工程/RunHistory.cs",
]


def git_show(path):
    r = subprocess.run(
        ["git", "show", "%s:%s" % (BASE, path)],
        cwd=REPO, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if r.returncode != 0:
        raise RuntimeError("git show failed: %s" % r.stderr.decode("utf-8", "replace"))
    return r.stdout.decode("utf-8-sig")


def read_worktree(path):
    with io.open(os.path.join(REPO, path), encoding="utf-8-sig") as f:
        return f.read()


class Lexer(object):
    """C#-lite：返回 (剥注释后文本, 字符串字面量内 // 与 /* 计数)。"""

    def __init__(self):
        self.out = []
        self.str_slashslash = 0
        self.str_slashstar = 0

    def run(self, text):
        i, n = 0, len(text)
        while i < n:
            c = text[i]
            # 注释（代码态）
            if c == "/" and i + 1 < n and text[i + 1] == "/":
                j = text.find("\n", i)
                i = n if j < 0 else j
                self.out.append(" ")
                continue
            if c == "/" and i + 1 < n and text[i + 1] == "*":
                j = text.find("*/", i + 2)
                if j < 0:
                    i = n
                else:
                    i = j + 2
                self.out.append(" ")
                continue
            # 逐字字符串 @"
            if c == "@" and i + 1 < n and text[i + 1] == '"':
                i = self._verbatim(text, i + 2)
                continue
            # 插值字符串 $"
            if c == "$" and i + 1 < n and text[i + 1] == '"':
                i = self._interpolated(text, i + 2)
                continue
            # $@" 或 @$"
            if c in "$@" and i + 2 < n and text[i + 1] in "$@" and text[i + 2] == '"':
                i = self._interp_verbatim(text, i + 3)
                continue
            # 普通字符串
            if c == '"':
                i = self._regular(text, i + 1)
                continue
            # 字符字面量
            if c == "'":
                i = self._char(text, i + 1)
                continue
            self.out.append(c)
            i += 1
        return "".join(self.out)

    def _count(self, s):
        self.str_slashslash += s.count("//")
        self.str_slashstar += s.count("/*")

    def _regular(self, text, i):
        buf = []
        n = len(text)
        while i < n:
            if text[i] == "\\" and i + 1 < n:
                buf.append(text[i:i + 2])
                i += 2
                continue
            if text[i] == '"':
                self._count("".join(buf))
                self.out.append('"' + "".join(buf) + '"')
                return i + 1
            buf.append(text[i])
            i += 1
        raise ValueError("unterminated regular string")

    def _verbatim(self, text, i):
        buf = []
        n = len(text)
        while i < n:
            if text[i] == '"':
                if i + 1 < n and text[i + 1] == '"':
                    buf.append('""')
                    i += 2
                    continue
                self._count("".join(buf))
                self.out.append('@"' + "".join(buf) + '"')
                return i + 1
            buf.append(text[i])
            i += 1
        raise ValueError("unterminated verbatim string")

    def _char(self, text, i):
        buf = []
        n = len(text)
        while i < n:
            if text[i] == "\\" and i + 1 < n:
                buf.append(text[i:i + 2])
                i += 2
                continue
            if text[i] == "'":
                self._count("".join(buf))
                self.out.append("'" + "".join(buf) + "'")
                return i + 1
            buf.append(text[i])
            i += 1
        raise ValueError("unterminated char literal")

    def _interp_verbatim(self, text, i):
        # $@"..." / @$"..."：逐字 + 插值洞（洞内允许嵌套普通串）
        buf = []
        n = len(text)
        depth = 0
        while i < n:
            ch = text[i]
            if depth == 0:
                if ch == '"':
                    if i + 1 < n and text[i + 1] == '"':
                        buf.append('""')
                        i += 2
                        continue
                    self._count("".join(buf))
                    self.out.append('$@"' + "".join(buf) + '"')
                    return i + 1
                if ch == "{" and i + 1 < n and text[i + 1] == "{":
                    buf.append("{{")
                    i += 2
                    continue
                if ch == "}" and i + 1 < n and text[i + 1] == "}":
                    buf.append("}}")
                    i += 2
                    continue
                if ch == "{":
                    depth = 1
                    buf.append("{")
                    i += 1
                    continue
                buf.append(ch)
                i += 1
            else:
                if ch == '"':
                    j = self._regular(text, i + 1)
                    buf.append("...")
                    i = j
                    continue
                if ch == "{":
                    depth += 1
                    buf.append("{")
                    i += 1
                    continue
                if ch == "}":
                    depth -= 1
                    buf.append("}")
                    i += 1
                    continue
                buf.append(ch)
                i += 1
        raise ValueError("unterminated interpolated verbatim string")

    def _interpolated(self, text, i):
        buf = []
        n = len(text)
        depth = 0
        while i < n:
            ch = text[i]
            if depth == 0:
                if ch == "\\" and i + 1 < n:
                    buf.append(text[i:i + 2])
                    i += 2
                    continue
                if ch == '"':
                    self._count("".join(buf))
                    self.out.append('$"' + "".join(buf) + '"')
                    return i + 1
                if ch == "{" and i + 1 < n and text[i + 1] == "{":
                    buf.append("{{")
                    i += 2
                    continue
                if ch == "}" and i + 1 < n and text[i + 1] == "}":
                    buf.append("}}")
                    i += 2
                    continue
                if ch == "{":
                    depth = 1
                    buf.append("{")
                    i += 1
                    continue
                buf.append(ch)
                i += 1
            else:
                if ch == '"':
                    j = self._regular(text, i + 1)
                    buf.append("...")
                    i = j
                    continue
                if ch == "{":
                    depth += 1
                    buf.append("{")
                    i += 1
                    continue
                if ch == "}":
                    depth -= 1
                    buf.append("}")
                    i += 1
                    continue
                buf.append(ch)
                i += 1
        raise ValueError("unterminated interpolated string")


def normalize(text):
    lx = Lexer()
    code = lx.run(text)
    flat = " ".join(code.split())
    return flat, (lx.str_slashslash, lx.str_slashstar)


def byte_steadiness(raw_bytes, side):
    """side='worktree'：无 BOM + 纯 CRLF（GEN-WIN-003/004 工作区稳态）。
    side='blob'：无 BOM + 纯 LF（本仓 core.autocrlf=true，对象库稳态）。"""
    problems = []
    if raw_bytes[:3] == b"\xef\xbb\xbf":
        problems.append("has BOM")
    lf = raw_bytes.count(b"\n")
    crlf = raw_bytes.count(b"\r\n")
    if side == "worktree":
        if crlf != lf or crlf == 0:
            problems.append("not pure CRLF: crlf=%d lf=%d" % (crlf, lf))
    else:
        if crlf != 0 or lf == 0:
            problems.append("not pure LF blob: crlf=%d lf=%d" % (crlf, lf))
    return problems


def first_diff(a, b):
    n = min(len(a), len(b))
    for i in range(n):
        if a[i] != b[i]:
            lo = max(0, i - 40)
            return i, a[lo:i + 40], b[lo:i + 40]
    if len(a) != len(b):
        return n, a[n - 40:n + 40], b[n - 40:n + 40]
    return -1, "", ""


def main():
    all_pass = True
    for path in FILES:
        try:
            old_blob = subprocess.run(
                ["git", "cat-file", "blob", "%s:%s" % (BASE, path)],
                cwd=REPO, stdout=subprocess.PIPE).stdout
            with open(os.path.join(REPO, path), "rb") as f:
                new_raw = f.read()
            old = old_blob.decode("utf-8-sig")
            new = new_raw.decode("utf-8-sig")
            o_flat, o_cnt = normalize(old)
            n_flat, n_cnt = normalize(new)
            ok = (o_flat == n_flat)
            bs = byte_steadiness(new_raw, "worktree")
            bs_old = byte_steadiness(old_blob, "blob")
            status = "PASS" if ok and not bs and not bs_old else "FAIL"
            if not ok:
                i, ctx_a, ctx_b = first_diff(o_flat, n_flat)
                status += " first_diff@%d old=[%s] new=[%s]" % (i, ctx_a, ctx_b)
            if o_cnt != (0, 0) or n_cnt != (0, 0):
                status += " WARN str-lit-slash old=%s new=%s" % (o_cnt, n_cnt)
            if bs or bs_old:
                status += " byte-issues new=%s old=%s" % (bs, bs_old)
            if status != "PASS":
                all_pass = False
            print("%-46s %s" % (os.path.basename(path), status))
        except Exception as e:
            all_pass = False
            print("%-46s ERROR %s" % (os.path.basename(path), e))
    print("V2_RESULT=%s" % ("PASS" if all_pass else "FAIL"))
    sys.exit(0 if all_pass else 1)


if __name__ == "__main__":
    main()
