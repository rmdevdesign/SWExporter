param(
    [string]$Version = '0.1.0',
    [string]$SwInstallDir = 'D:\Softwares\SolidworksCorp\SOLIDWORKS\api\redist\',
    [string]$InnoCompiler
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$payload = Join-Path $projectRoot 'artifacts\installer\payload'
$output = Join-Path $projectRoot 'artifacts\installer'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version attendue : majeure.mineure.correctif' }
if (-not $InnoCompiler) {
    $compilerCandidates = @(
        (Join-Path $projectRoot 'artifacts\tools\inno\ISCC.exe'),
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    $InnoCompiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $InnoCompiler -or -not (Test-Path -LiteralPath $InnoCompiler)) {
    throw 'Installer Inno Setup 6, ou fournir -InnoCompiler avec le chemin de ISCC.exe.'
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
if (Test-Path -LiteralPath $payload) {
    $resolvedPayload = [IO.Path]::GetFullPath($payload)
    $expectedPayload = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts\installer\payload'))
    if ($resolvedPayload -ne $expectedPayload) { throw 'Dossier de preparation inattendu' }
    Remove-Item -LiteralPath $resolvedPayload -Recurse -Force
}
New-Item -ItemType Directory -Path $payload -Force | Out-Null

& dotnet build (Join-Path $projectRoot 'src\SwMultiExport\SwMultiExport.csproj') -c Release -p:Platform=x64 "-p:SwInstallDir=$SwInstallDir" "-p:OutputPath=$payload\"
if ($LASTEXITCODE -ne 0) { throw 'Compilation Release echouee' }
# The DLL interops belong to SolidWorks. Never ship a second copy in the add-in.
$interops = Get-ChildItem -LiteralPath $payload -Filter 'SolidWorks.Interop.*.dll'
if ($interops) { throw 'Les DLL interop SolidWorks ne doivent pas etre incluses dans le plugin.' }
$required = @('SwMultiExport.dll', 'MeshDecimator.dll', 'AssimpNet.dll', 'SharpGLTF.Core.dll', 'SharpGLTF.Toolkit.dll', 'runtimes\win-x64\native\assimp.dll')
foreach ($file in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $file))) { throw "Dependance manquante : $file" }
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $payload
$licenses = Join-Path $payload 'licenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'third_party\MeshDecimator\LICENSE.md') -Destination (Join-Path $licenses 'MeshDecimator-LICENSE.md')
Get-ChildItem -LiteralPath (Join-Path $projectRoot 'installer\licenses') -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $licenses
}
$assets = Get-Content -LiteralPath (Join-Path $projectRoot 'src\SwMultiExport\obj\project.assets.json') -Raw | ConvertFrom-Json
foreach ($package in $assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' }) {
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $packagePath = Join-Path $folder $package.Value.path
        if (-not (Test-Path -LiteralPath $packagePath)) { continue }
        $packageName = $package.Name.Replace('/', '-')
        Get-ChildItem -LiteralPath $packagePath -File | Where-Object { $_.Name -match 'license|notice|\.nuspec$' } | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $licenses "$packageName-$($_.Name)")
        }
        break
    }
}
$assembly = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $payload 'SwMultiExport.dll'))
& $InnoCompiler "/DPayloadDir=$payload" "/DOutputDir=$output" "/DProductVersion=$Version" "/DAssemblyFullName=$($assembly.FullName)" "/DAssemblyVersion=$($assembly.Version)" (Join-Path $projectRoot 'installer\SwMultiExport.iss')
if ($LASTEXITCODE -ne 0) { throw 'Compilation de l''installateur echouee' }
$installer = Join-Path $output "SWMultiExport-Setup-$Version-x64.exe"
$checksum = Get-FileHash -LiteralPath $installer -Algorithm SHA256
"$($checksum.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($installer))" | Set-Content -LiteralPath "$installer.sha256" -Encoding ASCII
Get-Item -LiteralPath $installer | Select-Object FullName, Length
