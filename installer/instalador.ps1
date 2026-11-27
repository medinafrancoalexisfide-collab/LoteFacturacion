# Crea el instalador: publica el programa y compila el instalador con Inno Setup.
# Uso:  powershell -ExecutionPolicy Bypass -File .\instalador.ps1
# Si Inno Setup esta en un lugar raro:  ... -File .\instalador.ps1 -RutaIscc "C:\ruta\ISCC.exe"
param([string]$RutaIscc = "")

$ErrorActionPreference = "Stop"

$iss = Join-Path $PSScriptRoot "FacturacionLote.iss"
if (-not (Test-Path $iss)) {
    throw "No se encontro $iss. Debe llamarse FacturacionLote.iss y estar junto a este script."
}

# 1) Buscar el proyecto (.csproj) en la carpeta de arriba o en sus subcarpetas
$padre  = Split-Path -Parent $PSScriptRoot
$csproj = Get-ChildItem -Path $padre -Filter "FacturacionLote.csproj" -Recurse -Depth 3 -ErrorAction SilentlyContinue |
          Select-Object -First 1
if (-not $csproj) {
    throw "No se encontro FacturacionLote.csproj cerca de la carpeta Instalador (busque en $padre)."
}
Write-Host "Proyecto: $($csproj.FullName)"

# 2) La carpeta publish queda FUERA del proyecto (en la raiz del repositorio)
$publish = Join-Path $padre "publish"
Write-Host "Carpeta de publicacion: $publish"

# 3) Buscar el compilador de Inno Setup (ISCC.exe)
if ($RutaIscc -and (Test-Path $RutaIscc)) {
    $iscc = $RutaIscc
} else {
    $candidatos = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe"
    )
    $iscc = $candidatos | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) {
    throw 'No se encontro Inno Setup (ISCC.exe). Instalalo desde https://jrsoftware.org/isdl.php o indica su ruta con -RutaIscc "C:\ruta\ISCC.exe"'
}
Write-Host "Inno Setup: $iscc"

# 4) Publicar el programa (autocontenido, para Windows de 64 bits)
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $csproj.FullName -c Release -r win-x64 --self-contained true -o $publish
if ($LASTEXITCODE -ne 0) { throw "Fallo 'dotnet publish'. Revisa los mensajes de arriba." }

# 5) Compilar el instalador (le pasamos la carpeta publish al script .iss)
& $iscc "/DPublishDir=$publish" $iss
if ($LASTEXITCODE -ne 0) { throw "Fallo la compilacion del instalador." }

Write-Host ""
Write-Host "Listo. El instalador esta en: $(Join-Path $PSScriptRoot 'salida')" -ForegroundColor Green
