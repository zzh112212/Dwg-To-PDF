# -*- coding: utf-8 -*-
"""
DwgToPdf B1（注释批）空 catch 清单生成脚本  v1.2
================================================
用途（CQ-03）：按 PRD 既定规则「catch 行或上方 1 行无注释」产出**确定性清单**——
即 B1 需要补「为什么可以失败」注释的空 catch 位置表。

规则（与 prd-code-quality.md §CQ-03 逐字对齐）：
  1. 扫描范围（默认）：v4.17「源码工程」全部 .cs（自动排除 bin/ obj/）。
     v4.16 历史树与自测试工程不在 B1 范围（见设计文档 Anything UNCLEAR）。
     `--scope selftest` 可单列自测试工程参考；`--scope all` 全仓（含 v4.16）仅供核对。
  2. 空 catch 定义：catch 子句体为空的两种形态——
       一行式：  try { ... } catch { }          （同一行内闭合）
       多行式：  catch
                 {
                 }
     注意：catch (Exception ex) 带异常变量、或 catch 体内有语句的，均不计入。
  3. 注释判定（满足其一即「有注释」）：
        a) catch 行内有行尾注释（含 //）
        b) catch 行的**上方紧邻 1 行**是注释行（去除空白后以 // 开头）
     其余一律判「无注释」→ 进入 B1 待办清单。
  4. 排除清单（EXCLUDE，归 B2/CQ-05/06，避免与 B2 的 diff 重叠；每批可单独 revert）：
        BgLogger.cs:50                      —— logger 自身写盘失败（CQ-06 chicken-egg 注释）
        ConverterEngine.cs:404/437/580/1157/1188 —— envelope catch（CQ-06）
        RunHistory.cs:104/111                —— 历史读写静默（CQ-06 改记 BgLogger.Warn）
        MainForm.cs:1152                     —— 粘贴整体吞异常（CQ-06）
        MainForm.cs:1930                     —— 设置保存失败静默（CQ-06）
        EnvironmentSelfCheck.cs:58           —— 修复失败吞异常（CQ-06）
        FrameExtractor.cs:426/563            —— onFileDone UI 回调（CQ-05 改 BgLogger.Warn）
        LogWriter.cs:57/78/85                —— WriteLine/Dispose 写盘失败（CQ-02 加 LastWriteError）
  5. 输出：
        - 空 catch 总数与分文件分布（验收基线：v4.17 源码工程 = PRD 口径 140）
        - B1 待办清单（文件:catch 行号 + 形态 + 上下文预览）

用法：
    python 空catch清单.py [--scope v417-src|selftest|all] [--check]
      --check  仅输出机器可读汇总行（供验证步骤断言）：
                 TOTAL_EMPTY_CATCH / FLAGGED_ALL / EXCLUDED_B2 / B1_TODO

行尾/编码：本脚本实测为纯 LF 无 BOM（源 .cs 为 CRLF 无 BOM；读取用 utf-8-sig 兼容两者）。
已知局限：has_trailing_comment 用 "//" 行内查找，若字符串常量内含 "//" 会误判
           「有注释」（本仓该类情况为 0，脚本输出已逐条肉眼复核）。
"""

import io
import os
import re
import sys

PLANNING = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(PLANNING)  # .planning/ 的上级 = git 仓库根
PROJ = os.path.join(REPO, "DwgToPdf 工具 v4.17")
SRC = os.path.join(PROJ, "源码工程")
SELFTEST = os.path.join(PROJ, "自测试工程")

EXCLUDE = {
    # B2/CQ-05/06 站点；行号为 B1 注释批落地后的当前行号（B1 只在上方插入注释行，
    # 站点内容与冻结版 .planning/b1-空catch清单.txt 一一对应，仅行号平移）：
    ("BgLogger.cs", 51),
    ("ConverterEngine.cs", 405),
    ("ConverterEngine.cs", 438),
    ("ConverterEngine.cs", 584),
    ("ConverterEngine.cs", 1165),
    ("ConverterEngine.cs", 1199),
    ("RunHistory.cs", 105),
    ("RunHistory.cs", 112),
    ("MainForm.cs", 1157),
    ("MainForm.cs", 1937),
    ("EnvironmentSelfCheck.cs", 59),
    ("FrameExtractor.cs", 427),
    ("FrameExtractor.cs", 569),
    ("LogWriter.cs", 57),
    ("LogWriter.cs", 79),
    ("LogWriter.cs", 86),
}

# 一行式空 catch：整行内 catch { } 闭合
RE_ONELINE = re.compile(r"catch\s*\{\s*\}")
# 多行式起点：行以 catch 结尾，或 catch { 后无内容
RE_MULTI_START = re.compile(r"\bcatch\s*(\{\s*)?$")


def iter_cs_files(root):
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in ("bin", "obj", ".git")]
        for fn in filenames:
            if fn.endswith(".cs"):
                yield os.path.join(dirpath, fn)


