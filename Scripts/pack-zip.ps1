$ErrorActionPreference = "Stop"
$pluginName = "QuickLook.Plugin.IsoViewer"
$root = Split-Path $PSScriptRoot -Parent

Write-Host "=== pack-zip.ps1 ==="

$candidates = @(
    (Join-Path $root "bin\release"),
    (Join-Path $root "bin\Release"),
    (Join-Path $root "bin\x86\Release"),
    (Join-Path $root "bin\x86\release"),
    (Join-Path $root "Release"),
    (Join-Path $root "release")
)

$releasePath = $null
foreach ($p in $candidates) {
    if (Test-Path $p) {
        $dll = Get-ChildItem -Path $p -Filter "*.dll" -File -ErrorAction SilentlyContinue
        if ($dll) {
            $releasePath = $p
            Write-Host "Using output: $releasePath"
            break
        }
    }
}

if (-not $releasePath) {
    Write-Host "ERROR: No Release output with DLL found."
    exit 1
}

# .qlplugin только в bin\ — НЕ в bin\release, чтобы не попал в следующую упаковку
$binPath = Join-Path $root "bin"
if (-not (Test-Path $binPath)) {
    New-Item -ItemType Directory -Path $binPath | Out-Null
}

$qlPath  = Join-Path $binPath "$pluginName.qlplugin"
$zipPath = Join-Path $binPath "$pluginName.zip"

Remove-Item $qlPath  -Force -ErrorAction SilentlyContinue
Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $root "$pluginName.qlplugin") -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $releasePath "$pluginName.qlplugin") -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $releasePath "$pluginName.zip") -Force -ErrorAction SilentlyContinue

$files = Get-ChildItem -Path $releasePath -File | Where-Object {
    $_.Extension -notin @(".pdb", ".xml") -and
    $_.Name -notlike "*.qlplugin" -and
    $_.Name -notlike "*.zip"
}

if (-not $files -or $files.Count -eq 0) {
    Write-Host "ERROR: No files to pack in $releasePath"
    exit 1
}

Write-Host "Packing $($files.Count) file(s):"
$files | ForEach-Object { Write-Host "  - $($_.Name)" }

$paths = @($files | ForEach-Object { $_.FullName })
Compress-Archive -Path $paths -DestinationPath $zipPath -Force
Move-Item -Path $zipPath -Destination $qlPath -Force

Write-Host "OK -> $qlPath"
exit 0
