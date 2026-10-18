#define MyAppName "Hyper Battery Monitor"
#ifndef MyAppVersion
  #error MyAppVersion must be supplied by Release\Build-Release.ps1
#endif
#ifndef MyAppNumericVersion
  #error MyAppNumericVersion must be supplied by Release\Build-Release.ps1
#endif
#ifndef MyPublishDir
  #error MyPublishDir must be supplied by Release\Build-Release.ps1
#endif
#define MyAppPublisher "Dave Santana"
#define MyAppExeName "Hyper Battery Monitor.exe"
#define LegacyAppName "HyperX Battery Monitor"
#define LegacyAppExeName "HyperX Battery Monitor.exe"
#define MyAppUserModelId "DaveSantana.HyperXBatteryMonitor"
#define MyAppMutexName "HyperBatteryMonitor_8D7E0A6C9D5B4F0B9C3E5F5E9C2A1B71"
#define StartupRunSubkey "Software\Microsoft\Windows\CurrentVersion\Run"
#define StartupValueName "HyperBatteryMonitor"
#define LegacyStartupValueName "HyperXBatteryMonitor"
#define OlderLegacyStartupValueName "HyperXBatteryTray"
#define CleanInstallTaskName "cleaninstall"
#define CleanInstallTaskDescription "Perform a clean installation (delete existing settings and battery history)"
#define CleanInstallTaskGroup "Installation options:"
#define ResetUserDataArgument "--reset-user-data"
#define MigrateUserDataArgument "--migrate-user-data"
#define CleanInstallErrorMessage "Could not complete the clean installation. Existing user data may not have been fully removed."
#define MigrationErrorMessage "Could not migrate the existing Hyper Battery Monitor settings and battery history. The previous data was preserved."
#define LegacyCleanupErrorMessage "Hyper Battery Monitor was installed, but the previous application folder could not be removed."

