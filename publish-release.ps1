# AngelDungeonTips — one-click release pack for forum download
# Output: Desktop\AngelDungeonTips-Release\AngelDungeonTips.zip

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root "AngelDungeonTips\AngelDungeonTips.csproj"
$guideSrc = Join-Path $root "guide-zh.txt"
# Ensure packaged catalog.url exists (maintainer fills URL before forum release)
$outDir = Join-Path ([Environment]::GetFolderPath("Desktop")) "AngelDungeonTips-Release"
$publishDir = Join-Path $outDir "publish"

if (-not (Test-Path $proj)) {
    throw "Project not found: $proj"
}

Write-Host "Cleaning old output..."
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

Write-Host "Publishing self-contained single-file (may take a few minutes)..."
dotnet publish $proj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

if (Test-Path $guideSrc) {
    $guideDst = Join-Path $publishDir ([char]0x4F7F + [char]0x7528 + [char]0x8AAA + [char]0x660E + ".txt")
    Copy-Item $guideSrc $guideDst -Force
}

$zipPath = Join-Path $outDir "AngelDungeonTips.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force

$sizeMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host ""
Write-Host "DONE"
Write-Host "  ZIP: $zipPath  (~$sizeMb MB)"
Write-Host "  Folder: $publishDir"
Write-Host "Upload AngelDungeonTips.zip to the forum."
