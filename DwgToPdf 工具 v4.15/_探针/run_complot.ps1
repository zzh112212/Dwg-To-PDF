# run_complot.ps1 -- A/B the COM (AutoCAD GUI) plot channel against the accoreconsole
# -EXPORT channel, and measure what each produces with pdfink.py.
#
# Why: the shipping tool plots PISCES 2A0 sheets through accoreconsole -EXPORT, and the
# result is RASTER-ONLY (measured 2026-10-07 on a production PDF: 61 image tiles,
# ~296 dpi effective, zero vector ops, Producer "pdfplot17.hdi"). Thin linework in a
# raster page is anti-aliased into pale grey -> the user's "打印发浅（文字可读但线条浅）".
# The COM channel has a display context, so it is the candidate for a darker output.
#
# Variants are "lw:shade":
#   lw    -> Layout.PlotLineweights  (false = what the tool sets today, true = the
#             drawing's / layer lineweights are plotted)
#   shade -> Layout.ShadePlot  0 = as displayed, 1 = rendered as displayed,
#             2 = hidden line removal
#
# Every variant uses the SAME plot window and paper, so only the tested setting differs.
#
# Usage:
#   powershell -STA -ExecutionPolicy Bypass -File .\run_complot.ps1 -Dwg "E:\...\7633640.dwg"
#   powershell -STA -ExecutionPolicy Bypass -File .\run_complot.ps1 -Dwg "..." -Variants "false:0,true:0,true:1"
#
# Discipline: the DWG is COPIED to an ASCII temp dir first and opened FROM the copy, so
# the user's source drawing cannot be written back. AutoCAD is quit at the end.
param(
    [string]$Dwg = "",
    [string]$Variants = "false:0,true:0,true:1,true:2,false:1",
    [double]$MarginMm = 0.0,
    [switch]$KeepTemp
)

$ErrorActionPreference = "Stop"
$root = "C:\Temp\dwgtopdf_complot"
$outDir = Join-Path $PSScriptRoot "out\complot"
$pdfink = Join-Path $PSScriptRoot "pdfink.py"

if (-not $Dwg) { throw "give -Dwg" }
$src = Get-Item -LiteralPath $Dwg
$tag = [IO.Path]::GetFileNameWithoutExtension($src.Name)
if ($tag.Length -gt 40) { $tag = $tag.Substring(0, 40) }
$tag = ($tag -replace '[^A-Za-z0-9_\-]', '_')

Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $root | Out-Null
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$work = "$root\$tag.dwg"
Copy-Item -LiteralPath $src.FullName -Destination $work
Write-Host ("[com] source={0} {1:N1}MB -> copied to {2}" -f $src.Name, ($src.Length / 1MB), $work)

# ---- read EXTMIN/EXTMAX via accoreconsole (same helper as run_plotvars) --------------
$accore = "D:\AutoCAD2026\AutoCAD 2026\accoreconsole.exe"
Copy-Item (Join-Path $PSScriptRoot "p_caps.lsp") "$root\p_caps.lsp"
Set-Content -LiteralPath "$root\ext.scr" -Encoding ASCII -Value @(
    '(setq PT_FH (open "C:/Temp/dwgtopdf_complot/extents.txt" "w"))',
    '(load "C:/Temp/dwgtopdf_complot/p_caps.lsp")',
    '(p_extents)',
    '(close PT_FH)',
    'QUIT', 'Y')
