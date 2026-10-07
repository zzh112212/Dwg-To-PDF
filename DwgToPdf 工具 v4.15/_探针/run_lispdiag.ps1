# run_lispdiag.ps1 -- why did the layout entget dump produce no file?
# Also: does the Layout COM object expose anything shade-plot related?
param(
    [Parameter(Mandatory = $true)][string]$Dwg
)
$ErrorActionPreference = "Continue"
$root = "C:\Temp\dwgtopdf_gui"
New-Item -ItemType Directory -Path $root -Force | Out-Null
$work = Join-Path $root "diag.dwg"
Copy-Item -LiteralPath $Dwg -Destination $work -Force

$app = [Runtime.InteropServices.Marshal]::GetActiveObject("AutoCAD.Application")
$app.Visible = $true
try { $app.DisplayAlerts = 0 } catch { }
$doc = $app.Documents.Open($work, $false)

$marker = Join-Path $root "marker1.txt"
$dump   = Join-Path $root "diag_layout.txt"
Remove-Item $marker, $dump -Force -ErrorAction SilentlyContinue

Write-Host "===== Layout COM members matching Plot/Shade ====="
$lay = $doc.Layouts.Item("Model")
$lay | Get-Member -MemberType Property, Method |
    Where-Object { $_.Name -match 'Plot|Shade|Style|Layer' } |
    ForEach-Object { "   {0,-28} {1}" -f $_.Name, $_.MemberType }

Write-Host ""
Write-Host "===== SendCommand: write a marker file ====="
$lisp1 = '(setq fp (open "C:/Temp/dwgtopdf_gui/marker1.txt" "w"))(princ "lisp-ran" fp)(close fp)(princ)'
try { $r = $doc.SendCommand($lisp1 + "`n"); Write-Host ("   SendCommand returned: {0}" -f $r) }
catch { Write-Host ("   SendCommand THREW: " + $_.Exception.Message) }
Start-Sleep -Milliseconds 2500
Write-Host ("   marker1.txt exists = {0}" -f (Test-Path $marker))
if (Test-Path $marker) { Write-Host ("   content = " + (Get-Content $marker -Raw)) }

Write-Host ""
Write-Host "===== SendCommand: dump Model layout entget ====="
$lisp2 = "(setq lay (cdr (assoc 350 (member (cons 3 ""Model"") (dictsearch (namedobjdict) ""ACAD_LAYOUT"")))))" +
         "(setq fp (open ""C:/Temp/dwgtopdf_gui/diag_layout.txt"" ""w""))" +
         "(princ (if lay ""LAYOUT-FOUND"" ""LAYOUT-NIL"") fp)(terpri fp)" +
         "(princ (entget lay) fp)(close fp)(princ)"
try { $r = $doc.SendCommand($lisp2 + "`n"); Write-Host ("   SendCommand returned: {0}" -f $r) }
catch { Write-Host ("   SendCommand THREW: " + $_.Exception.Message) }
Start-Sleep -Milliseconds 2500
Write-Host ("   diag_layout.txt exists = {0}" -f (Test-Path $dump))
if (Test-Path $dump) { Get-Content $dump -Encoding Default | ForEach-Object { "   | $_" } }

$doc.Close(0)
[Runtime.InteropServices.Marshal]::ReleaseComObject($doc) | Out-Null
[Runtime.InteropServices.Marshal]::ReleaseComObject($app) | Out-Null
