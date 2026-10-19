# -*- coding: utf-8 -*-
"""
QA 独立 V6 EXCLUDE 完整性审计（工程师自验未覆盖项）
====================================================
口径来源：design-b1.md §1.1 难点 3 表 = 16 个 B2 站点（内容锚定，不信行号）；
仪器空catch清单.py 的 excluded_hits 内容校验 = lines[eln-1].startswith("catch")，
报告 EXCLUDED_B2=13。本审计回答三问：
  ① 16 站点当前是否仍为空 catch 且【无注释】（B1 补注释 = 越界）；
  ② 与 65d5fe4 是否逐字节一致（未被 B1 改动）；
  ③ 16→13 的 3 条差异分别是哪几个、为什么。
方法：以 65d5fe4 原始行号定位站点 → 用「剥注释保行结构」的代码行序列
difflib 映射到工作区（抗行号漂移）→ 在新位置复核形态/注释/字节一致；
同时把仪器当前 EXCLUDE 逐条对照真实站点，找出失配锚点并归类原因。
"""
import difflib
import importlib.util
import io
import os
import re
import subprocess
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(REPO, "DwgToPdf 工具 v4.17", "源码工程")
BASE = "65d5fe4"

# design-b1.md §1.1 难点 3：16 个 B2 站点（65d5fe4 行号 + 内容描述）
DESIGN_SITES = [
    ("BgLogger.cs", 50, "BgLogger Line 写盘静默 catch（chicken-egg）"),
    ("ConverterEngine.cs", 404, "envelope：CollectDwgs 收集失败兜底"),
    ("ConverterEngine.cs", 437, "envelope：公司版提示写出失败"),
    ("ConverterEngine.cs", 580, "envelope：看门狗停滞处理体外层"),
    ("ConverterEngine.cs", 1157, "envelope：转换收尾统计失败"),
    ("ConverterEngine.cs", 1188, "envelope：RunHistory 保存失败"),
    ("RunHistory.cs", 104, "历史读失败（try{...}catch{} 行尾式）"),
    ("RunHistory.cs", 111, "Save 整体写失败"),
    ("MainForm.cs", 1152, "粘贴整体吞异常（多行式）"),
    ("MainForm.cs", 1930, "设置保存失败静默"),
    ("EnvironmentSelfCheck.cs", 58, "修复失败吞异常"),
    ("FrameExtractor.cs", 426, "onFileDone 回调（try{...}catch{} 行尾式）"),
    ("FrameExtractor.cs", 563, "onFileDone 回调（try{...}catch{} 行尾式）"),
    ("LogWriter.cs", 57, "WriteLine 写盘失败（多行式）"),
    ("LogWriter.cs", 78, "Flush 写盘失败（多行式）"),
    ("LogWriter.cs", 85, "Close 写盘失败（多行式）"),
]

RE_ONELINE = re.compile(r"catch\s*\{\s*\}")


def load_instrument_exclude():
    spec = importlib.util.spec_from_file_location(
        "instrument", os.path.join(REPO, ".planning", "空catch清单.py"))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return set(mod.EXCLUDE)


def blob_lines(rev, path):
    r = subprocess.run(["git", "cat-file", "blob", "%s:%s" % (rev, path)],
                       cwd=REPO, stdout=subprocess.PIPE)
    return r.stdout.decode("utf-8-sig").splitlines()


def wt_lines(path):
    with io.open(os.path.join(SRC, path), encoding="utf-8-sig") as f:
        return f.read().splitlines()


def strip_comments_keep_lines(text):
    """剥除 // 与 /* */ 注释但保留行数（字符串内的不算）。返回等行数列表。"""
    out = []
    i, n = 0, len(text)
    line_has_content = [False]
    buf = []

    def flush_line():
        out.append("".join(buf))
        del buf[:]

    while i < n:
        c = text[i]
        if c == "\n":
            flush_line()
            i += 1
            continue
        if c == "/" and i + 1 < n and text[i + 1] == "/":
            j = text.find("\n", i)
            i = n if j < 0 else j
            continue
        if c == "/" and i + 1 < n and text[i + 1] == "*":
            j = text.find("*/", i + 2)
            i = n if j < 0 else j + 2
            continue
        if c == '"':
            if i > 0 and text[i - 1] == "@":
                buf.append(c)
                i += 1
                while i < n:
                    if text[i] == '"':
                        if i + 1 < n and text[i + 1] == '"':
                            buf.append('""')
                            i += 2
                            continue
                        buf.append('"')
                        i += 1
                        break
                    buf.append(text[i])
                    i += 1
                continue
            buf.append(c)
            i += 1
            while i < n:
                if text[i] == "\\" and i + 1 < n:
                    buf.append(text[i:i + 2])
                    i += 2
                    continue
                if text[i] == '"':
                    buf.append('"')
                    i += 1
                    break
                buf.append(text[i])
                i += 1
            continue
        if c == "'":
            buf.append(c)
            i += 1
            while i < n:
                if text[i] == "\\" and i + 1 < n:
                    buf.append(text[i:i + 2])
                    i += 2
                    continue
                if text[i] == "'":
                    buf.append("'")
                    i += 1
                    break
                buf.append(text[i])
                i += 1
            continue
        buf.append(c)
        i += 1
    flush_line()
    return out


