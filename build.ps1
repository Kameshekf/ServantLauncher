# ==============================================================================
#  Servant Launcher & Installer Automated Build Script
#  All build artifacts are strictly placed in dist/ (never in repository root)
# ==============================================================================

param (
    [switch]$NoInstaller = $false,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$scriptRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptRoot)) {
    $scriptRoot = (Get-Location).Path
}

$launcherProject = Join-Path $scriptRoot "ServantLauncher.csproj"
$installerProject = Join-Path $scriptRoot "ServantInstaller\ServantInstaller.csproj"

$distDir = Join-Path $scriptRoot "dist"
$distLauncher = Join-Path $distDir "launcher"
$distInstaller = Join-Path $distDir "installer"
$payloadZip = Join-Path $scriptRoot "ServantInstaller\payload.zip"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "         SERVANT LAUNCHER BUILD & PACKAGE PIPELINE       " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# Stop any running instances to avoid file lock issues
Stop-Process -Name ServantLauncher -Force -ErrorAction SilentlyContinue
Stop-Process -Name ServantSetup -Force -ErrorAction SilentlyContinue

# Ensure clean dist directory
if (Test-Path $distLauncher) {
    Remove-Item $distLauncher -Recurse -Force -ErrorAction SilentlyContinue
}
if (Test-Path $distInstaller) {
    Remove-Item $distInstaller -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $distDir -Force | Out-Null
New-Item -ItemType Directory -Path $distLauncher -Force | Out-Null
New-Item -ItemType Directory -Path $distInstaller -Force | Out-Null

$versionJson = @"
{
  "version": "1.3.5",
  "url": "https://servant-ru.online/api/launcher/ServantLauncher.exe",
  "setupUrl": "https://servant-ru.online/api/launcher/ServantSetup.exe",
  "notes": "Обновление Servant Launcher 1.3.5: Drag-and-Drop импорт модов, ресурспаков и шейдеров прямо в список, оптимизация и параллельная распаковка mrpack, базовое разрешение 854x480"
}
"@
Set-Content -Path (Join-Path $distDir "version.json") -Value $versionJson -Encoding UTF8

if (Test-Path $payloadZip) {
    Remove-Item $payloadZip -Force -ErrorAction SilentlyContinue
}

# 1. Publish Servant Launcher
Write-Host "[2/5] Сборка и публикация Servant Launcher (.NET 9, win-x64, $Configuration)..." -ForegroundColor Yellow
$publishArgs = @(
    "publish",
    $launcherProject,
    "-c", $Configuration,
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:IncludeAllContentForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-o", $distLauncher
)
& dotnet @publishArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host "Ошибка при сборке Servant Launcher!" -ForegroundColor Red
    exit 1
}

Write-Host "  -> Лаунчер собран: $distLauncher\ServantLauncher.exe" -ForegroundColor Green

if ($NoInstaller) {
    Write-Host "Флаг -NoInstaller активен. Сборка установщика пропущена." -ForegroundColor Yellow
    Write-Host "Готово! Все файлы в: $distLauncher" -ForegroundColor Green
    exit 0
}

# 2. Package into payload.zip for installer
Write-Host "[3/5] Архивирование payload.zip для установщика..." -ForegroundColor Yellow
Compress-Archive -Path "$distLauncher\*" -DestinationPath $payloadZip -CompressionLevel Optimal -Force

# 3. Publish Installer
Write-Host "[4/5] Сборка автономного установщика ServantSetup.exe..." -ForegroundColor Yellow
$installerPublishArgs = @(
    "publish",
    $installerProject,
    "-c", $Configuration,
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:IncludeAllContentForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-o", $distInstaller
)
& dotnet @installerPublishArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host "Ошибка при сборке ServantSetup!" -ForegroundColor Red
    exit 1
}

# 4. Clean temporary payload.zip
Write-Host "[5/5] Очистка временных архивов..." -ForegroundColor Yellow
if (Test-Path $payloadZip) {
    Remove-Item $payloadZip -Force -ErrorAction SilentlyContinue
}

Write-Host "==========================================================" -ForegroundColor Green
Write-Host "                  СБОРКА УСПЕШНО ЗАВЕРШЕНА!               " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host " Лаунчер:    $distLauncher\ServantLauncher.exe" -ForegroundColor White
Write-Host " Установщик: $distInstaller\ServantSetup.exe" -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Green