[Setup]
AppId={{8D7E0A6C-9D5B-4F0B-9C3E-5F5E9C2A1B71}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
VersionInfoVersion={#MyAppNumericVersion}
VersionInfoProductVersion={#MyAppNumericVersion}
VersionInfoTextVersion={#MyAppVersion}
VersionInfoProductTextVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
UsePreviousAppDir=no
DefaultGroupName={#MyAppName}
UsePreviousGroup=no
OutputDir=..\Releases
OutputBaseFilename=HyperBatteryMonitor-Setup-v{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
PrivilegesRequired=admin
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=no
AppMutex={#MyAppMutexName}

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
Type: files; Name: "{autodesktop}\{#LegacyAppName}.lnk"
Type: files; Name: "{autoprograms}\{#LegacyAppName}\{#LegacyAppName}.lnk"
Type: dirifempty; Name: "{autoprograms}\{#LegacyAppName}"

[Registry]
; Startup is user-controlled at runtime, so Setup must not create or change these values.
; The uninstall log tracks all current and legacy names so Uninstall removes orphaned startup entries.
Root: HKCU; Subkey: "{#StartupRunSubkey}"; ValueType: none; ValueName: "{#StartupValueName}"; Flags: dontcreatekey uninsdeletevalue
Root: HKCU; Subkey: "{#StartupRunSubkey}"; ValueType: none; ValueName: "{#LegacyStartupValueName}"; Flags: dontcreatekey uninsdeletevalue
Root: HKCU; Subkey: "{#StartupRunSubkey}"; ValueType: none; ValueName: "{#OlderLegacyStartupValueName}"; Flags: dontcreatekey uninsdeletevalue

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; AppUserModelID: "{#MyAppUserModelId}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; AppUserModelID: "{#MyAppUserModelId}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"
Name: "{#CleanInstallTaskName}"; Description: "{#CleanInstallTaskDescription}"; GroupDescription: "{#CleanInstallTaskGroup}"; Flags: unchecked checkedonce

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
const
  PreviousUninstallSubkey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8D7E0A6C-9D5B-4F0B-9C3E-5F5E9C2A1B71}_is1';

var
  PreviousInstallDir: String;
  PreviousInstallDetected: Boolean;

function TryReadPreviousInstallDir(var InstallDir: String): Boolean;
begin
  InstallDir := '';
  Result := False;

  if IsWin64 then
    Result := RegQueryStringValue(
      HKLM64,
      PreviousUninstallSubkey,
      'InstallLocation',
      InstallDir);

  if not Result then
    Result := RegQueryStringValue(
      HKLM32,
      PreviousUninstallSubkey,
      'InstallLocation',
      InstallDir);

  if Result then
    InstallDir := RemoveBackslashUnlessRoot(InstallDir);
end;

function InitializeSetup: Boolean;
begin
  PreviousInstallDetected := TryReadPreviousInstallDir(PreviousInstallDir);
  Result := True;
end;

procedure RegisterExtraCloseApplicationsResources;
var
  ExecutablePath: String;
begin
  if not PreviousInstallDetected then
    Exit;

  ExecutablePath := AddBackslash(PreviousInstallDir) + '{#LegacyAppExeName}';
  if FileExists(ExecutablePath) then
  begin
#if Ver >= EncodeVer(7,0,0,0)
    RegisterExtraCloseApplicationsResource(ExecutablePath);
#else
    RegisterExtraCloseApplicationsResource(ExecutablePath, False);
#endif
  end;

  ExecutablePath := AddBackslash(PreviousInstallDir) + '{#MyAppExeName}';
  if FileExists(ExecutablePath) then
  begin
#if Ver >= EncodeVer(7,0,0,0)
    RegisterExtraCloseApplicationsResource(ExecutablePath);
#else
    RegisterExtraCloseApplicationsResource(ExecutablePath, False);
#endif
  end;
end;

function ExecuteMaintenanceArgument(
  const Argument: String;
  const ErrorMessage: String): Boolean;
var
  ResultCode: Integer;
  Launched: Boolean;
begin
  ResultCode := -1;

  Launched := ExecAsOriginalUser(
    ExpandConstant('{app}\{#MyAppExeName}'),
    Argument,
    ExpandConstant('{app}'),
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode);

  if not Launched then
  begin
    Log('Unable to start the maintenance process as the original user. ' +
      'Argument: ' + Argument + '. ' + ErrorMessage);
    Result := False;
    Exit;
  end;

  Result := ResultCode = 0;
  if not Result then
    Log(ErrorMessage + ' Maintenance process exit code: ' +
      IntToStr(ResultCode) +
      '. Check the original user TEMP\HBM-Maintenance.log for the .NET exception.');
end;

function IsSafePreviousInstallDirectory: Boolean;
var
  NormalizedPreviousDir: String;
  NormalizedCurrentDir: String;
  DirectoryName: String;
  HasKnownExecutable: Boolean;
begin
  Result := False;

  if not PreviousInstallDetected then
    Exit;

  NormalizedPreviousDir := RemoveBackslashUnlessRoot(PreviousInstallDir);
  NormalizedCurrentDir := RemoveBackslashUnlessRoot(ExpandConstant('{app}'));

  if CompareText(NormalizedPreviousDir, NormalizedCurrentDir) = 0 then
    Exit;

  DirectoryName := ExtractFileName(NormalizedPreviousDir);
  if (CompareText(DirectoryName, '{#LegacyAppName}') <> 0) and
     (CompareText(DirectoryName, '{#MyAppName}') <> 0) then
    Exit;

  HasKnownExecutable :=
    FileExists(AddBackslash(NormalizedPreviousDir) + '{#LegacyAppExeName}') or
    FileExists(AddBackslash(NormalizedPreviousDir) + '{#MyAppExeName}');

  Result := HasKnownExecutable;
end;

function RemovePreviousInstallDirectory: Boolean;
begin
  Result := True;

  if not IsSafePreviousInstallDirectory then
    Exit;

  Log('Removing previous application directory: ' + PreviousInstallDir);
  Result := DelTree(
    PreviousInstallDir,
    True,
    True,
    True);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep <> ssPostInstall then
    Exit;

  if WizardIsTaskSelected('{#CleanInstallTaskName}') then
  begin
    if not ExecuteMaintenanceArgument(
      '{#ResetUserDataArgument}',
      '{#CleanInstallErrorMessage}') then
    begin
      RaiseException('{#CleanInstallErrorMessage}');
    end;
  end
  else
  begin
    if not ExecuteMaintenanceArgument(
      '{#MigrateUserDataArgument}',
      '{#MigrationErrorMessage}') then
    begin
      RaiseException('{#MigrationErrorMessage}');
    end;
  end;

  if not RemovePreviousInstallDirectory then
    RaiseException('{#LegacyCleanupErrorMessage}');
end;
