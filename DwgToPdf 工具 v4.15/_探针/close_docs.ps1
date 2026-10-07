# close_docs.ps1 -- close every open document in the RUNNING visible AutoCAD,
# WITHOUT saving (discard changes), so temp working copies are released.
#
# Why: 2026-10-07 the probe could not re-copy its working DWG because AutoCAD
# still held PISCES_7807758-AC_LD004APIA3___4.dwg open. COM calls also hit
# RPC_E_CALL_REJECTED (0x80010001) while the app was busy, so every COM call
# here is wrapped in a retry loop.
#
# NEVER calls Quit(): the user's AutoCAD session must survive.
param(
    [int]$Retries = 30,
    [int]$DelayMs = 800
)

$ErrorActionPreference = "Stop"
$app = [Runtime.InteropServices.Marshal]::GetActiveObject("AutoCAD.Application")

function Invoke-Com([scriptblock]$Action, [string]$What) {
    for ($i = 1; $i -le $Retries; $i++) {
        try { return & $Action }
        catch {
            if ($i -eq $Retries) { throw ("{0} failed after {1} tries: {2}" -f $What, $Retries, $_.Exception.Message) }
            Start-Sleep -Milliseconds $DelayMs
        }
    }
}

$count = Invoke-Com { $app.Documents.Count } "Documents.Count"
Write-Host ("[close] {0} document(s) open" -f $count)

# NOTE 2026-10-07: Documents.Item(1) returns E_FAIL in this environment, so we
# always close $app.ActiveDocument instead (verified working). Each Close makes
# the next document active, so this drains the collection.
for ($j = 1; $j -le $count; $j++) {
    $doc = Invoke-Com { $app.ActiveDocument } "ActiveDocument"
    $name = $doc.Name
    Invoke-Com { $doc.Close($false) } ("Close(" + $name + ")") | Out-Null
    Write-Host ("  closed (no save): {0}" -f $name)
    Start-Sleep -Milliseconds 300
}

$left = Invoke-Com { $app.Documents.Count } "Documents.Count(after)"
Write-Host ("[done ] {0} document(s) left open" -f $left)
[Runtime.InteropServices.Marshal]::ReleaseComObject($app) | Out-Null
