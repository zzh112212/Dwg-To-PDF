# -*- coding: utf-8 -*-
"""
 DwgToPdf B1（注释批）V2 零可执行变更 token 等价校验 v1.0
=========================================================
规格（design-b1.md 附录 C）：对每个变更 .cs，取 `git show HEAD:<path>` 与工作区版本，
剥除全部注释（// 行/行尾、/* */ 块）并压平所有空白后，两侧 token 流必须完全相等。
多行式压行（catch/{/} 三行 → catch { } 一行）前后 token 完全一致，由该校验兜底放行。

用法：
    python token等价校验.py            # 逐文件 PASS/FAIL + 首个分歧位置
    python token等价校验.py --quiet    # 仅输出汇总
退出码：全部 PASS=0，否则=1。
"""

import os
import re
import subprocess
import sys

PLANNING = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(PLANNING)


def git_show(path):
    """取 HEAD 版本文本（CRLF 原样）。"""
    out = subprocess.check_output(
        ["git", "show", "HEAD:%s" % path], cwd=REPO)
    return out.decode("utf-8")


def read_worktree(path):
    with open(os.path.join(REPO, path), "rb") as f:
        return f.read().decode("utf-8")


def normalize(text):
    """剥注释（/* */ 块、// 行/行尾）+ 压平全部空白。本仓字符串字面量内无 // 与 /*（已核）。"""
    text = re.sub(r"/\*.*?\*/", " ", text, flags=re.S)   # 块注释
    text = re.sub(r"//[^\n]*", " ", text)                # 行注释
    return re.sub(r"\s+", " ", text).strip()


def first_diff(a, b):
    """返回首个分歧位置（字符索引）与两侧上下文。"""
    n = min(len(a), len(b))
    for i in range(n):
        if a[i] != b[i]:
            lo = max(0, i - 30)
            return i, a[lo:i + 30], b[lo:i + 30]
    if len(a) != len(b):
        return n, a[n:n + 60], b[n:n + 60]
    return None


def changed_cs_files():
    out = subprocess.check_output(
        ["git", "-c", "core.quotepath=false", "diff", "--name-only", "-z"],
        cwd=REPO).decode("utf-8")
    files = [f for f in out.split("\0") if f]
    return [f for f in files if f.endswith(".cs")]


def main():
    quiet = "--quiet" in sys.argv
    files = changed_cs_files()
    if not files:
        print("没有变更的 .cs 文件")
        return 0
    all_pass = True
    for path in files:
        old = normalize(git_show(path))
        new = normalize(read_worktree(path))
        if old == new:
            if not quiet:
                print("PASS  %s  (tokens=%d)" % (path, len(old)))
        else:
            all_pass = False
            d = first_diff(old, new)
            print("FAIL  %s" % path)
            if d:
                i, ca, cb = d
                print("  首个分歧 @%d" % i)
                print("  HEAD: …%s…" % ca)
                print("  工作: …%s…" % cb)
    print("----")
    print("V2 RESULT: %s（%d 个变更 .cs）" % ("ALL PASS" if all_pass else "FAIL", len(files)))
    return 0 if all_pass else 1


if __name__ == "__main__":
    sys.exit(main())
