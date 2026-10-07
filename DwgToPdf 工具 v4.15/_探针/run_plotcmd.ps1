# run_plotcmd.ps1 -- drive AutoCAD's own -PLOT command in a VISIBLE instance.
#
# Why (measured 2026-10-07):
#   PlotToFile (object model)  -> raster, whole page, both headless and visible
#   AutoCAD GUI, PLOT dialog  -> VECTOR (user's own file: 3.2MB path data)
# So the difference is the PLOT *command* path, not visibility. Per manual ch.26 the
# blocker was never "it does not work" but "we never learned the real prompt sequence".
# This probe solves that by turning LOGFILEMODE on and reading AutoCAD's own log after
# every SendCommand, so each answer is derived from the actual prompt instead of guessed.
#
# The drawing is never modified: we plot a throw-away copy and close with SaveChanges=0.
# Page setup is applied to the in-memory document only (ConfigName / media / window /
# scale / rotation / origin), never saved.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File .\run_plotcmd.ps1 -Dwg "<path>" -Answers "..."
param(
    [Parameter(Mandatory = $true)][string]$Dwg,
    [string]$Answers = "",        # e.g. "Y;;" - sent one line at a time after the first prompt
    [int]$StepWaitMs = 2500,
    [switch]$KeepTemp
)

$ErrorActionPreference = "Stop"
$root = "C:\Temp\dwgtopdf_plotcmd"
$outDir = Join-Path $PSScriptRoot "out\plotcmd"
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $root | Out-Null

$tag = [IO.Path]::GetFileNameWithoutExtension($Dwg) -replace '[^A-Za-z0-9_\-]', '_'
if ($tag.Length -gt 40) { $tag = $tag.Substring(0, 40) }
$work = Join-Path $root "$tag.dwg"
Copy-Item -LiteralPath $Dwg -Destination $work
$pdf = Join-Path $outDir "$tag__plotcmd.pdf"
if (Test-Path $pdf) { Remove-Item $pdf -Force }

$media = "ISO_full_bleed_2A0_(1189.00_x_1682.00_MM)"

function Send-Cmd($doc, [string]$text, [int]$waitMs) {
    # SendCommand is asynchronous: the app queues the text and returns immediately.
    $doc.SendCommand($text + "`n")
    Start-Sleep -Milliseconds $waitMs
}

function Get-LogTail($logPath, [int]$lines) {
    if (-not (Test-Path $logPath)) { return @("<no log yet: $logPath>") }
    $all = @(Get-Content -LiteralPath $logPath -Encoding Default -ErrorAction SilentlyContinue)
    $n = [Math]::Min($lines, $all.Count)
    return $all[($all.Count - $n)..($all.Count - 1)]
}

$app = New-Object -ComObject AutoCAD.Application
$logPath = $null
try {
    $app.Visible = $true
    try { $app.DisplayAlerts = 0 } catch { }
    $doc = $app.Documents.Open($work, $false)
    try {
        # turn on command logging so we can read the real prompts
        try {
            $doc.SetVariable("LOGFILEMODE", 1)
            $logPath = $doc.GetVariable("LOGFILEPATH")
            Write-Host "[log] LOGFILEPATH = $logPath"
        } catch { Write-Host "[log] could not enable logging: $($_.Exception.Message)" }

        $lay = $doc.Layouts.Item("Model")
        try { $doc.ActiveLayout = $lay } catch { }
        $lay.ConfigName = "DWG TO PDF.pc3"
        try { $lay.StyleSheet = "monochrome.ctb" } catch { }
        $lay.CanonicalMediaName = $media
        [double]$xmin = 0; [double]$ymin = 0; [double]$xmax = 0; [double]$ymax = 0
        $ext = $doc.GetVariable("EXTMIN"); $ex2 = $doc.GetVariable("EXTMAX")
        $xmin = [double]$ext[0]; $ymin = [double]$ext[1]; $xmax = [double]$ex2[0]; $ymax = [double]$ex2[1]
        $lay.SetWindowToPlot([double[]]@($xmin, $ymin), [double[]]@($xmax, $ymax))
        $lay.UseStandardScale = $true
        $lay.StandardScale = 0        # acScaleToFit
        $lay.CenterPlot = $true
        $lay.PlotType = 4             # acWindow
        Write-Host ("[setup] media={0} window={1:N1},{2:N1}->{3:N1},{4:N1}" -f $lay.CanonicalMediaName, $xmin, $ymin, $xmax, $ymax)

        # marker so we can find our own place in the log
        Send-Cmd $doc '"=== PLOTCMD PROBE START ==="' 800

        Send-Cmd $doc "-PLOT" $StepWaitMs
        Write-Host ""
        Write-Host "===== after '-PLOT' : what AutoCAD actually asked ====="
        Get-LogTail $logPath 30 | ForEach-Object { "   | $_" }

        if ($Answers) {
            $steps = $Answers -split ';'
            $i = 0
            foreach ($a in $steps) {
                $i++
                if ($a -eq "__WAIT__") { Start-Sleep -Seconds 5; continue }
                Write-Host ""
                Write-Host ("----- answer #{0}: '{1}' -----" -f $i, $a)
                Send-Cmd $doc $a $StepWaitMs
                Get-LogTail $logPath 12 | ForEach-Object { "   | $_" }
            }
        }

        # let the plot finish, then escape any leftover prompt
        Start-Sleep -Seconds 6
        Send-Cmd $doc ([char]27) 800
        Write-Host ""
        Write-Host ("[result] pdf exists = {0}" -f (Test-Path $pdf))
        if (Test-Path $pdf) { Write-Host ("[result] size = {0:N0} bytes" -f (Get-Item $pdf).Length) }
        Write-Host "===== log tail ====="
        Get-LogTail $logPath 40 | ForEach-Object { "   | $_" }
    }
    finally {
        $doc.Close(0)     # SaveChanges = 0 -> the DWG on disk is never touched
    }
}
finally {
    try { $app.Quit() } catch { }
    [Runtime.InteropServices.Marshal]::ReleaseComObject($app) | Out-Null
    [GC]::Collect(); [GC]::WaitForPendingFinalizers()
    if (-not $KeepTemp) { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
}
