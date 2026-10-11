<#
.SYNOPSIS
    Desenregistre SwMultiExport.dll en tant qu'add-in COM pour SolidWorks.

.DESCRIPTION
    A executer dans une console PowerShell en tant qu'administrateur (ecriture dans HKLM).

.PARAMETER Configuration
    Configuration de build a desenregistrer (Debug ou Release). Par defaut : Debug.

.PARAMETER DllPath
    Chemin explicite vers la DLL si different du chemin de sortie par defaut.

.PARAMETER SwInstallDir
    Dossier redist de SolidWorks contenant les DLL interop (pour la resolution
    de dependances par RegAsm, qui ne tourne pas dans le process SolidWorks).
#>
param(
    [string]$Configuration = "Debug",
    [string]$DllPath,
    [string]$SwInstallDir = "D:\Softwares\SolidworksCorp\SOLIDWORKS\api\redist\"
)

$ErrorActionPreference = "Stop"

$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Ce script doit etre execute en tant qu'administrateur (ecriture dans HKLM)."
}

if (-not $DllPath) {
    $root = Split-Path -Parent $PSScriptRoot
    $DllPath = Join-Path $root "src\SwMultiExport\bin\x64\$Configuration\SwMultiExport.dll"
}

if (-not (Test-Path $DllPath)) {
    throw "DLL introuvable : $DllPath."
}

$regasm = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"
if (-not (Test-Path $regasm)) {
    throw "RegAsm introuvable : $regasm"
}

$dllDir = Split-Path -Parent $DllPath
$interopNames = @(
    "SolidWorks.Interop.sldworks.dll",
    "SolidWorks.Interop.swconst.dll",
    "SolidWorks.Interop.swpublished.dll"
)
$copiedFiles = @()
foreach ($name in $interopNames) {
    $dest = Join-Path $dllDir $name
    if (-not (Test-Path $dest)) {
        $src = Join-Path $SwInstallDir $name
        Copy-Item -Path $src -Destination $dest
        $copiedFiles += $dest
    }
}

try {
    Write-Host "Desenregistrement de $DllPath via $regasm /unregister"
    & $regasm $DllPath /unregister /verbose

    if ($LASTEXITCODE -ne 0) {
        throw "RegAsm a echoue avec le code $LASTEXITCODE"
    }
}
finally {
    foreach ($f in $copiedFiles) {
        Remove-Item -Path $f -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ""
Write-Host "Add-in desenregistre."