Start-Process -FilePath $accore -ArgumentList "/i `"$work`" /s `"$root\ext.scr`"" `
    -WorkingDirectory $root -NoNewWindow -Wait `
    -RedirectStandardOutput "$root\ext.out" -RedirectStandardError "$root\ext.err" | Out-Null

$mnx = $null
foreach ($line in (Get-Content -LiteralPath "$root\extents.txt")) {
    if ($line -match 'EXTMIN=\(([-0-9.eE]+)\s+([-0-9.eE]+)') { $mnx = [double]$Matches[1]; $mny = [double]$Matches[2] }
    if ($line -match 'EXTMAX=\(([-0-9.eE]+)\s+([-0-9.eE]+)') { $mxx = [double]$Matches[1]; $mxy = [double]$Matches[2] }
}
if ($null -eq $mnx) { throw "could not read extents" }
$mnx -= $MarginMm; $mny -= $MarginMm; $mxx += $MarginMm; $mxy += $MarginMm
$fw = $mxx - $mnx; $fh = $mxy - $mny

# ---- mirror PageSpec.Resolve: smallest full-bleed paper that fits --------------------
$long = @(297.0, 420.0, 594.0, 841.0, 1189.0, 1682.0, 2378.0)
$media = @(
    "ISO_full_bleed_A4_(210.00_x_297.00_MM)", "ISO_full_bleed_A3_(297.00_x_420.00_MM)",
    "ISO_full_bleed_A2_(420.00_x_594.00_MM)", "ISO_full_bleed_A1_(594.00_x_841.00_MM)",
    "ISO_full_bleed_A0_(841.00_x_1189.00_MM)", "ISO_full_bleed_2A0_(1189.00_x_1682.00_MM)",
    "ISO_full_bleed_4A0_(1682.00_x_2378.00_MM)")
$landscape = $fw -ge $fh
$needLong = if ($landscape) { $fw } else { $fh }
$needShort = if ($landscape) { $fh } else { $fw }
$idx = 0
for ($i = 0; $i -lt $long.Count; $i++) {
    $short = [math]::Round($long[$i] / [math]::Sqrt(2.0))
    if ($long[$i] -ge $needLong -and $short -ge $needShort) { $idx = $i; break }
}
if ($idx -ge $long.Count) { $idx = $long.Count - 1 }
Write-Host ("[com] frame {0:N1} x {1:N1} mm -> paper {2} ({3})" -f $fw, $fh, $media[$idx], $long[$idx])

# ---- one COM plot per variant --------------------------------------------------------
$app = New-Object -ComObject AutoCAD.Application
$app.Visible = $false
try { $app.DisplayAlerts = 0 } catch { }
$doc = $null
try {
    $doc = $app.Documents.Open($work, $false)
    try { $doc.SetVariable("BACKGROUNDPLOT", 0) } catch { }
    try { $doc.SetVariable("PLQUIET", 1) } catch { }
    # NOTE: Document.ModelSpace is the ENTITY collection, not the layout object.
    # The plot settings live on Document.Layouts.Item("Model") - same as the C# code
    # (layouts.GetAt("Item", "Model")).
    $layout = $doc.Layouts.Item("Model")
    try { $doc.ActiveLayout = $layout } catch { }
    $layout.ConfigName = "DWG To PDF.pc3"
    $layout.StyleSheet = "monochrome.ctb"
    $layout.CanonicalMediaName = $media[$idx]
    if ($landscape) { $layout.PlotRotation = 1 } else { $layout.PlotRotation = 0 }

    foreach ($v in ($Variants -split ',')) {
        $parts = $v.Trim() -split ':'
        $lw = $parts[0]
        $shade = $parts[1]
        $label = "com_lw$lw`_sh$shade"
        $out = Join-Path $outDir ($tag + "__" + $label + ".pdf")
        if (Test-Path $out) { Remove-Item $out -Force }

        $ll = New-Object 'double[]' 2
        $ur = New-Object 'double[]' 2
        $ll[0] = $mnx; $ll[1] = $mny; $ur[0] = $mxx; $ur[1] = $mxy
        $layout.SetWindowToPlot($ll, $ur)
        $layout.UseStandardScale = $true
        $layout.StandardScale = 0          # acScaleToFit
        $layout.PlotType = 4               # acWindow
        $layout.CenterPlot = $true
        # MEASURED 2026-10-07: the Layout object has NO "PlotLineweights" property.
        # The real names are PlotWithLineweights / ScaleLineweights (enumerate with
        # Get-Member). The C# COM path sets "PlotLineweights" and silently throws ->
        # the产品 "显示线宽" setting has never taken effect.
        $layout.PlotWithLineweights = [System.Convert]::ToBoolean($lw)
        try { $layout.ShadePlot = [int]$shade } catch { Write-Warning "[com] ShadePlot not settable: $($_.Exception.Message)" }

        $sw = [Diagnostics.Stopwatch]::StartNew()
        $doc.Plot.PlotToFile($out)
        $sw.Stop()
        if (Test-Path $out) {
            Write-Host ("[com]   {0} ok {1:N1}s {2:N0} bytes" -f $label, $sw.Elapsed.TotalSeconds, (Get-Item $out).Length)
        } else {
            Write-Warning "[com]   $label produced no PDF"
        }
    }
}
finally {
    if ($doc) { try { $doc.Close($false) } catch { } }
    try { $app.Quit() } catch { }
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($app)
    [GC]::Collect(); [GC]::WaitForPendingFinalizers()
}

Write-Host ""
Write-Host "================ MEASUREMENT (pdfink.py) ================"
$pdfs = @(Get-ChildItem -LiteralPath $outDir -Filter *.pdf | Sort-Object Name | ForEach-Object { $_.FullName })
if ($pdfs.Count -gt 0) { & python $pdfink $pdfs } else { Write-Warning "no PDFs" }

if (-not $KeepTemp) { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
