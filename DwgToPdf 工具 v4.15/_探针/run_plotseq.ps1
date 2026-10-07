# run_plotseq.ps1 -- learn AutoCAD's -PLOT answer sequence WITHOUT looking at a screen.
#
# Why (measured 2026-10-07): driving -PLOT through SendCommand hangs, because every prompt
# waits for input and the operator cannot see the window (RPC_E_CALL_REJECTED once blocked).
# Manual ch.26 called this the "locked next step". But the prompt sequence does not need a
# screen: accoreconsole prints the prompts to stdout and stdout IS readable here. So
#   step 1 (this script): append answers, run, read the prompts, repeat until aligned
#   step 2: feed the validated full sequence to the VISIBLE AutoCAD in one SendCommand
#           -> nothing to get stuck on, no screen needed
#
# Traps already paid for (do not repeat):
#   - answers must come AFTER "-PLOT" and BEFORE any QUIT, else they are silently ignored
#   - accoreconsole stdout is UTF-8; decoding it as GBK yields mojibake ("}T销:" = "命令:")
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File .\run_plotseq.ps1 -Dwg "<path>" [-Answers ";;"]
param(
    [Parameter(Mandatory = $true)][string]$Dwg,
    [string]$Answers = "",
    [switch]$KeepTemp
)

$ErrorActionPreference = "Stop"
$accore = "D:\AutoCAD2026\AutoCAD 2026\accoreconsole.exe"
$root = "C:\Temp\dwgtopdf_plotseq"
New-Item -ItemType Directory -Path $root -Force | Out-Null
Remove-Item -LiteralPath "$root\*" -Recurse -Force -ErrorAction SilentlyContinue

$tag = [IO.Path]::GetFileNameWithoutExtension($Dwg) -replace '[^A-Za-z0-9_\-]', '_'
if ($tag.Length -gt 40) { $tag = $tag.Substring(0, 40) }
$work = Join-Path $root "$tag.dwg"
Copy-Item -LiteralPath $Dwg -Destination $work   # source never opened

# ---- page setup (same fields PlotSession sets) then -PLOT -----------------------------
$lsp = @'
(setq lay (cdr (assoc 350 (member (cons 3 "Model") (dictsearch (namedobjdict) "ACAD_LAYOUT")))))
(setq le (entget lay))
(setq le (subst (cons 4 "ISO_full_bleed_2A0_(1189.00_x_1682.00_MM)") (assoc 4 le) le))
(setq le (subst (cons 70 1) (assoc 70 le) le))
(setq le (subst (cons 73 1) (assoc 73 le) le))
(setq le (subst (cons 74 5) (assoc 74 le) le))
(entmod le)
(princ)
'@
Set-Content -LiteralPath "$root\setup.lsp" -Value $lsp -Encoding ASCII

$lines = @(
    'FILEDIA',
    '0',
    '(load "C:/Temp/dwgtopdf_plotseq/setup.lsp")',
    '(princ)',
    '-PLOT'
)
if ($Answers) {
    foreach ($a in ($Answers -split ';')) { if ($a -ne '') { $lines += $a } }
}
Set-Content -LiteralPath "$root\run.scr" -Value ($lines -join "`r`n") -Encoding ASCII

$sw = [Diagnostics.Stopwatch]::StartNew()
$p = Start-Process -FilePath $accore -ArgumentList "/i `"$work`" /s `"$root\run.scr`"" `
    -WorkingDirectory $root -NoNewWindow -Wait -PassThru `
    -RedirectStandardOutput "$root\out.txt" -RedirectStandardError "$root\err.txt"
$sw.Stop()
Write-Host ("[run] exit={0}  {1:N1}s  answers='{2}'" -f $p.ExitCode, $sw.Elapsed.TotalSeconds, $Answers)

$text = [IO.File]::ReadAllText("$root\out.txt", [Text.Encoding]::UTF8)
$clean = @($text -split "`r?`n" | Where-Object { $_ -and $_.Trim().Length -gt 0 })
Write-Host ""
Write-Host "===== prompt trail (last 30 non-empty lines) ====="
$clean | Select-Object -Last 30 | ForEach-Object { "   | $_" }

if (-not $KeepTemp) { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
