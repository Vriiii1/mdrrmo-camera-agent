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
OutputBaseFilename=MdrrmoCameraAgent-Setup-{#AppVersion}
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
DisableProgramGroupPage=yes
WizardStyle=modern

[Files]
Source: "..\dist\MdrrmoCameraAgent.exe";      DestDir: "{app}"
Source: "..\third-party\winsw.exe";           DestDir: "{app}"; DestName: "winsw.exe"
Source: "..\third-party\mediamtx.exe";        DestDir: "{app}\mediamtx"
Source: "..\packaging\MdrrmoCameraAgent.xml"; DestDir: "{app}"

[Run]
; Install and start the Windows service, passing the provisioning bundle path supplied by the
; /bundle= installer parameter (e.g. MdrrmoCameraAgent-Setup-0.1.0.exe /bundle="C:\path\to\provisioning.json")
Filename: "{app}\MdrrmoCameraAgent.exe"; Parameters: "install ""{param:bundle}"""; Flags: runhidden

[UninstallRun]
; Stop and remove the Windows service before files are deleted
Filename: "{app}\MdrrmoCameraAgent.exe"; Parameters: "uninstall"; Flags: runhidden; RunOnceId: "UninstallService"

[UninstallDelete]
; Wipe the vault and all agent runtime data from ProgramData after the service is removed
; Maps to C:\ProgramData\MDRRMO\CameraAgent
Type: filesandordirs; Name: "{commonappdata}\MDRRMO\CameraAgent"
