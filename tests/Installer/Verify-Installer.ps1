param([Parameter(Mandatory = $true)][string]$InstallerPath)

$ErrorActionPreference = 'Stop'
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Ce test doit etre lance dans PowerShell administrateur.' }
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$testRoot = Join-Path $projectRoot 'artifacts\installer\verification'
$installPath = Join-Path $testRoot 'installed plugin'
$addinGuid = '{17829f30-3734-46d1-9247-83b8cca9a3f4}'
$uninstallKey = 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{565D545F-942F-4E2D-9D7B-1E64C234EFA5}_is1'
if (Get-Process SLDWORKS -ErrorAction SilentlyContinue) { throw 'Fermer SolidWorks avant ce test.' }
if (Test-Path -LiteralPath $uninstallKey) { throw 'Une installation de production existe deja : utiliser une machine de test.' }
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$registryKeys = @(
    "HKLM\SOFTWARE\Classes\CLSID\$addinGuid",
    'HKLM\SOFTWARE\Classes\SwMultiExport.Addin',
    "HKLM\SOFTWARE\SolidWorks\AddIns\$addinGuid",
    "HKCU\SOFTWARE\SolidWorks\AddInsStartup\$addinGuid"
)
$backups = @()
foreach ($key in $registryKeys) {
    $providerKey = $key.Replace('HKLM\', 'Registry::HKEY_LOCAL_MACHINE\').Replace('HKCU\', 'Registry::HKEY_CURRENT_USER\')
    if (Test-Path -LiteralPath $providerKey) {
        $backup = Join-Path $testRoot "registry-$($backups.Count).reg"
        & reg.exe export $key $backup /y | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Sauvegarde registre echouee' }
        $backups += $backup
    }
}

function Invoke-Setup([string[]]$SetupArguments, [int]$ExpectedExitCode) {
    $process = Start-Process -FilePath $InstallerPath -ArgumentList $SetupArguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne $ExpectedExitCode) { throw "Code installateur $($process.ExitCode), attendu $ExpectedExitCode" }
}

$common = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', ('/DIR="' + $installPath + '"'))
$runningHelper = $null
try {
    Invoke-Setup ($common + @('/SWDIR="D:\__missing_solidworks__"', ('/LOG="' + (Join-Path $testRoot 'missing-solidworks.log') + '"'))) 7
    if (Test-Path -LiteralPath (Join-Path $installPath 'SwMultiExport.dll')) { throw 'Installation malgre un prerequis absent' }
    Write-Output 'PASS installation blocked when SolidWorks folder is invalid'

    # Only this synthetic process is stopped. Never launch or stop SolidWorks.
    $helperSource = Join-Path $testRoot 'RunningProcess.cs'
    Set-Content -LiteralPath $helperSource -Value 'class RunningProcess { static void Main() { System.Threading.Thread.Sleep(120000); } }' -Encoding ASCII
    $helperExe = Join-Path $testRoot 'SLDWORKS.exe'
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo "/out:$helperExe" $helperSource
    if ($LASTEXITCODE -ne 0) { throw 'Compilation du processus de test echouee' }
    $runningHelper = Start-Process -FilePath $helperExe -WindowStyle Hidden -PassThru
    Invoke-Setup ($common + @('/TASKS=autostart', ('/LOG="' + (Join-Path $testRoot 'running-solidworks.log') + '"'))) 7
    Stop-Process -Id $runningHelper.Id
    $runningHelper = $null
    if (Test-Path -LiteralPath (Join-Path $installPath 'SwMultiExport.dll')) { throw 'Installation malgre SolidWorks ouvert' }
    Write-Output 'PASS installation blocked when SLDWORKS process is running'

    Invoke-Setup ($common + @('/TASKS=autostart', ('/LOG="' + (Join-Path $testRoot 'install.log') + '"'))) 0
    $dll = Join-Path $installPath 'SwMultiExport.dll'
    if (-not (Test-Path -LiteralPath $dll)) { throw 'DLL non installee' }
    if ((Get-FileHash -LiteralPath $dll).Hash -ne (Get-FileHash -LiteralPath (Join-Path $projectRoot 'artifacts\installer\payload\SwMultiExport.dll')).Hash) { throw 'Mauvaise DLL installee' }
    if (-not (Test-Path -LiteralPath (Join-Path $installPath 'runtimes\win-x64\native\assimp.dll'))) { throw 'Assimp natif absent' }
    if (Get-ChildItem -LiteralPath $installPath -Filter 'SolidWorks.Interop.*.dll') { throw 'Interop copiee dans le plugin' }
    $registration = Get-ItemProperty -LiteralPath "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes\CLSID\$addinGuid\InprocServer32"
    $registration | Select-Object Assembly,Class,CodeBase,RuntimeVersion,ThreadingModel | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $testRoot 'installed-registration.json')
    if (([Uri]$registration.CodeBase).LocalPath -ne $dll) { throw 'CodeBase incorrect' }
    $startup = Get-Item -LiteralPath "Registry::HKEY_CURRENT_USER\SOFTWARE\SolidWorks\AddInsStartup\$addinGuid"
    if ($startup.GetValue('') -ne 1) { throw 'Demarrage automatique absent' }
    Write-Output 'PASS install: files, native Assimp, COM CodeBase, autostart, no duplicate interops'

    $comCheck = @'