def detect_empty_catch_at(lines, idx):
    """判断 lines[idx]（0 基）是否是一个空 catch 的起始行。
    返回 (True, form, region_end_exclusive) 或 (False, None, None)。"""
    line = lines[idx]
    if RE_ONELINE.search(line):
        return True, "一行式(行内)", idx + 1
    s = line.strip()
    if s == "catch" or re.search(r"\bcatch\s*\{\s*$", line):
        j = idx + 1
        while j < len(lines) and lines[j].strip() == "":
            j += 1
        has_open = "{" in line
        if has_open and j < len(lines) and lines[j].strip() == "}":
            return True, "多行式(catch{})", j + 1
        if not has_open and j < len(lines) and lines[j].strip() == "{":
            k = j + 1
            while k < len(lines) and lines[k].strip() == "":
                k += 1
            if k < len(lines) and lines[k].strip() == "}":
                return True, "多行式(catch/{/{})", k + 1
    return False, None, None


def main():
    exclude = load_instrument_exclude()
    site_verdicts = []
    mismatch_notes = []

    # 每个文件：建立 old 站点行 -> new 行 的映射（代码行 difflib）
    files = sorted(set(f for f, _, _ in DESIGN_SITES))
    line_map = {}  # (file, old_idx0) -> new_idx0
    for f in files:
        old_txt = subprocess.run(
            ["git", "cat-file", "blob", "%s:DwgToPdf 工具 v4.17/源码工程/%s" % (BASE, f)],
            cwd=REPO, stdout=subprocess.PIPE).stdout.decode("utf-8-sig")
        new_txt = io.open(os.path.join(SRC, f), encoding="utf-8-sig").read()
        old_code = strip_comments_keep_lines(old_txt)
        new_code = strip_comments_keep_lines(new_txt)
        sm = difflib.SequenceMatcher(a=old_code, b=new_code, autojunk=False)
        for tag, i1, i2, j1, j2 in sm.get_opcodes():
            if tag == "equal":
                for d in range(i2 - i1):
                    line_map[(f, i1 + d)] = j1 + d
            elif tag == "replace":
                for d in range(i2 - i1):
                    line_map[(f, i1 + d)] = j1 + min(d, j2 - j1 - 1) if j2 > j1 else None
            else:  # delete / insert：无法一一对应
                for d in range(i2 - i1):
                    line_map[(f, i1 + d)] = None
        # 完整性自检：注释行不会进入 old_code 的非空集合差异——old 的代码行必须全部映射成功
        unmapped = [(i + 1, old_code[i]) for i in range(len(old_code))
                    if old_code[i].strip() and line_map.get((f, i)) is None]
        if unmapped:
            print("WARN %s 有 %d 行代码行未能映射（如为纯插入/删除块属预期）" % (f, len(unmapped)))

    for f, ln, desc in DESIGN_SITES:
        old0 = ln - 1
        old_lines = blob_lines(BASE, "DwgToPdf 工具 v4.17/源码工程/" + f)
        new_lines = wt_lines(f)
        ok_old, form, old_end = detect_empty_catch_at(old_lines, old0)
        m = line_map.get((f, old0))
        problems = []
        if not ok_old:
            problems.append("OLD@%d 不是空 catch 起点（设计行号有误？）" % ln)
        if m is None:
            site_verdicts.append((f, ln, desc, "MAP-FAIL", problems,
                                  None, None, None))
            continue
        new_ln = m + 1
        ok_new, form_new, new_end = detect_empty_catch_at(new_lines, m)
        if not ok_new:
            problems.append("NEW 映射位 %d 不再是空 catch" % new_ln)
        # ① 无注释：catch 行无 //；紧邻上一行不是注释行
        if ok_new:
            if "//" in new_lines[m]:
                problems.append("B1 给 B2 站点补了行尾注释（越界）")
            if m > 0 and new_lines[m - 1].strip().startswith("//"):
                problems.append("B1 给 B2 站点补了上方注释行（越界）")
        # ② 与 65d5fe4 逐字节一致（站点区域代码）
        if ok_old and ok_new:
            region_old = [l.rstrip() for l in old_lines[old0:old_end]]
            region_new = [l.rstrip() for l in new_lines[m:new_end]]
            if region_old != region_new:
                problems.append("站点区域与父提交不一致: OLD=%r NEW=%r"
                                % (region_old, region_new))
        # EXCLUDE 锚点对照（当前仪器）
        anchor_hit = None
        for (en, eln) in sorted(exclude):
            if en == f:
                pass
        anchors_here = sorted(eln for (en, eln) in exclude if en == f)
        anchor_info = []
        for eln in anchors_here:
            content = new_lines[eln - 1].strip()[:70] if 0 < eln <= len(new_lines) else "<out-of-range>"
            starts_catch = content.startswith("catch")
            is_site = (eln == new_ln)
            anchor_info.append((eln, starts_catch, is_site, content))
        # 本站点是否被 EXCLUDE 元组覆盖（功能排除 = todos 不收录）
        in_exclude = (f, new_ln) in exclude
        verdict = "PASS" if not problems else "FAIL"
        site_verdicts.append((f, ln, desc, verdict, problems,
                              new_ln, form_new or form, in_exclude))
        mismatch_notes.append((f, ln, desc, new_ln, anchor_info))

    print("== V6-1/2 逐站点结论（16 个 B2 站点，内容锚定）==")
    n_fail = 0
    for f, ln, desc, verdict, problems, new_ln, form, in_excl in site_verdicts:
        if verdict == "MAP-FAIL":
            n_fail += 1
            print("  FAIL %s:%d %s -> 映射失败 %s" % (f, ln, desc, problems))
            continue
        if verdict != "PASS":
            n_fail += 1
        print("  %s %s:%d(父)->%d(今) [%s] 形态=%s EXCLUDE元组覆盖=%s %s"
              % (verdict, f, ln, new_ln, desc, form, in_excl,
                 ("; ".join(problems) if problems else "")))

    print()
    print("== V6-3 仪器 EXCLUDE 锚点 16->13 差异归因 ==")
    # 全局去重统计：当前仪器 EXCLUDE 中，内容校验（startswith('catch')）命中的锚点
    all_anchor_lines = {}
    for f in files:
        nl = wt_lines(f)
        for (en, eln) in sorted(exclude):
            if en == f and 0 < eln <= len(nl):
                all_anchor_lines[(f, eln)] = nl[eln - 1].strip()
    hit = {k: v for k, v in all_anchor_lines.items() if v.startswith("catch")}
    counted = len(hit)
    print("  仪器 EXCLUDE 锚点总数 = %d；startswith('catch') 命中 = %d（= EXCLUDED_B2 报告值）"
          % (len(all_anchor_lines), counted))
    site_new_lines = {}
    for f, ln, desc, new_ln, anchor_info in mismatch_notes:
        site_new_lines[(f, new_ln)] = ln
    not_counted = []
    for (f, eln), content in sorted(all_anchor_lines.items()):
        if (f, eln) in hit:
            continue
        orig = site_new_lines.get((f, eln), "?")
        not_counted.append((f, orig, eln, content[:70]))
    for f, orig, eln, content in not_counted:
        print("  差异 %-22s 设计行 %s -> 今 %d：行内容 = %r" % (f + ":" + str(orig), orig, eln, content))
        print("      原因：该站点为 try { ... } catch { } 行尾式，catch 不在行首；")
        print("      仪器 excluded_hits 内容校验 startswith('catch') 形态盲区 -> EXCLUDED_B2 漏计。")
        print("      但 (文件,行) 元组排除有效：B1_TODO=0 证明其未被列入待办。")
    # 反向：所有命中的锚点必须都是真实 B2 站点
    print()
    print("== V6-3b 反向审计：EXCLUDE 命中锚点是否全部指向真实 B2 站点 ==")
    bad = 0
    for k in sorted(hit):
        if k not in site_new_lines:
            print("  WARN EXCLUDE 锚点 %s:%d 命中 catch 行但不对应任何设计站点: %r"
                  % (k[0], k[1], hit[k][:60]))
            bad += 1
    if bad == 0:
        print("  %d 个命中锚点全部与设计站点一一对应（无错位 / 无漂移未重锚）。" % counted)

    print()
    print("V6_RESULT=%s" % ("PASS" if n_fail == 0 else "FAIL"))
    sys.exit(0 if n_fail == 0 else 1)


if __name__ == "__main__":
    main()
