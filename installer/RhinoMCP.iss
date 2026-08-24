#ifndef AppVersion
  #define AppVersion "0.4.2"
#endif

#ifndef PayloadDir
  #define PayloadDir "..\dist\windows"
#endif

#ifndef InstallerOutputDir
  #define InstallerOutputDir "..\dist"
#endif

[Setup]
AppId={{A538E51F-23EF-49AA-B086-0CD6B4BAC51F}
AppName=Rhino MCP
AppVersion={#AppVersion}
AppPublisher=millik66n
AppPublisherURL=https://github.com/millik66n/rhino-mcp
AppSupportURL=https://github.com/millik66n/rhino-mcp/issues
DefaultDirName={localappdata}\Programs\Rhino MCP
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern
OutputDir={#InstallerOutputDir}
OutputBaseFilename=RhinoMCP-Windows-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
SetupLogging=yes
UninstallDisplayName=Rhino MCP
VersionInfoVersion={#AppVersion}
VersionInfoCompany=millik66n
VersionInfoDescription=One-click Rhino and Grasshopper integration for AI clients
VersionInfoProductName=Rhino MCP
VersionInfoProductVersion={#AppVersion}
LicenseFile=..\LICENSE

[Files]
Source: "{#PayloadDir}\rhino-mcp\*"; DestDir: "{app}\server"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PayloadDir}\*.yak"; DestDir: "{app}\payload"; Flags: ignoreversion
Source: "Install-RhinoMCP.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "Uninstall-RhinoMCP.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion; AfterInstall: InstallRuntime

[Run]
Filename: "{code:GetRhinoExe}"; Description: "Open Rhino 8 now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\Uninstall-RhinoMCP.ps1"" -AppDir ""{app}"""; Flags: waituntilterminated runhidden skipifdoesntexist

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\Rhino MCP\Logs"

[Code]
var
  ClientPage: TInputOptionWizardPage;

function ProgramFiles64: String;
begin
  Result := GetEnv('ProgramW6432');
  if Result = '' then
    Result := GetEnv('ProgramFiles');
end;

function GetYakExe: String;
begin
  Result := AddBackslash(ProgramFiles64) + 'Rhino 8\System\yak.exe';
end;

function GetRhinoExe(Param: String): String;
begin
  Result := AddBackslash(ProgramFiles64) + 'Rhino 8\System\Rhino.exe';
end;

function ClientFromCommandLine: String;
begin
  Result := Lowercase(ExpandConstant('{param:CLIENT|}'));
end;

procedure InitializeWizard;
var
  RequestedClient: String;
begin
  ClientPage := CreateInputOptionPage(
    wpWelcome,
    'Choose your AI client',
    'Rhino MCP will configure it automatically.',
    'Choose the app you will use to prompt Rhino. The safe Grasshopper profile is enabled by default.',
    True,
    False
  );
  ClientPage.Add('Codex');
  ClientPage.Add('Claude');
  ClientPage.Add('Cursor');

  RequestedClient := ClientFromCommandLine;
  if RequestedClient = 'claude' then
    ClientPage.SelectedValueIndex := 1
  else if RequestedClient = 'cursor' then
    ClientPage.SelectedValueIndex := 2
  else
    ClientPage.SelectedValueIndex := 0;
end;

function GetSelectedClient(Param: String): String;
var
  RequestedClient: String;
begin
  RequestedClient := ClientFromCommandLine;
  if (RequestedClient = 'codex') or (RequestedClient = 'claude') or
     (RequestedClient = 'cursor') then
  begin
    Result := RequestedClient;
    exit;
  end;

  case ClientPage.SelectedValueIndex of
    1: Result := 'claude';
    2: Result := 'cursor';
  else
    Result := 'codex';
  end;
end;

function GetSelectedClientName(Param: String): String;
var
  Client: String;
begin
  Client := GetSelectedClient('');
  if Client = 'codex' then
    Result := 'Codex'
  else if Client = 'claude' then
    Result := 'Claude'
  else
    Result := 'Cursor';
end;

procedure InstallRuntime;
var
  PowerShell, Parameters, CleanupParameters: String;
  ResultCode, CleanupCode: Integer;
begin
  WizardForm.StatusLabel.Caption := 'Installing Rhino MCP and configuring ' +
    GetSelectedClientName('') + '...';
  PowerShell := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  Parameters := '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
    ExpandConstant('{app}\Install-RhinoMCP.ps1') + '" -AppDir "' + ExpandConstant('{app}') +
    '" -Client "' + GetSelectedClient('') + '" -Profile "grasshopper"';

  if (not Exec(PowerShell, Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode)) or
     (ResultCode <> 0) then
  begin
    CleanupParameters := '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
      ExpandConstant('{app}\Uninstall-RhinoMCP.ps1') + '" -AppDir "' +
      ExpandConstant('{app}') + '"';
    Exec(PowerShell, CleanupParameters, '', SW_HIDE, ewWaitUntilTerminated, CleanupCode);
    RaiseException(
      'Automatic setup failed. Nothing needs to be configured manually. ' +
      'Check ' + ExpandConstant('{localappdata}\Rhino MCP\Logs\install.log') +
      ' for the exact reason, then run Setup again.'
    );
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if not FileExists(GetYakExe) then
  begin
    Result := 'Rhino 8 was not found. Install Rhino 8 before installing Rhino MCP.';
    exit;
  end;

  if Exec(
    ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoLogo -NoProfile -NonInteractive -Command "if (Get-Process -Name Rhino -ErrorAction SilentlyContinue) { exit 10 }"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode
  ) and (ResultCode = 10) then
    Result := 'Rhino is currently running. Save your work and close Rhino before continuing.';
end;
