<#
    SXA RTX Sync - publicacion a GitHub Releases.

    Uso:
        .\publish.ps1                 # genera el ZIP (y el instalador si Inno Setup esta instalado)
        .\publish.ps1 -Version 1.1.0  # genera con version especifica
        .\publish.ps1 -Push           # genera ZIP+instalador y crea el Release en GitHub (via API REST)

    El ZIP/instalador NO incluye appsettings.json ni device.config: la config
    del equipo se conserva al actualizar. Un PC nuevo abre Configuración y define sus datos.
#>
param(
    [string]$Version = "",
    [switch]$Push
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$project = Join-Path $root "SXA-RTX-Sync\src\SXA.RTX.Sync.Tray\SXA.RTX.Sync.Tray.csproj"
$repo = "hector1516/SXA-RTX"

if (-not $Version) {
    $csproj = Get-Content $project -Raw
    $m = [regex]::Match($csproj, '<Version>([^<]+)</Version>')
    if (-not $m.Success) { throw "No se encontro <Version> en el csproj." }
    $Version = $m.Groups[1].Value
}

Write-Host "=== Publicando SXA RTX Sync v$Version ===" -ForegroundColor Cyan

$publishDir = Join-Path $root "artifacts\publish"
$pkgDir = Join-Path $root "artifacts\pkg"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (Test-Path $pkgDir) { Remove-Item $pkgDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publishDir, $pkgDir | Out-Null

Write-Host "dotnet publish (win-x64, framework-dependent)..."
dotnet publish $project -c Release -r win-x64 --self-contained false -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "Fallo dotnet publish." }

# El ZIP de distribucion: sin configs del equipo.
$zipName = "SXA-RTX-Sync-v$Version-win-x64.zip"
$zipPath = Join-Path $pkgDir $zipName
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath
Write-Host "ZIP creado: $zipPath" -ForegroundColor Green

# Instalador Inno Setup (si esta instalado, genera Setup_SXA...exe)
$iss = Join-Path $root "installer.iss"
$setupName = "Setup_SXA_RTX_Sync_v$Version.exe"
$setupPath = Join-Path $pkgDir $setupName
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($iscc -and (Test-Path $iss)) {
    Write-Host "Compilando instalador con Inno Setup..."
    & $iscc "/DMyAppVersion=$Version" $iss
    if ($LASTEXITCODE -ne 0) { Write-Warning "Fallo al compilar el instalador Inno Setup (se continua solo con el ZIP)." }
    elseif (Test-Path $setupPath) { Write-Host "Instalador Inno creado: $setupPath" -ForegroundColor Green }
} elseif (Test-Path $iss) {
    Write-Host "Inno Setup no encontrado. Se omitirá el instalador Inno." -ForegroundColor DarkGray
}

# Instalador NSIS (si esta instalado, genera Setup NSIS)
$issNSIS = Join-Path $root "installer.nsi"
$nsisSetupPath = Join-Path $pkgDir $setupName
$makensis = @(
    "${env:ProgramFiles(x86)}\NSIS\makensis.exe",
    "$env:ProgramFiles\NSIS\makensis.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($makensis -and (Test-Path $issNSIS)) {
    Write-Host "Compilando instalador NSIS..."
    & $makensis "/DMyAppVersion=$Version" $issNSIS
    if ($LASTEXITCODE -ne 0) { Write-Warning "Fallo al compilar el instalador NSIS." }
    elseif (Test-Path $nsisSetupPath) { Write-Host "Instalador NSIS creado: $nsisSetupPath" -ForegroundColor Green; $setupPath = $nsisSetupPath }
} elseif (Test-Path $issNSIS) {
    Write-Host "NSIS no encontrado. Se omitirá el instalador NSIS." -ForegroundColor DarkGray
}

if ($Push) {
    Write-Host "Creando Release v$Version en GitHub..."
    $token = $env:GITHUB_TOKEN
    if (-not $token) { $token = $env:GH_TOKEN }
    $assets = @($zipPath) + @($setupPath) | Where-Object { Test-Path $_ }
    if ($token) {
        $headers = @{ Authorization = "Bearer $token"; Accept = "application/vnd.github+json"; "User-Agent" = "SXA-RTX-Release" }
        $body = @{ tag_name = "v$Version"; name = "SXA RTX Sync v$Version"; body = "Sincronizador VTi/VTech de pruebas RTX.`n`nInstalar: ejecutar Setup_SXA...exe o descomprimir el ZIP y ejecutar SXA.RTX.Sync.Tray.exe. La config del equipo se conserva.`n`nCambios: ver commits en https://github.com/$repo/commits/v$Version"; draft = $false; prerelease = $false } | ConvertTo-Json
        $rel = Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/$repo/releases" -Headers $headers -Body $body -ContentType "application/json"
        Write-Host "Release creado: $($rel.html_url)" -ForegroundColor Green
        foreach ($asset in $assets) {
            $uri = "https://uploads.github.com/repos/$repo/releases/$($rel.id)/assets?name=$(Split-Path $asset -Leaf)"
            $up = Invoke-RestMethod -Method Post -Uri $uri -Headers $headers -InFile $asset -ContentType "application/octet-stream"
            Write-Host "  Asset subido: $($up.name) ($($up.size) bytes)" -ForegroundColor Green
        }
    } else {
        # Fallback a gh CLI si hay token en gh auth
        $ghArgs = @("release", "create", "v$Version") + $assets + @("--repo", $repo, "--title", "SXA RTX Sync v$Version", "--notes", "Sincronizador VTi/VTech. Ver https://github.com/$repo/releases/tag/v$Version")
        & gh @ghArgs
        if ($LASTEXITCODE -ne 0) { throw "Fallo gh release create. Define GITHUB_TOKEN o autentica gh." }
        Write-Host "Release publicado: https://github.com/$repo/releases/tag/v$Version" -ForegroundColor Green
    }
} else {
    Write-Host "Para publicar el Release usa: `$env:GITHUB_TOKEN='...' ; .\publish.ps1 -Push" -ForegroundColor Yellow
}
