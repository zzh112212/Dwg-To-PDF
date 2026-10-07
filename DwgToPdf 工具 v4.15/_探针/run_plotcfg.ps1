# run_plotcfg.ps1 -- dump plot configuration of DWG(s) via accoreconsole.
#
# Usage (single file):
#   powershell -ExecutionPolicy Bypass -File ".\run_plotcfg.ps1" -Dwg "E:\path\PISCES_7633640.dwg"
# Usage (whole folder):
#   powershell -ExecutionPolicy Bypass -File ".\run_plotcfg.ps1" -Dir "E:\path\problem-folder"
#
# Discipline (see manual ch.8 / ch.3 3.4):
#   1) accoreconsole cannot handle non-ASCII paths -> copy each DWG to C:\Temp\dwgtopdf_probe\tag.dwg first.
#   2) Copying (instead of opening in place) ALSO protects the source DWG from being written back.
#   3) The .scr always answers "Y" to the QUIT prompt (discard changes) - see manual rule 11.
#   4) Paren balance of the .lsp is verified before running; result is pure-ASCII for easy diffing.
#
# NOTE: keep this file ASCII-only. PowerShell 5.1 reads BOM-less .ps1 as ANSI, so any
#       non-ASCII character here corrupts the script (same class of trap as GB2312 .lsp).
param(
    [string]$Dwg = "",
    [string]$Dir = "",
    [string]$Lsp = "",
    [string]$Func = "p_all",
    [switch]$KeepTemp
)

$ErrorActionPreference = "Stop"
$accore = "D:\AutoCAD2026\AutoCAD 2026\accoreconsole.exe"
$lsp = if ($Lsp) { $Lsp } else { Join-Path $PSScriptRoot "p_plotcfg.lsp" }
$root = "C:\Temp\dwgtopdf_probe"
$outDir = Join-Path $PSScriptRoot "out"

if (-not (Test-Path $accore)) { throw "accoreconsole not found: $accore" }

$targets = @()
if ($Dwg) { $targets = @(Get-Item -LiteralPath $Dwg) }
elseif ($Dir) { $targets = @(Get-ChildItem -LiteralPath $Dir -Filter *.dwg | Sort-Object Name) }
else { throw "give either -Dwg or -Dir" }

# --- paren balance check: delegated to lispcheck.py (single source of truth) ---
# Do NOT re-implement this in PowerShell: a naive ';' split miscounts when a string
# literal contains a semicolon (measured2026-10-07 on exactly that case).
$checker = Join-Path $PSScriptRoot "lispcheck.py"
if (Get-Command python -ErrorAction SilentlyContinue) {
    & python $checker $lsp
    if ($LASTEXITCODE -ne 0) { throw "lispcheck.py reported unbalanced parens in $lsp" }
} else {
    Write-Warning "python not found - skipping the paren balance precheck"
}

New-Item -ItemType Directory -Path $outDir -Force | Out-Null

foreach ($t in $targets) {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $root | Out-Null
    Copy-Item -LiteralPath $lsp -Destination "$root\p_plotcfg.lsp"

    $tag = [IO.Path]::GetFileNameWithoutExtension($t.Name)
    if ($tag.Length -gt 40) { $tag = $tag.Substring(0, 40) }
    $tag = ($tag -replace '[^A-Za-z0-9_\-]', '_')
    Copy-Item -LiteralPath $t.FullName -Destination "$root\$tag.dwg"

    $scr = @(
        '(setq PT_FH (open "C:/Temp/dwgtopdf_probe/out_plotcfg.txt" "w"))',
        '(load "C:/Temp/dwgtopdf_probe/p_plotcfg.lsp")',
        '(setq PT_R (vl-catch-all-apply (quote ' + $Func + ') nil))',
        '(if (vl-catch-all-error-p PT_R) (write-line (strcat "FATAL: " (vl-catch-all-error-message PT_R)) PT_FH))',
        '(close PT_FH)',
        'QUIT',
        'Y'
    ) -join "`r`n"
    Set-Content -LiteralPath "$root\run.scr" -Value $scr -Encoding ASCII

    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $accore `
        -ArgumentList "/i `"$root\$tag.dwg`" /s `"$root\run.scr`"" `
        -WorkingDirectory $root -NoNewWindow -PassThru -Wait `
        -RedirectStandardOutput "$root\accore_stdout.txt" `
        -RedirectStandardError "$root\accore_stderr.txt"
    $sw.Stop()
    Write-Host ("[probe] {0}  exit={1}  {2:N1}s" -f $t.Name, $p.ExitCode, $sw.Elapsed.TotalSeconds)

    if (Test-Path "$root\out_plotcfg.txt") {
        $dest = Join-Path $outDir ($tag + ".plotcfg.txt")
        Copy-Item "$root\out_plotcfg.txt" $dest -Force
        Write-Host "[probe] saved -> $dest"
        Get-Content $dest
    } else {
        Write-Warning "no out_plotcfg.txt produced. stdout tail:"
        Get-Content "$root\accore_stdout.txt" -Tail 25 -ErrorAction SilentlyContinue
    }
}

if (-not $KeepTemp) { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }