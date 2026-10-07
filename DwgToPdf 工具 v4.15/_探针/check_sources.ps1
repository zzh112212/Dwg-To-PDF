# check_sources.ps1 -- acceptance criterion #3: "source drawings must not be modified".
#
# Why this script exists (2026-10-07, TRAE finding):
#   The old baseline file _探针\out\源图纸哈希_改前.txt was PROVEN WRONG -- 3 of its 8
#   hashes do not match the authoritative originals, i.e. it recorded some other revision.
#   That single bad file produced two opposite false verdicts:
#     * TRAE reported "3 source drawings were rewritten" (wrong: only 7633639 was, and its
#       baseline entry was itself not the original), and
#     * the manual (ch.27) recorded "8/8 MD5 unchanged" (wrong reasoning: it compared against
#       the same untrustworthy file).
#   Lesson: a hash baseline is only worth anything if it was captured from a KNOWN-GOOD source.
#   So this script does NOT read a stored hash table at all -- it compares the working copies
#   against the folder the user handed over (E:\钉钉下载\DWG), live, every time. It can never drift.
#
# Related trap this exposed (same root cause, second instance):
#   _探针\语料基线.txt stores per-file byte sizes, so restoring the originals also invalidated
#   it (3 files). Whenever source drawings are legitimately restored/replaced, re-register with
#   check_corpus.ps1 -Rebase and say WHY in the file header -- otherwise the next run reports
#   a phantom "SIZE CHANGED" regression.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File .\check_sources.ps1
#   powershell -ExecutionPolicy Bypass -File .\check_sources.ps1 -Original "E:\other\baseline"
#
# NOTE: this script only READS bytes (Get-FileHash). It never opens the drawings in AutoCAD.
#
# Exit code: 0 = all match / 1 = mismatch, missing or extra file / 2 = baseline folder absent.
param(
    [string]$Original = "E:\钉钉下载\DWG",
    [string]$Test = ""
)

$ErrorActionPreference = "Stop"
if (-not $Test) {
    # _探针\ -> "DwgToPdf 工具 v4.15" -> "DWG TO PDF"  (two levels up)
    $root = Split-Path (Split-Path $PSScriptRoot)
    $Test = Join-Path $root "测试文件输入\打印问题文件"
}
if (-not (Test-Path -LiteralPath $Original)) {
    Write-Output ("BASELINE NOT FOUND: " + $Original)
    Write-Output "请用 -Original 指定权威原件目录（这批图纸的原件是 E:\钉钉下载\DWG）。"
    exit 2
}
if (-not (Test-Path -LiteralPath $Test)) {
    Write-Output ("WORKING COPY NOT FOUND: " + $Test)
    exit 2
}

$orig = @{}
foreach ($f in (Get-ChildItem -LiteralPath $Original -Filter *.dwg -File)) {
    $orig[$f.Name] = @{
        hash = (Get-FileHash -LiteralPath $f.FullName -Algorithm MD5).Hash
        size = $f.Length
    }
}
$work = @(Get-ChildItem -LiteralPath $Test -Filter *.dwg -File | Sort-Object Name)

Write-Output ("baseline : " + $Original + "  (" + $orig.Count + " dwg)")
Write-Output ("working  : " + $Test + "  (" + $work.Count + " dwg)")
Write-Output ""

$bad = 0
foreach ($f in $work) {
    if (-not $orig.ContainsKey($f.Name)) {
        Write-Output ("EXTRA     " + $f.Name + "  (not in baseline)")
        $bad++
        continue
    }
    $h = (Get-FileHash -LiteralPath $f.FullName -Algorithm MD5).Hash
    $o = $orig[$f.Name]
    if ($h -eq $o.hash -and $f.Length -eq $o.size) {
        Write-Output ("OK        " + $f.Name + "  " + $f.Length + " B")
    } else {
        Write-Output ("MODIFIED  " + $f.Name)
        Write-Output ("            working  " + $f.Length + " B  " + $h)
        Write-Output ("            original " + $o.size + " B  " + $o.hash)
        $bad++
    }
}
foreach ($name in ($orig.Keys | Sort-Object)) {
    if (-not (Get-ChildItem -LiteralPath $Test -Filter $name -File)) {
        Write-Output ("MISSING   " + $name + "  (in baseline, missing from working copy)")
        $bad++
    }
}

Write-Output ""
if ($bad -eq 0) {
    Write-Output ("SOURCES UNCHANGED  (" + $work.Count + "/" + $orig.Count + " match the authoritative originals)")
    exit 0
}
Write-Output ("SOURCES CHANGED  (" + $bad + " problem(s)) -- 违反硬约束，请从 " + $Original + " 恢复")
exit 1
