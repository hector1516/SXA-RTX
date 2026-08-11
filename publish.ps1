<#
    SXA RTX Sync - publicacion a GitHub Releases.

    Uso:
        .\publish.ps1                 # genera el ZIP con la version del csproj
        .\publish.ps1 -Version 1.1.0  # genera el ZIP con version especifica
        .\publish.ps1 -Push           # genera ZIP y crea el Release en GitHub (requiere gh autenticado)

    El ZIP NO incluye appsettings.json ni device.config: la config del equipo se
    conserva al actualizar. Un PC nuevo abre Configuración y define sus datos.
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

if ($Push) {
    Write-Host "Creando Release v$Version en GitHub..."
    gh release create "v$Version" $zipPath `
        --repo $repo `
        --title "SXA RTX Sync v$Version" `
        --notes "Sincronizador VTi/VTech de pruebas RTX. Instalar: descomprimir sobre la carpeta de la app y ejecutar SXA.RTX.Sync.Tray.exe. La config del equipo se conserva."
    if ($LASTEXITCODE -ne 0) { throw "Fallo gh release create." }
    Write-Host "Release publicado: https://github.com/$repo/releases/tag/v$Version" -ForegroundColor Green
} else {
    Write-Host "Para publicar el Release usa: .\publish.ps1 -Push" -ForegroundColor Yellow
}
