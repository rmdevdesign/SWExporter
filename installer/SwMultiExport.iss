; Build with scripts/build-installer.ps1. Only the add-in's COM class is registered.
#ifndef PayloadDir
  #error PayloadDir is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif
#ifndef ProductVersion
  #define ProductVersion "0.1.0"
#endif
#ifndef AssemblyFullName
  #error AssemblyFullName is required
#endif
#ifndef AssemblyVersion
  #error AssemblyVersion is required
#endif
#define AddinGuid "{{17829f30-3734-46d1-9247-83b8cca9a3f4}"
#define ClsidKey "Software\Classes\CLSID\" + AddinGuid
#define InprocKey ClsidKey + "\InprocServer32"

[Setup]
AppId={{565D545F-942F-4E2D-9D7B-1E64C234EFA5}
AppName=SW Multi Export
AppVersion={#ProductVersion}
AppPublisher=RMDev
AppPublisherURL=https://github.com/rmdevdesign/SWExporter
DefaultDirName={autopf}\SW Multi Export
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=SWMultiExport-Setup-{#ProductVersion}-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no
UninstallDisplayIcon={app}\SwMultiExport.dll
SetupLogging=yes

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "autostart"; Description: "Charger le plugin au démarrage de SolidWorks pour ce compte Windows"; Flags: checkedonce

[Files]
Source: "{#PayloadDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\runtimes\win-x64\native\assimp.dll"; DestDir: "{app}\runtimes\win-x64\native"; Flags: ignoreversion
Source: "{#PayloadDir}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
Root: HKLM; Subkey: "{#ClsidKey}"; ValueType: string; ValueName: ""; ValueData: "SwMultiExport.Addin.SwAddin"; Flags: uninsdeletekey
Root: HKLM; Subkey: "{#InprocKey}"; ValueType: string; ValueName: ""; ValueData: "mscoree.dll"
Root: HKLM; Subkey: "{#InprocKey}"; ValueType: string; ValueName: "ThreadingModel"; ValueData: "Both"
Root: HKLM; Subkey: "{#InprocKey}"; ValueType: string; ValueName: "Class"; ValueData: "SwMultiExport.Addin.SwAddin"
Root: HKLM; Subkey: "{#InprocKey}"; ValueType: string; ValueName: "Assembly"; ValueData: "{#AssemblyFullName}"
Root: HKLM; Subkey: "{#InprocKey}"; ValueType: string; ValueName: "RuntimeVersion"; ValueData: "v4.0.30319"
Root: HKLM; Subkey: "{#InprocKey}"; ValueType: string; ValueName: "CodeBase"; ValueData: "{code:GetCodeBase}"
Root: HKLM; Subkey: "{#InprocKey}\{#AssemblyVersion}"; ValueType: string; ValueName: "Class"; ValueData: "SwMultiExport.Addin.SwAddin"
Root: HKLM; Subkey: "{#InprocKey}\{#AssemblyVersion}"; ValueType: string; ValueName: "Assembly"; ValueData: "{#AssemblyFullName}"
Root: HKLM; Subkey: "{#InprocKey}\{#AssemblyVersion}"; ValueType: string; ValueName: "RuntimeVersion"; ValueData: "v4.0.30319"
Root: HKLM; Subkey: "{#InprocKey}\{#AssemblyVersion}"; ValueType: string; ValueName: "CodeBase"; ValueData: "{code:GetCodeBase}"
Root: HKLM; Subkey: "{#ClsidKey}\ProgId"; ValueType: string; ValueName: ""; ValueData: "SwMultiExport.Addin"
Root: HKLM; Subkey: "{#ClsidKey}\Implemented Categories\{{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\SwMultiExport.Addin"; ValueType: string; ValueName: ""; ValueData: "SwMultiExport.Addin.SwAddin"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\SwMultiExport.Addin\CLSID"; ValueType: string; ValueName: ""; ValueData: "{#AddinGuid}"
Root: HKLM; Subkey: "Software\SolidWorks\AddIns\{#AddinGuid}"; ValueType: dword; ValueName: ""; ValueData: "1"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\SolidWorks\AddIns\{#AddinGuid}"; ValueType: string; ValueName: "Title"; ValueData: "SW Multi Export"
Root: HKLM; Subkey: "Software\SolidWorks\AddIns\{#AddinGuid}"; ValueType: string; ValueName: "Description"; ValueData: "Export GLB, glTF et FBX"
Root: HKCU; Subkey: "Software\SolidWorks\AddInsStartup\{#AddinGuid}"; ValueType: dword; ValueName: ""; ValueData: "1"; Tasks: autostart; Flags: uninsdeletekey

[Code]
var
  SolidWorksPage: TInputDirWizardPage;

function GetCodeBase(Param: String): String;
begin
  Result := ExpandConstant('{app}\SwMultiExport.dll');
  StringChangeEx(Result, '\', '/', True);
  // Match RegAsm's CodeBase format, including literal spaces in the path.
  Result := 'file:///' + Result;
end;

function ValidSolidWorksFolder(Folder: String): Boolean;
begin
  Result := FileExists(AddBackslash(Folder) + 'SLDWORKS.exe') and
    FileExists(AddBackslash(Folder) + 'SolidWorks.Interop.sldworks.dll') and
    FileExists(AddBackslash(Folder) + 'SolidWorks.Interop.swconst.dll') and
    FileExists(AddBackslash(Folder) + 'SolidWorks.Interop.swpublished.dll');
end;

function DetectSolidWorksFolder: String;
var
  Versions: TArrayOfString;
  I: Integer;
  Candidate: String;
begin
  Result := '';
  if RegGetSubkeyNames(HKLM64, 'Software\SolidWorks', Versions) then
    for I := GetArrayLength(Versions) - 1 downto 0 do
      if RegQueryStringValue(HKLM64, 'Software\SolidWorks\' + Versions[I] + '\Setup', 'SolidWorks Folder', Candidate) then
        if ValidSolidWorksFolder(Candidate) then begin Result := Candidate; Exit; end;
  if RegQueryStringValue(HKLM64, 'Software\Microsoft\Windows\CurrentVersion\App Paths\SLDWORKS.exe', '', Candidate) then begin
    Candidate := ExtractFileDir(RemoveQuotes(Candidate));
    if ValidSolidWorksFolder(Candidate) then begin Result := Candidate; Exit; end;
  end;
  Candidate := ExpandConstant('{autopf}\SOLIDWORKS Corp\SOLIDWORKS');
  if ValidSolidWorksFolder(Candidate) then Result := Candidate;
end;

function SolidWorksRunning: Boolean;
var
  Locator, Services, Processes: Variant;
begin
  // Fail closed if Windows cannot check running processes.
  Result := True;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Services := Locator.ConnectServer('.', 'root\CIMV2');
    Processes := Services.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name=''SLDWORKS.exe''');
    Result := Processes.Count > 0;
  except
    Log('Impossible de vérifier les processus SolidWorks : ' + GetExceptionMessage);
  end;
end;

function InitializeSetup: Boolean;
begin
  Result := IsDotNetInstalled(net48, 0);
  if not Result then
    SuppressibleMsgBox('Installez .NET Framework 4.8 ou plus récent, puis relancez cet installateur.', mbError, MB_OK, IDOK);
end;

procedure InitializeWizard;
begin
  SolidWorksPage := CreateInputDirPage(wpSelectDir, 'Installation de SolidWorks',
    'Vérifiez le dossier de SolidWorks.',
    'Le dossier est détecté automatiquement. Sinon, sélectionnez celui contenant SLDWORKS.exe. Fermez SolidWorks avant de continuer.', False, '');
  SolidWorksPage.Add('Dossier de SolidWorks :');
  SolidWorksPage.Values[0] := ExpandConstant('{param:SWDIR|}') ;
  if SolidWorksPage.Values[0] = '' then SolidWorksPage.Values[0] := DetectSolidWorksFolder;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  // Silent installs validate prerequisites in PrepareToInstall without a popup.
  if WizardSilent then Exit;
  if CurPageID = SolidWorksPage.ID then begin
    Result := ValidSolidWorksFolder(SolidWorksPage.Values[0]);
    if not Result then SuppressibleMsgBox('Sélectionnez le dossier contenant SLDWORKS.exe et ses DLL interop.', mbError, MB_OK, IDOK);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not ValidSolidWorksFolder(SolidWorksPage.Values[0]) then
    Result := 'SolidWorks est introuvable. Vérifiez le dossier sélectionné.'
  else if SolidWorksRunning then
    Result := 'Fermez SolidWorks après avoir sauvegardé vos documents, puis réessayez.';
end;

function InitializeUninstall: Boolean;
begin
  Result := not SolidWorksRunning;
  if not Result then
    SuppressibleMsgBox('Fermez SolidWorks après avoir sauvegardé vos documents, puis relancez la désinstallation.', mbError, MB_OK, IDOK);
end;
