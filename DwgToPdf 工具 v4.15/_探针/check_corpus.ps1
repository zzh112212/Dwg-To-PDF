# check_corpus.ps1 -- corpus self-check BEFORE and AFTER any full regression.
#
# Why dynamic: the corpus changes whenever the user adds/removes drawings (2026-09-16
# 183 -> 166, 2026-10-07 166 -> 174 with 打印问题文件). Hard-coding a count made every
# run look like a regression, so the baseline is now a FILE next to this script and is
# re-registered explicitly with -Rebase (never implicitly).
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File .\check_corpus.ps1
#   powershell -ExecutionPolicy Bypass -File .\check_corpus.ps1 -Rebase
#   powershell -ExecutionPolicy Bypass -File .\check_corpus.ps1 -Root "E:\other-corpus"
#
# Exit code: 0 = matches baseline (or freshly rebased), 1 = mismatch / suspicious files.
param(
    [string]$Root = "",
    [switch]$Rebase
)

$ErrorActionPreference = "Stop"
if (-not $Root) {
    # _探针\ -> v4.15\ -> "DWG TO PDF"   (two levels up, NOT three)
    $Root = Split-Path (Split-Path $PSScriptRoot)
}
$corpus = Join-Path $Root "测试文件输入"
$baselineFile = Join-Path $PSScriptRoot "语料基线.txt"

if (-not (Test-Path $corpus)) { throw "corpus not found: $corpus" }

$all = @(Get-ChildItem $corpus -Recurse -Filter *.dwg -ErrorAction SilentlyContinue)
Write-Output ("dwg total = " + $all.Count)

# suspicious leftovers: accoreconsole crash recovery rewrites sources in place
$sus = @($all | Where-Object { $_.Name -match "recover|\.bak|~\$" })
if ($sus.Count -gt 0) {
    Write-Output "-- suspicious files (move them to _语料隔离 before trusting a run):"
    foreach ($f in $sus) {
        Write-Output ("   " + $f.Name + "  " + [math]::Round($f.Length / 1KB) + "KB  " + $f.LastWriteTime)
    }
}

if ($Rebase) {
    $lines = @("registered=" + (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), "count=" + $all.Count)
    # per-file sizes, so later runs can report ADDED / REMOVED / SIZE CHANGED instead of
    # just a total (a total alone cannot tell "user swapped drawings" from "count matches").
    foreach ($f in ($all | Sort-Object Name)) {
        $lines += ($f.Name + "`t" + $f.Length)
    }
    Set-Content -LiteralPath $baselineFile -Value $lines -Encoding UTF8
    Write-Output ("BASELINE RE-REGISTERED -> " + $baselineFile + "  count=" + $all.Count)
    exit 0
}

if (-not (Test-Path $baselineFile)) {
    Write-Output "no baseline registered yet - run with -Rebase to register the current corpus"
    exit 1
}

$base = @{}
foreach ($line in (Get-Content -LiteralPath $baselineFile -Encoding UTF8)) {
    # tolerant on purpose: PowerShell may fold the two header lines into one, and files use
    # either spaces or a tab before the size - a missed "count" line must not silently
    # disable the check, so match "count=<n>" anywhere instead of anchoring at line start.
    if ($line -match "count=(\d+)") { $base["count"] = [int]$Matches[1] }
    elseif ($line -match "^(.+\.dwg)[\s\t]+(\d+)$") { $base[$Matches[1]] = [int]$Matches[2] }
}

$ok = $true
if ($base.ContainsKey("count")) {
    if ($base["count"] -ne $all.Count) {
        $ok = $false
        Write-Output ("COUNT MISMATCH: baseline " + $base["count"] + " -> now " + $all.Count +
                      "  (added " + ($all.Count - $base["count"]) + " / removed " + ($base["count"] - $all.Count) + ")")
    } else {
        Write-Output ("count OK: " + $all.Count)
    }
}

# per-file size drift catches "the corpus was silently replaced" without hashing 170 files
$curNames = @{}
foreach ($f in $all) { $curNames[$f.Name] = $f.Length }
foreach ($name in $base.Keys) {
    if ($name -eq "count") { continue }
    if (-not $curNames.ContainsKey($name)) {
        $ok = $false
        Write-Output ("REMOVED: " + $name)
    } elseif ($curNames[$name] -ne $base[$name]) {
        $ok = $false
        Write-Output ("SIZE CHANGED: " + $name + "  " + $base[$name] + " -> " + $curNames[$name] +
                      "  (" + [math]::Round(($curNames[$name] - $base[$name]) / 1KB, 1) + " KB)")
    }
}
foreach ($name in $curNames.Keys) {
    if (-not $base.ContainsKey($name)) { Write-Output ("ADDED:   " + $name) }
}

if ($sus.Count -gt 0) { $ok = $false; Write-Output "CORPUS CHECK FAILED - suspicious leftovers present" }
elseif ($ok) { Write-Output "CORPUS OK" }
else { Write-Output "CORPUS CHECK FAILED - inspect above" }
exit $(if ($ok) { 0 } else { 1 })
