; packaging/installer.iss
; Inno Setup 6 script for MDRRMO Camera Agent
; Build with: pwsh ./scripts/build-installer.ps1 -Version 0.1.0
;
; Prerequisites before building:
;   pwsh ./scripts/fetch-winsw.ps1
;   pwsh ./scripts/fetch-mediamtx.ps1
;   dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ./dist

[Setup]
AppName=MDRRMO Camera Agent
AppVersion={#AppVersion}
AppPublisher=MDRRMO
AppPublisherURL=https://mdrrmo.gov.ph
DefaultDirName={autopf}\MDRRMO\CameraAgent
DefaultGroupName=MDRRMO
PrivilegesRequired=admin
; Stable filename — no version suffix — so the GitHub `releases/latest/download/`
; alias keeps working across releases without env-var rotation.
OutputBaseFilename=MdrrmoCameraAgent-Setup
OutputDir=..\output
AppId={{6B3F4C2A-8D1E-4F9A-B3C7-2E5D8A1F4B6C}
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
DisableProgramGroupPage=yes
WizardStyle=modern

[Files]
Source: "..\dist\MdrrmoCameraAgent.exe";      DestDir: "{app}"
Source: "..\third-party\winsw.exe";           DestDir: "{app}"; DestName: "winsw.exe"
Source: "..\third-party\mediamtx.exe";        DestDir: "{app}\mediamtx"
Source: "..\third-party\ffmpeg.exe";         DestDir: "{app}\mediamtx"
Source: "..\packaging\MdrrmoCameraAgent.xml"; DestDir: "{app}"

[Run]
; Install and start the Windows service, passing the provisioning bundle path resolved at
; runtime — either via the /bundle= CLI parameter or the file-picker prompt in [Code].
;
; runhidden was removed deliberately: when the agent's `install` subcommand fails (expired
; token, network error, antivirus quarantine), runhidden swallows stderr and the wizard
; happily reports "Finish" over a dead install. We now show the console (so errors are
; visible during install) and check the exit code in [Code]/CurStepChanged so failures
; surface as a real error dialog instead of a silent no-op. See AfterInstall= for the
; exit-code capture; the variable is read in CurStepChanged after the [Run] entry runs.
Filename: "{app}\MdrrmoCameraAgent.exe"; Parameters: "install ""{code:GetBundlePath}"""; \
  StatusMsg: "Enrolling agent and registering Windows service..."; \
  Flags: waituntilterminated; AfterInstall: AfterRunAgentInstall

[UninstallRun]
; Stop and remove the Windows service before files are deleted
Filename: "{app}\MdrrmoCameraAgent.exe"; Parameters: "uninstall"; Flags: runhidden skipifdoesntexist; RunOnceId: "UninstallService"

[UninstallDelete]
; Wipe the vault and all agent runtime data from ProgramData after the service is removed
; Maps to C:\ProgramData\MDRRMO\CameraAgent
Type: filesandordirs; Name: "{commonappdata}\MDRRMO\CameraAgent"

[Code]
var
  ResolvedBundlePath: String;
  AgentInstallSucceeded: Boolean;

// Called immediately after the [Run] entry that invokes `MdrrmoCameraAgent.exe install`.
// We can't read the process exit code directly from AfterInstall, but we CAN check whether
// the install actually produced its expected side effects (creds.dpapi + the Windows
// service registration). If neither exists, the install subcommand failed silently and we
// flag it so CurStepChanged can surface a real error dialog instead of a fake "Finish".
procedure AfterRunAgentInstall();
var
  CredsPath:    String;
  ServiceCheck: Integer;
begin
  CredsPath := ExpandConstant('{commonappdata}\MDRRMO\CameraAgent\creds.dpapi');
  AgentInstallSucceeded := FileExists(CredsPath);
  if not AgentInstallSucceeded then begin
    // Try to confirm via the service as a fallback — DPAPI write may have lagged.
    if Exec('sc.exe', 'query MdrrmoCameraAgent', '', SW_HIDE,
            ewWaitUntilTerminated, ServiceCheck) and (ServiceCheck = 0) then
      AgentInstallSucceeded := True;
  end;
end;

// Shown after [Run] but before the wizard's "Finish" page. If the install subcommand
// silently failed, present a real error so the operator knows to check logs / regenerate
// the bundle / contact the admin instead of believing the install succeeded.
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then begin
    if not AgentInstallSucceeded then begin
      MsgBox(
        'Agent enrollment failed.' + #13#10 + #13#10 +
        'The Windows service was NOT registered. The most common causes are:' + #13#10 +
        '  • The provisioning bundle''s enrollment token has already been used' + #13#10 +
        '    (single-use, 24h expiry — generate a fresh one in the dashboard).' + #13#10 +
        '  • The dashboard at api_base_url is unreachable from this PC.' + #13#10 +
        '  • Windows Defender / SmartScreen blocked the agent.' + #13#10 + #13#10 +
        'Please uninstall, request a fresh provisioning bundle from your MDRRMO admin, ' +
        'and run the installer again.',
        mbCriticalError, MB_OK);
    end;
  end;
end;

// Returns the bundle path passed via /bundle=, or asks the user to pick one
// with an Open File dialog if the parameter wasn't supplied. Cached after the
// first call so [Run] and any post-install steps see the same value.
function GetBundlePath(Param: String): String;
var
  Picked: String;
begin
  if ResolvedBundlePath <> '' then begin
    Result := ResolvedBundlePath;
    Exit;
  end;

  Picked := ExpandConstant('{param:bundle}');
  if Picked = '' then begin
    if not GetOpenFileName(
        'Select the MDRRMO provisioning bundle (.json) you received from your admin',
        Picked,
        ExpandConstant('{userdocs}'),
        'Provisioning bundle (*.json)|*.json|All files (*.*)|*.*',
        'json') then begin
      // User cancelled the picker — abort install with a clear message.
      MsgBox('Installation cancelled — no provisioning bundle selected.' + #13#10 + #13#10 +
             'You should have received a "mdrrmo-cctv-provision-<municipality>.json" file ' +
             'from your MDRRMO admin. Run this installer again and select that file.',
             mbError, MB_OK);
      Abort;
    end;
  end;

  if not FileExists(Picked) then begin
    MsgBox('The selected provisioning bundle does not exist:' + #13#10 + Picked,
           mbError, MB_OK);
    Abort;
  end;

  ResolvedBundlePath := Picked;
  Result := ResolvedBundlePath;
end;

function InitializeSetup(): Boolean;
begin
  // The bundle is resolved lazily by GetBundlePath — either from /bundle=, or
  // via an Open File dialog the first time it's needed. We always allow the
  // wizard to start so the user gets a real UI instead of a silent failure.
  Result := True;
end;
