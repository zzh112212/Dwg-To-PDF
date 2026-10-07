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
$locked = 0
foreach ($f in $work) {
    if (-not $orig.ContainsKey($f.Name)) {
        Write-Output ("EXTRA     " + $f.Name + "  (not in baseline)")
        $bad++
        continue
    }
    # MEASURED 2026-10-07: a DWG being converted right now is LOCKED by AutoCAD/accoreconsole
    # and Get-FileHash then throws "cannot read ... being used by another process".
    # That is NOT evidence of modification -- reporting it as "MODIFIED" would be a false alarm
    # on the very hard constraint we care most about. So locked files get their own verdict
    # (LOCKED, counted separately) and a dedicated exit code.
    $h = $null
    $isLocked = $false
    try { $h = (Get-FileHash -LiteralPath $f.FullName -Algorithm MD5 -ErrorAction Stop).Hash }
    catch { $isLocked = $true }
    if ($isLocked) {
        Write-Output ("LOCKED    " + $f.Name + "  (正被 AutoCAD/转换进程占用，本次无法判定 —— 不等于被改写)")
        $locked++
        continue
    }
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
if ($locked -gt 0) {
    Write-Output ("SOURCES NOT FULLY CHECKED  (" + $locked + " 个文件被占用，无法判定)")
    Write-Output "  被占用通常是 AutoCAD / accoreconsole 正在转换或打开该图纸。关闭后重跑本脚本。"
    Write-Output "  ⚠ 占用**不是**被改写的证据；但在占用消失前，这些图既不能确认安全、也不能判定被改。"
}
if ($bad -eq 0 -and $locked -eq 0) {
    Write-Output ("SOURCES UNCHANGED  (" + $work.Count + "/" + $orig.Count + " match the authoritative originals)")
    exit 0
}
if ($bad -eq 0 -and $locked -gt 0) {
    # 单独退出码：这一轮"没发现问题"，但也没"完成检查"。脚本不能假装通过了。
    exit 3
}
Write-Output ("SOURCES CHANGED  (" + $bad + " problem(s)) -- 违反硬约束，请从 " + $Original + " 恢复")
exit 1
