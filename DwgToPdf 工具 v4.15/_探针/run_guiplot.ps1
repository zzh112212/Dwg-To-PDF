# run_guiplot.ps1 -- prepare the VISIBLE AutoCAD instance for a GUI PLOT test.
#
# Why (2026-10-07, phase5):
#   the tool's own paths (accore -EXPORT, COM PlotToFile) both rasterise the whole page
#   (OLE2FRAME count == raster tile count), while a manual GUI PLOT of the same sheet
#   produced VECTOR output (3.2MB content stream). We want to reproduce the GUI PLOT and
#   learn whether the plot *engine* is what differs. Everything BEFORE pressing OK is set
#   here through COM so the only GUI-side action left is the PLOT dialog itself.
#
# What it does:
#   - attaches to the ALREADY RUNNING visible AutoCAD (GetActiveObject, never Quit)
#   - opens a throw-away copy of the drawing (source file is never opened)
#   - applies the same Model-layout page setup the tool uses
#   - prints every resulting value so we can compare with the dialog
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File .\run_guiplot.ps1 -Dwg "C:\Temp\dwgtopdf_gui\PISCES_7758.dwg"
param(
    [Parameter(Mandatory = $true)][string]$Dwg
)

$ErrorActionPreference = "Stop"
$media2A0 = "ISO_full_bleed_2A0_(1189.00_x_1682.00_MM)"

$app = [Runtime.InteropServices.Marshal]::GetActiveObject("AutoCAD.Application")
$app.Visible = $true
try { $app.DisplayAlerts = 0 } catch { }
Write-Host ("[attach] running AutoCAD, Visible={0}" -f $app.Visible)

$doc = $app.Documents.Open($Dwg, $false)
Write-Host ("[open] {0}" -f $doc.FullName)
try { $doc.SetVariable("BACKGROUNDPLOT", 0) } catch { Write-Host ("  BACKGROUNDPLOT failed: " + $_.Exception.Message) }
try { $doc.SetVariable("PLQUIET", 1) } catch { }

$lay = $doc.Layouts.Item("Model")
try { $doc.ActiveLayout = $lay } catch { }

Write-Host ("[before] Config={0}  Media={1}  PlotType={2}  Style={3}  Lw={4}" -f `
    $lay.ConfigName, $lay.CanonicalMediaName, $lay.PlotType, $lay.StyleSheet, $lay.PlotWithLineweights)

$lay.ConfigName = "DWG To PDF.pc3"
try { $lay.StyleSheet = "monochrome.ctb" } catch { Write-Host ("  StyleSheet failed: " + $_.Exception.Message) }
$lay.CanonicalMediaName = $media2A0

$ext = $doc.GetVariable("EXTMIN")
$ex2 = $doc.GetVariable("EXTMAX")
[double]$xmin = [double]$ext[0]; [double]$ymin = [double]$ext[1]
[double]$xmax = [double]$ex2[0]; [double]$ymax = [double]$ex2[1]
[double]$mx = 10.0
$p1 = [double[]]@(($xmin - $mx), ($ymin - $mx))
$p2 = [double[]]@(($xmax + $mx), ($ymax + $mx))
$lay.SetWindowToPlot($p1, $p2)
$lay.UseStandardScale = $true
$lay.StandardScale = 0          # acScaleToFit
$lay.CenterPlot = $true
$lay.PlotType = 4               # acWindow
try { $lay.PlotWithLineweights = $true; Write-Host "  PlotWithLineweights = true OK" }
catch { Write-Host ("  PlotWithLineweights failed: " + $_.Exception.Message) }
try { $lay.RefreshPlotDeviceInfo() } catch { }
$doc.Regen(-1) | Out-Null

Write-Host ("[after ] Config={0}  Media={1}" -f $lay.ConfigName, $lay.CanonicalMediaName)
Write-Host ("         PlotType={0} (4=Window)  Scale={1}/{2}  Center={3}" -f `
    $lay.PlotType, $lay.UseStandardScale, $lay.StandardScale, $lay.CenterPlot)
Write-Host ("         Style={0}  PlotWithLineweights={1}" -f $lay.StyleSheet, $lay.PlotWithLineweights)
Write-Host ("         Window = {0:N1},{1:N1} -> {2:N1},{3:N1}" -f $p1[0], $p1[1], $p2[0], $p2[1])
Write-Host ("[doc] layouts: " + (($doc.Layouts | ForEach-Object { $_.Name }) -join ", "))
Write-Host "[ready] now drive the GUI: PLOT dialog -> 确定  (output defaults to the drawing's folder)"
[Runtime.InteropServices.Marshal]::ReleaseComObject($doc) | Out-Null
[Runtime.InteropServices.Marshal]::ReleaseComObject($app) | Out-Null