$ErrorActionPreference = 'Stop'
$swFolder = (Get-ItemProperty 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\SolidWorks\SOLIDWORKS 2023\Setup').'SolidWorks Folder'
foreach ($name in @('sldworks','swconst','swpublished')) {
    [Reflection.Assembly]::LoadFrom((Join-Path $swFolder "SolidWorks.Interop.$name.dll")) | Out-Null
}
$type = [Type]::GetTypeFromProgID('SwMultiExport.Addin', $true)
$addin = [Activator]::CreateInstance($type)
if ($addin.GetType().Assembly.Location -ne '__EXPECTED_DLL__') { throw 'COM activated the wrong DLL' }
Write-Output 'PASS COM activation of installed DLL'
'@
    $comCheck = $comCheck.Replace('__EXPECTED_DLL__', $dll.Replace("'", "''"))
    # This host is not SolidWorks, so it cannot resolve SW interops from its
    # executable directory. Supply them only for this isolated COM test.
    $interopFiles = @()
    try {
        $swFolder = (Get-ItemProperty 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\SolidWorks\SOLIDWORKS 2023\Setup').'SolidWorks Folder'
        foreach ($name in @('sldworks', 'swconst', 'swpublished')) {
            $destination = Join-Path $installPath "SolidWorks.Interop.$name.dll"
            Copy-Item -LiteralPath (Join-Path $swFolder "SolidWorks.Interop.$name.dll") -Destination $destination
            $interopFiles += $destination
        }
        & "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -Command $comCheck
        if ($LASTEXITCODE -ne 0) { throw 'Activation COM echouee' }
    }
    finally { foreach ($file in $interopFiles) { Remove-Item -LiteralPath $file -Force } }

    Invoke-Setup ($common + @('/TASKS=autostart', ('/LOG="' + (Join-Path $testRoot 'update.log') + '"'))) 0
    Write-Output 'PASS reinstall/update using the same installation identity'

    $uninstaller = Join-Path $installPath 'unins000.exe'
    $process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $testRoot 'uninstall.log') + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw 'Desinstallation echouee' }
    if (Test-Path -LiteralPath $dll) { throw 'DLL restante apres desinstallation' }
    foreach ($key in $registryKeys) {
        $providerKey = $key.Replace('HKLM\', 'Registry::HKEY_LOCAL_MACHINE\').Replace('HKCU\', 'Registry::HKEY_CURRENT_USER\')
        if (Test-Path -LiteralPath $providerKey) { throw "Cle restante : $key" }
    }
    if (Test-Path -LiteralPath $uninstallKey) { throw 'Entree de desinstallation restante' }
    Write-Output 'PASS uninstall: plugin files and registry entries removed'
}
finally {
    if ($runningHelper -and -not $runningHelper.HasExited) { Stop-Process -Id $runningHelper.Id }
    $leftoverUninstaller = Join-Path $installPath 'unins000.exe'
    if (Test-Path -LiteralPath $leftoverUninstaller) {
        $cleanup = Start-Process -FilePath $leftoverUninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -WindowStyle Hidden -Wait -PassThru
        if ($cleanup.ExitCode -ne 0) { Write-Warning 'Le nettoyage de l''installation de test a echoue.' }
    }
    foreach ($backup in $backups) {
        & reg.exe import $backup | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Restauration du registre echouee : $backup" }
    }
    Write-Output 'Development COM registration restored'
}
