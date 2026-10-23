# ==============================================================================
#  Servant Launcher: Скрипт быстрой упаковки обновления сборки сервера (.mrpack)
# ==============================================================================

param (
    [Parameter(Mandatory=$false)]
    [string]$Version = "1.0.1",

    [Parameter(Mandatory=$false)]
    [string]$Notes = "Обновление модов и конфигурации сервера",

    [Parameter(Mandatory=$false)]
    [string]$InstanceDir = "",

    [Parameter(Mandatory=$false)]
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"

$scriptRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptRoot)) {
    $scriptRoot = (Get-Location).Path
}

# 1. Поиск папки сервера
if ([string]::IsNullOrWhiteSpace($InstanceDir)) {
    $appData = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
    $launcherDir = Join-Path $appData ".servantlauncher"
    $profilesJson = Join-Path $launcherDir "profiles.json"

    if (Test-Path $profilesJson) {
        try {
            $profiles = Get-Content -Path $profilesJson -Raw -Encoding UTF8 | ConvertFrom-Json
            $serverProf = $profiles | Where-Object { $_.IsServerProfile -eq $true -or $_.Name -like "Servant Server*" } | Select-Object -First 1
            if ($serverProf) {
                $InstanceDir = Join-Path $launcherDir "instances\$($serverProf.Id)"
            }
        } catch { }
    }

    if ([string]::IsNullOrWhiteSpace($InstanceDir) -or !(Test-Path $InstanceDir)) {
        $instancesDir = Join-Path $launcherDir "instances"
        if (Test-Path $instancesDir) {
            $firstInst = Get-ChildItem -Path $instancesDir -Directory | Select-Object -First 1
            if ($firstInst) {
                $InstanceDir = $firstInst.FullName
            }
        }
    }
}

if ([string]::IsNullOrWhiteSpace($InstanceDir) -or !(Test-Path $InstanceDir)) {
    Write-Host "ОШИБКА: Папка сборки сервера не найдена!" -ForegroundColor Red
    Write-Host "Укажите путь вручную: .\pack_server.ps1 -InstanceDir 'C:\path\to\instance'" -ForegroundColor Yellow
    exit 1
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "      SERVANT SERVER PACK BUILDER (.mrpack)              " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Версия сборки:   $Version" -ForegroundColor Green
Write-Host "Папка инстанса:  $InstanceDir" -ForegroundColor Green

# 2. Выходная папка
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $parent = Split-Path -Path $scriptRoot -Parent
    $OutputDir = Join-Path $parent "dist\serverpack"
}

if (!(Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$tempBuild = Join-Path $OutputDir "temp_build"
if (Test-Path $tempBuild) {
    Remove-Item $tempBuild -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $tempBuild -Force | Out-Null

$overridesDir = Join-Path $tempBuild "overrides"
New-Item -ItemType Directory -Path $overridesDir -Force | Out-Null

# 3. Копирование файлов в overrides/
# Моды
$srcMods = Join-Path $InstanceDir "mods"
if (Test-Path $srcMods) {
    Write-Host "Копирование модов..." -ForegroundColor Yellow
    $destMods = Join-Path $overridesDir "mods"
    New-Item -ItemType Directory -Path $destMods -Force | Out-Null
    Copy-Item -Path "$srcMods\*.jar" -Destination $destMods -Force -ErrorAction SilentlyContinue
    $modsCount = (Get-ChildItem -Path $destMods -Filter "*.jar").Count
    Write-Host "  -> Упаковано модов: $modsCount" -ForegroundColor Gray
}

# Конфиги
$srcConfig = Join-Path $InstanceDir "config"
if (Test-Path $srcConfig) {
    Write-Host "Копирование конфигов..." -ForegroundColor Yellow
    $destConfig = Join-Path $overridesDir "config"
    Copy-Item -Path $srcConfig -Destination $destConfig -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "  -> Конфиги скопированы." -ForegroundColor Gray
}

# Ресурспаки (если есть)
$srcRp = Join-Path $InstanceDir "resourcepacks"
if (Test-Path $srcRp) {
    $destRp = Join-Path $overridesDir "resourcepacks"
    Copy-Item -Path $srcRp -Destination $destRp -Recurse -Force -ErrorAction SilentlyContinue
}

# Настройки (options.txt, servers.dat если есть)
foreach ($f in @("options.txt", "servers.dat")) {
    $srcF = Join-Path $InstanceDir $f
    if (Test-Path $srcF) {
        Copy-Item -Path $srcF -Destination $overridesDir -Force -ErrorAction SilentlyContinue
    }
}

# 4. Генерация modrinth.index.json
Write-Host "Генерация modrinth.index.json..." -ForegroundColor Yellow
$indexJsonPath = Join-Path $tempBuild "modrinth.index.json"

$indexContent = @"
{
  "formatVersion": 1,
  "game": "minecraft",
  "versionId": "$Version",
  "name": "Servant Server",
  "summary": "$Notes",
  "files": [],
  "dependencies": {
    "minecraft": "1.21.11",
    "fabric-loader": "0.19.5"
  }
}
"@

Set-Content -Path $indexJsonPath -Value $indexContent -Encoding UTF8

# 5. Создание архива .mrpack
$mrpackPath = Join-Path $OutputDir "servant_server.mrpack"
if (Test-Path $mrpackPath) {
    Remove-Item $mrpackPath -Force -ErrorAction SilentlyContinue
}

$tempZip = Join-Path $OutputDir "temp_pack.zip"
if (Test-Path $tempZip) {
    Remove-Item $tempZip -Force -ErrorAction SilentlyContinue
}

Write-Host "Сжатие в $mrpackPath..." -ForegroundColor Yellow
Compress-Archive -Path "$tempBuild\*" -DestinationPath $tempZip -Force
Move-Item -Path $tempZip -Destination $mrpackPath -Force

# Удаление временной папки сборки
Remove-Item $tempBuild -Recurse -Force -ErrorAction SilentlyContinue

# 6. Создание version.json
$versionJsonPath = Join-Path $OutputDir "version.json"
$versionJsonContent = @"
{
  "version": "$Version",
  "url": "https://servant-ru.online/api/serverpack/servant_server.mrpack",
  "notes": "$Notes"
}
"@
Set-Content -Path $versionJsonPath -Value $versionJsonContent -Encoding UTF8

$fileSizeMb = [math]::Round(((Get-Item $mrpackPath).Length / 1MB), 2)

Write-Host "==========================================================" -ForegroundColor Green
Write-Host "        УПАКОВКА ОБНОВЛЕНИЯ УСПЕШНО ЗАВЕРШЕНА!           " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "Готовые файлы для загрузки на сервер в папке: $OutputDir" -ForegroundColor White
Write-Host " 1. servant_server.mrpack ($fileSizeMb MB)" -ForegroundColor Cyan
Write-Host " 2. version.json (версия $Version)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Green
