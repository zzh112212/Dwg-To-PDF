# run_shadeplot.ps1 -- code-only driver: open a DWG copy, set the layout's shade-plot
#                      DXF group, plot through the normal plot engine, report results.
#
# Context (2026-10-07, "打印发浅" = pale print, PISCES 2A0 sheets)
#   - Every tool path so far (accore -EXPORT, COM PlotToFile) produced a fully rasterised
#     page: OLE2FRAME count == raster tile count, linework baked into bitmaps -> pale.
#   - A manual GUI plot of the same sheet came out mostly VECTOR (3.2MB path data).
#   - The Plot dialog of an untouched PISCES sheet reads  着色打印 = 着色 (rendered);
#     the manual vector plot had used 按显示 (as displayed). Everything else in the
#     dialog already matched what the tool sets.
#   => hypothesis: the layout's shade-plot field is the raster/vector lever and the tool
#      never sets it. On the layout record that field is DXF group 76 (AcDbPlotSettings);
#      group 146 lives in AcDbLayout and is ELEVATION, not the shade plot.
#
# This script is the code half of the experiment; the GUI is only used to verify.
# The source drawing is never modified: a copy is opened, edits are in-memory only,
# and the document is closed with SaveChanges = 0.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File .\run_shadeplot.ps1 -Dwg "<src.dwg>" -Values "0"
#   powershell -ExecutionPolicy Bypass -File .\run_shadeplot.ps1 -Dwg "<src.dwg>" -Values "0,1,2,3" -Group 76
param(
    [Parameter(Mandatory = $true)][string]$Dwg,
    [string]$Values = "0",          # group values to try, in order
    [int]$Group = 76,               # DXF group holding the shade plot on the layout record
    [string]$OutDir = "C:\Temp\dwgtopdf_gui\runs",
    # v4.17 (2026-10-07): Layout.PlotWithLineweights was hard-coded true. It is now the
    # single independent variable of the A/B test: the only product that ever measured
    # REAL `w` operators (2/3/4/5/6/8 pt) came from this COM path with it ON, while every
    # tool output measured all-hairline (`0 w` only). Turn it off to see if the line-width
    # ladder in the PDF is caused by this property alone.
    # Int, not bool: `powershell -File` passes every argument as a string and a [bool]
    # parameter rejects "1"/"0". 1 = plot lineweights, 0 = do not.
    [int]$Lineweights = 1,
    [string]$Suffix = ""            # appended to the output file name, e.g. "_LWon"
)

$ErrorActionPreference = "Stop"
$root = "C:\Temp\dwgtopdf_gui"
New-Item -ItemType Directory -Path $root -Force | Out-Null
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$lspDst = Join-Path $root "p_shade.lsp"
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "p_shade.lsp") -Destination $lspDst -Force
$lspLoad = "(load """ + ($lspDst.Replace('\', '/')) + """)"

$tag = [IO.Path]::GetFileNameWithoutExtension($Dwg) -replace '[^A-Za-z0-9_\-]', '_'
if ($tag.Length -gt 32) { $tag = $tag.Substring(0, 32) }
$work = Join-Path $root "$tag.dwg"
$srcFull = [IO.Path]::GetFullPath($Dwg)
if (-not ($srcFull -eq [IO.Path]::GetFullPath($work))) { Copy-Item -LiteralPath $Dwg -Destination $work -Force }

function Send([object]$doc, [string]$cmd, [int]$waitMs = 1200) {
    $doc.SendCommand($cmd + "`n")
    Start-Sleep -Milliseconds $waitMs
}

function Read-Group([string]$path, [int]$code) {
    if (-not (Test-Path $path)) { return "<no dump>" }
    $t = Get-Content -LiteralPath $path -Raw -Encoding Default
    $m = [regex]::Match($t, "\(" + $code + "\s*\.\s*(-?[0-9.]+)\)")
    if ($m.Success) { return $m.Groups[1].Value }
    return "<absent>"
}

$app = [Runtime.InteropServices.Marshal]::GetActiveObject("AutoCAD.Application")
$app.Visible = $true
try { $app.DisplayAlerts = 0 } catch { }

$doc = $app.Documents.Open($work, $false)
$before = Join-Path $root "shade_before.txt"
$after  = Join-Path $root "shade_after.txt"
$results = @()
try {
    Write-Host ("[open ] {0}" -f $work)
    Send $doc ($lspLoad + "(c:shadeget)") 1500
    Write-Host ("[before] group {0} = {1}" -f $Group, (Read-Group $before $Group))
    $g70b = Read-Group $before 70

    # page setup the tool already knows how to set (unchanged from run_complot2.ps1)
    $lay = $doc.Layouts.Item("Model")
    try { $doc.ActiveLayout = $lay } catch { }
    $lay.ConfigName = "DWG To PDF.pc3"
    try { $lay.StyleSheet = "monochrome.ctb" } catch { }
    $lay.CanonicalMediaName = "ISO_full_bleed_2A0_(1189.00_x_1682.00_MM)"
    $ext = $doc.GetVariable("EXTMIN"); $ex2 = $doc.GetVariable("EXTMAX")
    [double]$xmin = [double]$ext[0];  [double]$ymin = [double]$ext[1]
    [double]$xmax = [double]$ex2[0];  [double]$ymax = [double]$ex2[1]
    [double]$mx = 10.0
    $lay.SetWindowToPlot([double[]]@(($xmin - $mx), ($ymin - $mx)),
                         [double[]]@(($xmax + $mx), ($ymax + $mx)))
    $lay.UseStandardScale = $true
    $lay.StandardScale = 0
    $lay.CenterPlot = $true
    $lay.PlotType = 4
    try { $lay.PlotWithLineweights = ($Lineweights -ne 0) } catch { }
    try { $lay.RefreshPlotDeviceInfo() } catch { }
    $doc.Regen(-1) | Out-Null

    foreach ($v in ($Values -split ',')) {
        $val = $v.Trim(); if ($val -eq '') { continue }
        Remove-Item $after -Force -ErrorAction SilentlyContinue
        Send $doc "(c:setgrp $Group $val)" 1200
        Send $doc "(c:shadeget2)" 1200
        $got = Read-Group $after $Group

        $pdf = Join-Path $OutDir ("$tag" + "_g$Group`_$val" + $Suffix + ".pdf")
        if (Test-Path $pdf) { Remove-Item $pdf -Force }
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $doc.Plot.PlotToFile($pdf)
        $sw.Stop()
        $sz = if (Test-Path $pdf) { (Get-Item $pdf).Length } else { 0 }
        $g70a = Read-Group $after 70
        Write-Host ("[try  ] want {0}={1}  LW={2}  readback={3}  g70 {4}->{5}  (168=0x08|32=0x20|128=0x80)" -f `
            $Group, $val, $Lineweights, $got, $g70b, $g70a)
        Write-Host ("       -> {0:N0} B  {1:N1}s  {2}" -f $sz, $sw.Elapsed.TotalSeconds, $pdf)
        $results += [pscustomobject]@{ Value = $val; ReadBack = $got; Bytes = $sz; Pdf = $pdf }
    }
}
finally {
    $doc.Close(0)
    [Runtime.InteropServices.Marshal]::ReleaseComObject($doc) | Out-Null
    [Runtime.InteropServices.Marshal]::ReleaseComObject($app) | Out-Null
    [GC]::Collect()
}
Write-Host ""
Write-Host "verify each pdf with:  python pdfruns.py <pdf>"
Write-Host "VECTOR = large vector content stream and ~0 raster tiles covering the sheet."
