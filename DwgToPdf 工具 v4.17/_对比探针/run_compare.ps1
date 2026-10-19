# run_compare.ps1 -- batch-run the compare probe over a list of DWGs.
# ASCII only (PS 5.1 reads BOM-less .ps1 as ANSI).
# Read-only w.r.t. source DWGs: each DWG is copied to C:\Temp\dwgtopdf_probe first.
param(
    [string]$ListFile = "",
    [string]$Lsp = "",
    [string]$Func = "p_all",
    [switch]$KeepTemp
)

$ErrorActionPreference = "Stop"
$accore = "D:\AutoCAD2026\AutoCAD 2026\accoreconsole.exe"
if (-not (Test-Path $accore)) { throw "accoreconsole not found: $accore" }

if (-not $ListFile) { $ListFile = Join-Path $PSScriptRoot "dwglist.txt" }
if (-not $Lsp) { $Lsp = Join-Path $PSScriptRoot "p_compare.lsp" }
if (-not (Test-Path $ListFile)) { throw "list file not found: $ListFile" }
if (-not (Test-Path $Lsp)) { throw "lsp not found: $Lsp" }

$root = "C:\Temp\dwgtopdf_probe"
$outDir = Join-Path $PSScriptRoot "out"
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

$dwgPaths = @(Get-Content -LiteralPath $ListFile -Encoding UTF8 |
    ForEach-Object { $_.Trim() } |
    Where-Object { $_ -ne "" -and -not $_.StartsWith("#") })

Write-Host ("[compare] {0} target(s)" -f $dwgPaths.Count)

$idx = 0
foreach ($path in $dwgPaths) {
    $idx++
    $src = Get-Item -LiteralPath $path
    if (-not $src) { Write-Warning "missing: $path"; continue }

    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $root | Out-Null
    Copy-Item -LiteralPath $Lsp -Destination "$root\p_compare.lsp"

    $tag = [IO.Path]::GetFileNameWithoutExtension($src.Name)
    if ($tag.Length -gt 40) { $tag = $tag.Substring(0, 40) }
    $tag = ($tag -replace '[^A-Za-z0-9_\-]', '_')
    $tag = ("{0:D2}_{1}" -f $idx, $tag)
    Copy-Item -LiteralPath $src.FullName -Destination "$root\$tag.dwg"

    $scr = @(
        '(setq PT_FH (open "C:/Temp/dwgtopdf_probe/out_plotcfg.txt" "w"))',
        '(load "C:/Temp/dwgtopdf_probe/p_compare.lsp")',
        '(setq PT_R (vl-catch-all-apply (quote ' + $Func + ') nil))',
        '(if (vl-catch-all-error-p PT_R) (write-line (strcat "FATAL: " (vl-catch-all-error-message PT_R)) PT_FH))',
        '(close PT_FH)',
        'QUIT',
        'Y'
    ) -join "`r`n"
    Set-Content -LiteralPath "$root\run.scr" -Value $scr -Encoding ASCII

    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $accore `
        -ArgumentList "/i `"$root\$tag.dwg`" /s `"$root\run.scr`"" `
        -WorkingDirectory $root -NoNewWindow -PassThru -Wait `
        -RedirectStandardOutput "$root\accore_stdout.txt" `
        -RedirectStandardError "$root\accore_stderr.txt"
    $sw.Stop()
    Write-Host ("[{0}/{1}] {2} exit={3} {4:N1}s" -f $idx, $dwgPaths.Count, $src.Name, $p.ExitCode, $sw.Elapsed.TotalSeconds)

    if (Test-Path "$root\out_plotcfg.txt") {
        $dest = Join-Path $outDir ($tag + ".txt")
        Copy-Item "$root\out_plotcfg.txt" $dest -Force
    } else {
        Write-Warning "no output for $($src.Name). stdout tail:"
        Get-Content "$root\accore_stdout.txt" -Tail 20 -ErrorAction SilentlyContinue
    }
}

if (-not $KeepTemp) { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
Write-Host "[compare] done -> $outDir"
