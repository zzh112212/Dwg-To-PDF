# run_ole.ps1 -- decisive test: is OLE2FRAME really what forces the full-page raster?
#
# Background (measured 2026-10-07): AutoCAD's PDF driver rasterizes any region that
# cannot be expressed as PDF vectors. Drawings WITHOUT OLE2FRAME come out vector at every
# sheet size; drawings with OLE2FRAME come out raster, and the tile count tracks the OLE
# count (4 OLE -> 4 tiles, 7 OLE -> 7 tiles, PISCES 16 OLE -> 122 tiles = whole page).
#
# This probe runs the SAME -EXPORT window plot twice on a throw-away ASCII copy:
#   base   : nothing touched                -> expect raster (full page)
#   noole  : every OLE2FRAME set invisible  -> if the theory holds, this becomes vector
# OLE objects are made invisible with DXF group 60 = 1 (not erased, not exploded), which
# is exactly "do not plot me" - so this is also the shape a real fix would take.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File .\run_ole.ps1 -Dwg "<path to dwg>" [-KeepTemp]
param(
    [Parameter(Mandatory = $true)][string]$Dwg,
    [switch]$KeepTemp
)

$ErrorActionPreference = "Stop"
$accore = "D:\AutoCAD2026\AutoCAD 2026\accoreconsole.exe"
$root = "C:\Temp\dwgtopdf_ole"
$outDir = Join-Path $PSScriptRoot "out\ole"
if (-not (Test-Path $accore)) { throw "accoreconsole not found: $accore" }
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $root | Out-Null
$tag = [IO.Path]::GetFileNameWithoutExtension($Dwg)
$tag = ($tag -replace '[^A-Za-z0-9_\-]', '_')
if ($tag.Length -gt 40) { $tag = $tag.Substring(0, 40) }
$work = Join-Path $root "$tag.dwg"
Copy-Item -LiteralPath $Dwg -Destination $work       # source is never opened

# --- read the plot window from the drawing first (mirrors what the tool does) ---------
$probeLsp = Join-Path $root "probe.lsp"
@'
(setq PT_H (open "C:/Temp/dwgtopdf_ole/win.txt" "w"))
(write-line (strcat (vl-princ-to-string (getvar "EXTMIN")) "," (vl-princ-to-string (getvar "EXTMAX"))) PT_H)
(close PT_H)
(princ)
'@ | Set-Content -LiteralPath $probeLsp -Encoding ASCII

$scr1 = @(
    '(load "C:/Temp/dwgtopdf_ole/probe.lsp")',
    'QUIT', 'N'
) -join "`r`n"
Set-Content -LiteralPath "$root\read.scr" -Value $scr1 -Encoding ASCII
$p = Start-Process -FilePath $accore -ArgumentList "/i `"$work`" /s `"$root\read.scr`"" `
    -WorkingDirectory $root -NoNewWindow -Wait -PassThru `
    -RedirectStandardOutput "$root\read.out" -RedirectStandardError "$root\read.err"

$winLine = (Get-Content "$root\win.txt" -Encoding Default -ErrorAction SilentlyContinue | Select-Object -First 1)
if (-not $winLine) { throw "could not read EXTMIN/EXTMAX" }
Write-Host "[window] $winLine"
$nums = $winLine -split '[^0-9\.\-eE]+' | Where-Object { $_ -ne "" -and $_ -ne "-" }
$xmin = [double]$nums[0]; $ymin = [double]$nums[1]; $xmax = [double]$nums[2]; $ymax = [double]$nums[3]
Write-Host ("[window] x {0:N1}..{1:N1}   y {2:N1}..{3:N1}" -f $xmin, $xmax, $ymin, $ymax)

# --- the two variants ---------------------------------------------------------------
$lsp = Join-Path $root "ole.lsp"
@'
;; hide every OLE2FRAME by setting DXF group 60 = 1 (invisible => not plotted)
(defun p_hide_ole (/ PT_S PT_N PT_E)
  (setq PT_S (ssget "_X" '((0 . "OLE2FRAME"))))
  (if (null PT_S)
    (princ "NO-OLE")
    (progn
      (setq PT_N 0)
      (while (< PT_N (sslength PT_S))
        (setq PT_E (ssname PT_S PT_N))
        (entmod (append (entget PT_E) (list (cons 60 1))))
        (setq PT_N (1+ PT_N)))
      (entupd)
      (princ (strcat "HIDDEN-" (itoa PT_N))))))
'@ | Set-Content -LiteralPath $lsp -Encoding ASCII

$runs = @(
    @{ Name = "base";  Pre = '(princ "BASE")' },
    @{ Name = "noole"; Pre = '(load "C:/Temp/dwgtopdf_ole/ole.lsp")' }
)

foreach ($r in $runs) {
    $pdf = Join-Path $outDir ($tag + "__" + $r.Name + ".pdf")
    if (Test-Path $pdf) { Remove-Item $pdf -Force }
    $lines = @(
        'FILEDIA', '0',
        $r.Pre,
        '-EXPORT', 'PDF', 'W',
        ([string]::Format([CultureInfo]::InvariantCulture, "{0}", $xmin)),
        ([string]::Format([CultureInfo]::InvariantCulture, "{0}", $ymin)),
        ([string]::Format([CultureInfo]::InvariantCulture, "{0}", $xmax)),
        ([string]::Format([CultureInfo]::InvariantCulture, "{0}", $ymax)),
        '', '', 'QUIT', 'N'
    ) -join "`r`n"
    Set-Content -LiteralPath "$root\$($r.Name).scr" -Value $lines -Encoding ASCII
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $pp = Start-Process -FilePath $accore -ArgumentList "/i `"$work`" /s `"$root\$($r.Name).scr`"" `
        -WorkingDirectory $root -NoNewWindow -Wait -PassThru `
        -RedirectStandardOutput "$root\$($r.Name).out" -RedirectStandardError "$root\$($r.Name).err"
    $sw.Stop()
    $ok = Test-Path $pdf
    $sz = if ($ok) { (Get-Item $pdf).Length } else { 0 }
    Write-Host ("[{0,-6}] exit={1} {2:N1}s  {3:N0} bytes -> {4}" -f $r.Name, $pp.ExitCode, $sw.Elapsed.TotalSeconds, $sz, (Split-Path $pdf -Leaf))
}

if (-not $KeepTemp) { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
Write-Host ""
Write-Host "measure with:  python pdfruns.py <pdf>   -> 'vector content=...' large + 0 tiles == VECTOR"