def is_comment_line(line):
    return line.strip().startswith("//")


def has_trailing_comment(line):
    return line.find("//") >= 0


def scan_file(path):
    """返回 (empty_catches, todos)。todos = 无注释空 catch（未过排除）。"""
    with io.open(path, encoding="utf-8-sig") as f:
        lines = f.read().splitlines()
    name = os.path.basename(path)
    catches = []
    todos = []
    i = 0
    n = len(lines)
    while i < n:
        line = lines[i]
        if RE_ONELINE.search(line):
            catches.append(i + 1)
            commented = has_trailing_comment(line) or (i > 0 and is_comment_line(lines[i - 1]))
            if not commented and (name, i + 1) not in EXCLUDE:
                todos.append({"file": name, "line": i + 1, "form": "一行式",
                              "ctx": line.strip()[:80]})
            i += 1
            continue
        m = RE_MULTI_START.search(line)
        if m:
            # 形态①：catch 行自带 { —— 下一非空行须为 }
            # 形态②：catch 行不带 { —— 下一非空行须为 {，再下一非空行须为 }
            j = i + 1
            while j < n and lines[j].strip() == "":
                j += 1
            if j >= n:
                i += 1
                continue
            has_open = "{" in line
            if has_open:
                if lines[j].strip() == "}":
                    catches.append(i + 1)
                    commented = has_trailing_comment(line) or (i > 0 and is_comment_line(lines[i - 1]))
                    if not commented and (name, i + 1) not in EXCLUDE:
                        todos.append({"file": name, "line": i + 1, "form": "多行式",
                                      "ctx": line.strip()[:80]})
                    i = j + 1
                    continue
            else:
                if lines[j].strip() == "{":
                    k = j + 1
                    while k < n and lines[k].strip() == "":
                        k += 1
                    if k < n and lines[k].strip() == "}":
                        catches.append(i + 1)
                        commented = has_trailing_comment(line) or (i > 0 and is_comment_line(lines[i - 1]))
                        if not commented and (name, i + 1) not in EXCLUDE:
                            todos.append({"file": name, "line": i + 1, "form": "多行式",
                                          "ctx": line.strip()[:80]})
                        i = k + 1
                        continue
        i += 1
    return catches, todos


def main():
    args = sys.argv[1:]
    scope = "v417-src"
    for a in args:
        if a.startswith("--scope"):
            if "=" in a:
                scope = a.split("=", 1)[1]
            else:
                scope = args[args.index(a) + 1]
    if scope == "v417-src":
        roots = [SRC]
        excluded_allowed = True
    elif scope == "selftest":
        roots = [SELFTEST]
        excluded_allowed = False
    else:
        roots = [REPO]
        excluded_allowed = False

    total = 0
    per_file = {}
    all_todos = []
    excluded_hits = []
    for root in roots:
        for path in sorted(iter_cs_files(root)):
            catches, todos = scan_file(path)
            if catches:
                rel = os.path.relpath(path, REPO).replace("\\", "/")
                per_file[rel] = len(catches)
                total += len(catches)
            all_todos.extend(todos)
    if excluded_allowed:
        # 重新扫一遍把被排除项也列出来（审计可见性）
        for path in sorted(iter_cs_files(SRC)):
            with io.open(path, encoding="utf-8-sig") as f:
                lines = f.read().splitlines()
            name = os.path.basename(path)
            for (en, eln) in EXCLUDE:
                if en == name and 0 < eln <= len(lines):
                    ln = lines[eln - 1]
                    # 形态兼容：catch 在行首，或一行式 try { ... } catch { }（行尾式，v1.2 修）
                    if ln.strip().startswith("catch") or RE_ONELINE.search(ln):
                        excluded_hits.append("%s:%d" % (name, eln))

    check_mode = "--check" in args
    b1 = all_todos
    if check_mode:
        print("TOTAL_EMPTY_CATCH=%d" % total)
        print("B1_TODO=%d" % len(b1))
        print("EXCLUDED_B2=%d" % len(excluded_hits))
        sys.exit(0)

    print("== 扫描范围：%s ==" % ",".join(os.path.relpath(r, REPO).replace("\\", "/") for r in roots))
    print("== 空 catch 总数：%d（PRD 口径基线 140）==" % total)
    print("\n== 分文件分布 ==")
    for f, c in sorted(per_file.items(), key=lambda kv: -kv[1]):
        print("  %-72s %d" % (f, c))
    if excluded_allowed:
        print("\n== 已排除（归 B2/CQ-05/06）== %d 处" % len(excluded_hits))
        for e in excluded_hits:
            print("  " + e)
    print("\n== B1 待办清单（catch 行或上方 1 行无注释，且未归 B2）共 %d 处 ==" % len(b1))
    for t in b1:
        print("  %s:%-6d [%s]  %s" % (t["file"], t["line"], t["form"], t["ctx"]))


if __name__ == "__main__":
    main()
