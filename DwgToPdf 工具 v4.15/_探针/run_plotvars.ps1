# run_plotvars.ps1 -- plot one DWG several times, changing ONE print setting per run,
# then measure each PDF with pdfink.py. This is how "which setting makes the plot come
# out light" gets answered with data instead of guesses.
#
# Variants are "lw:shade" pairs:
#   lw    -> LWDISPLAY value written before -EXPORT (0 = do not plot lineweights,
#            1 = plot lineweights using each object's / layer's lineweight)
#   shade -> DXF group 146 of the Model layout plot settings
#            (0 = as displayed, 1 = rendered as displayed, 2 = hidden line removal)
#            or the literal "keep" to leave 146 exactly as the drawing has it
#
# Every run uses the SAME plot window (from EXTMIN/EXTMAX), so any difference in the
# measured output is attributable only to the variant.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File .\run_plotvars.ps1 -Dwg "E:\...\7633640.dwg"
#   powershell -ExecutionPolicy Bypass -File .\run_plotvars.ps1 -Dwg "..." -Variants "0:keep,1:keep,0:1,0:2"
#
# Discipline: the DWG is COPIED to an ASCII temp dir first (accoreconsole cannot take
# Chinese paths) and every script answers Y to QUIT, so the source is never touched.
param(
    [string]$Dwg = "",
    [string]$Variants = "0:keep,1:keep,0:1,0:2,1:1,1:2",
    [switch]$KeepTemp
)

$ErrorActionPreference = "Stop"
$accore = "D:\AutoCAD2026\AutoCAD 2026\accoreconsole.exe"
$root = "C:\Temp\dwgtopdf_plotvars"
$outDir = Join-Path $PSScriptRoot "out\plotvars"

if (-not $Dwg) { throw "give -Dwg" }
if (-not (Test-Path $accore)) { throw "accoreconsole not found" }

$src = Get-Item -LiteralPath $Dwg
$tag = [IO.Path]::GetFileNameWithoutExtension($src.Name)
if ($tag.Length -gt 40) { $tag = $tag.Substring(0, 40) }
$tag = ($tag -replace '[^A-Za-z0-9_\-]', '_')

Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $root | Out-Null
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

$asciiDwg = "$root\$tag.dwg"
Copy-Item -LiteralPath $src.FullName -Destination $asciiDwg
Write-Host ("[vars] source={0}  {1:N1}MB  (copied to ASCII temp - source never opened)" -f $src.Name, ($src.Length / 1MB))

function Invoke-Accore([string]$scrText, [string]$label) {
    $scr = "$root\$label.scr"
    Set-Content -LiteralPath $scr -Value $scrText -Encoding ASCII
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $accore `
        -ArgumentList "/i `"$asciiDwg`" /s `"$scr`"" `
        -WorkingDirectory $root -NoNewWindow -PassThru -Wait `
        -RedirectStandardOutput "$root\$label.out" -RedirectStandardError "$root\$label.err"
    $sw.Stop()
    Write-Host ("[vars]   {0} exit={1} {2:N1}s" -f $label, $p.ExitCode, $sw.Elapsed.TotalSeconds)
}

# ---- step 1: read EXTMIN/EXTMAX so every variant shares one window -----------------
Copy-Item (Join-Path $PSScriptRoot "p_caps.lsp") "$root\p_caps.lsp"
Invoke-Accore @'
(setq PT_FH (open "C:/Temp/dwgtopdf_plotvars/extents.txt" "w"))
(load "C:/Temp/dwgtopdf_plotvars/p_caps.lsp")
(p_extents)
(close PT_FH)
QUIT
Y
'@ "extents"

$extTxt = "$root\extents.txt"
if (-not (Test-Path $extTxt)) { throw "extents probe produced nothing" }
$mnx = $null; $mny = $null; $mxx = $null; $mxy = $null
foreach ($line in (Get-Content -LiteralPath $extTxt)) {
    if ($line -match 'EXTMIN=\(([-0-9.eE]+)\s+([-0-9.eE]+)') { $mnx = [double]$Matches[1]; $mny = [double]$Matches[2] }
    if ($line -match 'EXTMAX=\(([-0-9.eE]+)\s+([-0-9.eE]+)') { $mxx = [double]$Matches[1]; $mxy = [double]$Matches[2] }
}
if ($null -eq $mnx) { throw "could not parse extents from $extTxt" }
# small margin so nothing sits exactly on the window border
$mnx = $mnx - 5; $mny = $mny - 5; $mxx = $mxx + 5; $mxy = $mxy + 5
$win = "{0},{1}" -f $mnx, $mny
$win2 = "{0},{1}" -f $mxx, $mxy
Write-Host ("[vars] plot window {0} -> {1}   frame {2:N1} x {3:N1} mm" -f $win, $win2, ($mxx - $mnx), ($mxy - $mny))

# ---- step 2: one plot per variant ---------------------------------------------------
foreach ($v in ($Variants -split ',')) {
    $parts = $v.Trim() -split ':'
    $lw = $parts[0]
    $shade = $parts[1]
    $label = "lw$lw`_sh$shade"

    if ($shade -eq "keep") {
        $shadeLine = "; shade-plot (146) kept as in drawing"
    } else {
        $shadeLine = '(setq le (subst (cons 146 ' + $shade + ') (assoc 146 le) le))(princ)'
    }

    # MEASURED 2026-10-07: group 70 bit 0x80 (128) is "plot with lineweights".
    # Proof: with COM Layout.PlotWithLineweights = true the Model layout's group 70 goes
    # 11828 -> 11956 (+128) and LWDISPLAY stays 0, so the layout flag - not the sysvar -
    # is what the plot engine reads. The shipping script only does
    # logior(logand(70 -17) 4) (clear 0x10, set 0x04) and therefore leaves 0x80 as found.
    $lwBitLine = '(setq le (subst (cons 70 (logior (logand (cdr (assoc 70 le)) -129) ' + $(if ($lw -eq "1") { "128" } else { "0" }) + ')) (assoc 70 le) le))(princ)'

    $scr = @"
FILEDIA
0
LWDISPLAY
$lw
;variant=$label
(setsetq d (dictsearch (namedobjdict) "ACAD_LAYOUT") lay (cdr (assoc 350 (member (cons 3 "Model") d))) le (entget lay))(princ)
$shadeLine
$lwBitLine
(entmod le)(princ)
-EXPORT
PDF
W
$win
$win2


QUIT
Y
"@

    Invoke-Accore $scr $label

    $produced = @(Get-ChildItem -LiteralPath $root -Filter *.pdf | Sort-Object LastWriteTimeUtc)
    if ($produced.Count -eq 0) {
        Write-Warning "[vars]   $label produced NO pdf"
        continue
    }
    $dest = Join-Path $outDir ($tag + "__" + $label + ".pdf")
    Move-Item -LiteralPath $produced[-1].FullName -Destination $dest -Force
    Write-Host ("[vars]   -> {0} ({1:N0} bytes)" -f (Split-Path $dest -Leaf), (Get-Item $dest).Length)
}

# ---- step 3: measure ---------------------------------------------------------------
Write-Host ""
Write-Host "================ MEASUREMENT (pdfink.py) ================"
$pdfs = @(Get-ChildItem -LiteralPath $outDir -Filter *.pdf | Sort-Object Name | ForEach-Object { $_.FullName })
if ($pdfs.Count -gt 0) {
    & python (Join-Path $PSScriptRoot "pdfink.py") $pdfs
} else {
    Write-Warning "no PDFs to measure"
}

if (-not $KeepTemp) { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
