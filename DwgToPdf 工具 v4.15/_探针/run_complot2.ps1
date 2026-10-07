# run_complot2.ps1 -- COM channel A/B: which factor decides vector vs raster?
#
# Measured 2026-10-07 (PISCES, "prints light"):
#   tool output via -EXPORT / COM   -> raster, 122 full-page tiles (linework baked into bitmap)
#   AutoCAD GUI, shade plot "as displayed" -> VECTOR, 3.2MB path data
# The GUI differs from our COM probe in exactly two ways, so test those two:
#   Visible : false (headless)  vs true (real on-screen instance)
#   PlotType: 4 = acWindow (plot model space through a window)
#             5 = acLayout (plot the layout sheet - what the GUI does when you
#                             pick the Model tab and press PLOT)
# Plus a fourth run with shade plot forced to "rendered" is NOT attempted here:
# Layout.ShadePlot does not exist in the object model (measured), so it cannot be set
# from COM at all - only via DXF group 146, which the drawing already carries as 0.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File .\run_complot2.ps1 -Dwg "C:\Temp\x.dwg" -Variants "novis_window,vis_window,vis_layout,novis_layout"
param(
    [Parameter(Mandatory = $true)][string]$Dwg,
    [string]$Variants = "novis_window,vis_window,vis_layout,novis_layout",
    [string]$OutDir = ""
)

$ErrorActionPreference = "Stop"
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot "out\complot2" }
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$media2A0 = "ISO_full_bleed_2A0_(1189.00_x_1682.00_MM)"

function Get-MediaNames($layout) {
    $names = @()
    try { $names = @($layout.GetCanonicalMediaNames()) } catch { }
    return $names
}

$app = New-Object -ComObject AutoCAD.Application
try { $app.DisplayAlerts = 0 } catch { }
$results = @()
try {
    foreach ($v in $Variants.Split(",")) {
        $v = $v.Trim()
        if (-not $v) { continue }
        $vis = $v.StartsWith("vis")
        $plotType = if ($v.EndsWith("layout")) { 5 } else { 4 }
        $tag = "v2_" + $v
        $pdf = Join-Path $OutDir ($tag + ".pdf")
        if (Test-Path $pdf) { Remove-Item $pdf -Force }

        $app.Visible = $vis
        Write-Host ("[{0}] Visible={1} PlotType={2}" -f $tag, $vis, $plotType)

        $doc = $app.Documents.Open($Dwg, $false)
        try {
            try { $doc.SetVariable("BACKGROUNDPLOT", 0) } catch { }
            try { $doc.SetVariable("PLQUIET", 1) } catch { }
            # NOT $doc.ActiveLayout: that returns null in accore/automation sessions.
            # Plot settings live on Document.Layouts.Item("Model") - same as the C# code.
            $lay = $doc.Layouts.Item("Model")
            try { $doc.ActiveLayout = $lay } catch { }
            Write-Host ("    layout = " + $lay.Name)
            # NOTE: RefreshPlotDeviceInfo() can throw here (it needs an installed plotter for
            # the *current* device); skip it before ConfigName - the working probe
            # run_complot.ps1 sets ConfigName first and only refreshes afterwards.
            $lay.ConfigName = "DWG To PDF.pc3"
            try { $lay.StyleSheet = "monochrome.ctb" } catch { }
            $lay.CanonicalMediaName = $media2A0
            # window = model-space extents of the frame (+/- margin), same as the tool uses
            $ext = $doc.GetVariable("EXTMIN")
            $ex2 = $doc.GetVariable("EXTMAX")
            # COM hands these back as object[] in PowerShell - SetWindowToPlot(double x4) will
            # not bind unless every argument is a real double ("Cannot find an overload").
            [double]$xmin = [double]$ext[0]; [double]$ymin = [double]$ext[1]
            [double]$xmax = [double]$ex2[0]; [double]$ymax = [double]$ex2[1]
            [double]$mx = 10.0
            [double]$ll = $xmin - $mx; [double]$ly = $ymin - $mx
            [double]$ur = $xmax + $mx; [double]$ur2 = $ymax + $mx
            Write-Host ("    window = {0:N1},{1:N1} -> {2:N1},{3:N1}" -f $ll, $ly, $ur, $ur2)
            # SetWindowToPlot takes TWO 2-element points (x,y). Passing 3-element arrays fails with
            # COMException "安全数组中的元素数目不正确" (wrong number of elements in safe array).
            $p1 = [double[]]@($ll, $ly)
            $p2 = [double[]]@($ur, $ur2)
            try { $lay.SetWindowToPlot($p1, $p2); Write-Host "    SetWindowToPlot OK" }
            catch { Write-Host ("    SetWindowToPlot FAILED: " + $_.Exception.Message); throw }
            try { $lay.UseStandardScale = $true; Write-Host "    UseStandardScale OK" } catch { Write-Host ("    UseStandardScale FAILED: " + $_.Exception.Message) }
            try { $lay.StandardScale = 0; Write-Host "    StandardScale OK" } catch { Write-Host ("    StandardScale FAILED: " + $_.Exception.Message) }
            try { $lay.PlotType = $plotType; Write-Host ("    PlotType OK (" + $lay.PlotType + ")") } catch { Write-Host ("    PlotType FAILED: " + $_.Exception.Message) }
            try { $lay.CenterPlot = $true; Write-Host "    CenterPlot OK" } catch { Write-Host ("    CenterPlot FAILED: " + $_.Exception.Message) }
            try { $doc.Regen(-1) | Out-Null } catch { }
            try { $lay.RefreshPlotDeviceInfo(); Write-Host "    RefreshPlotDeviceInfo OK" } catch { Write-Host ("    Refresh FAILED(ignored): " + $_.Exception.Message) }
            $lay.UseStandardScale = $true
            $lay.StandardScale = 0            # acScaleToFit
            $lay.PlotType = $plotType
            $lay.CenterPlot = $true
            $doc.Regen(-1) | Out-Null
            try { $lay.RefreshPlotDeviceInfo() } catch { }
            $sw = [Diagnostics.Stopwatch]::StartNew()
            $doc.Plot.PlotToFile($pdf)
            $sw.Stop()
            $ok = Test-Path $pdf
            $sz = if ($ok) { (Get-Item $pdf).Length } else { 0 }
            Write-Host ("    -> {0}  {1:N1}s  {2:N0} bytes" -f $(if ($ok) { "OK" } else { "FAILED" }), $sw.Elapsed.TotalSeconds, $sz)
            $results += [pscustomobject]@{ Variant = $v; Visible = $vis; PlotType = $plotType; Ok = $ok; Bytes = $sz }
        }
        finally {
            $doc.Close(0)   # wdDoNotSaveChanges - never touch the source
        }
    }
}
finally {
    try { $app.Quit() } catch { }
    [Runtime.InteropServices.Marshal]::ReleaseComObject($app) | Out-Null
    [GC]::Collect()
}

Write-Host ""
Write-Host ("{0,-14} {1,-7} {2,-9} {3,-7} {4,12}" -f "variant", "visible", "plottype", "ok", "bytes")
foreach ($r in $results) {
    Write-Host ("{0,-14} {1,-7} {2,-9} {3,-7} {4,12:N0}" -f $r.Variant, $r.Visible, $r.PlotType, $r.Ok, $r.Bytes)
}
Write-Host ""
Write-Host "now run:  python pdfruns.py <each pdf>   -> 'VECTOR' if vector content > 200000 and 0 tiles"
