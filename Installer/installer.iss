; =====================================================================
; SwiftList Inno Setup Script
; =====================================================================

#define AppName "SwiftList"
#define AppPublisher "SwiftList developer"
#define AppURL "https://github.com/swiftlist/SwiftList"
#define AppExeName "SwiftList.App.exe"
#define ServiceExeName "SwiftList.Service.exe"
#define ServiceName "SwiftListService"

[Setup]
AppId={{D37D0B75-B5E3-40D9-92EE-429C7D4D7F2A}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={commonpf64}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=SwiftList-Setup
SetupIconFile=..\App\logo.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
VersionInfoVersion={#AppVersion4}
VersionInfoTextVersion={#AppVersion}
; Automatically check and close running instances of the App
CloseApplications=yes
CloseApplicationsFilter={#AppExeName}

[Languages]
Name: "en_US"; MessagesFile: "compiler:Default.isl"
Name: "zh_CN"; MessagesFile: "ChineseSimplified.isl"

#include "Languages\en-US.iss"
#include "Languages\zh-CN.iss"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startmenuicon"; Description: "{cm:CreateStartMenuIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\SwiftList\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{commonprograms}\{#AppName}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: startmenuicon
Name: "{commonprograms}\{#AppName}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"; Tasks: startmenuicon
Name: "{commondesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; Install the service using sc.exe
Filename: "sc.exe"; Parameters: "create {#ServiceName} binPath= ""\""{app}\{#ServiceExeName}""\"" start= auto DisplayName= ""SwiftList Background Service"""; Flags: runhidden; StatusMsg: "{cm:InstallService}"
Filename: "sc.exe"; Parameters: "description {#ServiceName} ""SwiftList NTFS USN Journal file indexing and real-time monitoring service."""; Flags: runhidden
; Run the app as original non-elevated user at the end
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchApp}"; Flags: postinstall nowait runasoriginaluser

[UninstallRun]
; Stop and delete service on uninstall
Filename: "sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden
Filename: "sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden

[Code]
var
  DownloadPage: TDownloadWizardPage;

function IsDotNet10Installed(): Boolean;
var
  FindRec: TFindRec;
  Path: string;
begin
  Result := False;
  Path := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if FindFirst(Path + '\10.*', FindRec) then
  begin
    Result := True;
    FindClose(FindRec);
  end;
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  Result := True;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), CustomMessage('DotNetDownloading'), @OnDownloadProgress);
end;

function PrepareToInstall(var NeedsReboot: Boolean): String;
var
  ResultCode: Integer;
  InstallerPath: string;
begin
  Result := '';
  
  // 1. Force stop and delete service before installing new files
  Exec('sc.exe', 'stop ' + '{#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/F /IM ' + '{#ServiceExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('sc.exe', 'delete ' + '{#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // 2. Check and Download .NET 10.0 Desktop Runtime if missing
  if not IsDotNet10Installed() then
  begin
    DownloadPage.Clear;
    DownloadPage.Add('https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe', 'windowsdesktop-runtime-10-win-x64.exe', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
      except
        Result := 'Failed to download .NET 10.0 Desktop Runtime. Please install it manually.';
        Exit;
      end;
    finally
      DownloadPage.Hide;
    end;

    // Install the downloaded runtime
    WizardForm.StatusLabel.Caption := CustomMessage('DotNetInstalling');
    InstallerPath := ExpandConstant('{tmp}\windowsdesktop-runtime-10-win-x64.exe');
    if not Exec(InstallerPath, '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    begin
      Result := 'Failed to install .NET 10.0 Desktop Runtime (Code: ' + IntToStr(ResultCode) + ').';
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    // Force stop app and service on uninstallation
    Exec('taskkill.exe', '/F /IM ' + '{#AppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('sc.exe', 'stop ' + '{#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('taskkill.exe', '/F /IM ' + '{#ServiceExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('sc.exe', 'delete ' + '{#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
