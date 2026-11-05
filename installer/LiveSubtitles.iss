; PayloadDir and AppVersion are supplied by scripts/Build-Installer.ps1.
[Setup]
AppId=optima-xu.LiveSubtitles
AppName=LiveSubtitles
AppVersion={#AppVersion}
AppPublisher=optima-xu
AppPublisherURL=https://github.com/optima-xu/LiveSubtitles
DefaultDirName={localappdata}\Programs\LiveSubtitles
DefaultGroupName=LiveSubtitles
DisableDirPage=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
SetupArchitecture=x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupMutex=optima-xu.LiveSubtitles.Setup
SetupIconFile=..\src\LiveSubtitles\Assets\livesubtitles.ico
UninstallDisplayIcon={app}\app\LiveSubtitles.exe
UninstallDisplayName=LiveSubtitles
OutputBaseFilename=LiveSubtitles-Setup-v{#AppVersion}-windows-x64
WizardStyle=modern
Compression=lzma2/fast
SolidCompression=yes
SetupLogging=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Messages]
english.SelectDirLabel3=Setup installs [name] and its local models in the folder below. Choose a folder you can write to.
english.DiskSpaceMBLabel=Allow at least 12 GB of free space for a new installation with models.
english.DiskSpaceGBLabel=Allow at least 12 GB of free space for a new installation with models.
chinesesimplified.SelectDirLabel3=程序和模型将安装到以下文件夹。请选择当前账户可写入的目录。
chinesesimplified.DiskSpaceMBLabel=首次安装（含模型）请在所选磁盘预留至少 12 GB 空间。
chinesesimplified.DiskSpaceGBLabel=首次安装（含模型）请在所选磁盘预留至少 12 GB 空间。

[CustomMessages]
english.ModelTitle=Preparing speech recognition and translation
english.ModelDescription=The first installation downloads the local models. Allow at least 12 GB of free space on the selected drive.
english.ModelProgress=Preparing local models and Ollama…
english.ModelFailed=Model setup could not finish. Open the setup log for details and retry: %1
chinesesimplified.ModelTitle=正在准备语音识别和翻译模型
chinesesimplified.ModelDescription=首次安装会下载本地模型，请在所选磁盘预留至少 12 GB 空间。
chinesesimplified.ModelProgress=正在准备本地模型和 Ollama…
chinesesimplified.ModelFailed=模型准备未完成。请查看安装日志中的原因，处理后重试。日志位于 %1

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Keep these first so the preparation step can extract them without unpacking the app.
Source: "{#PayloadDir}\Install.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\resources\*"; DestDir: "{app}\resources"; Flags: ignoreversion
Source: "{#PayloadDir}\app\*"; DestDir: "{app}\app"; Flags: ignoreversion recursesubdirs
Source: "{#PayloadDir}\scripts\Diagnose.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "{#PayloadDir}\docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion recursesubdirs
Source: "{#PayloadDir}\README*.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\CONTRIBUTING.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\TESTING.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\Install.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\Start.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\Start.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\LiveSubtitles"; Filename: "{app}\app\LiveSubtitles.exe"; WorkingDir: "{app}\app"; AppUserModelID: "optima-xu.LiveSubtitles"
Name: "{userdesktop}\LiveSubtitles"; Filename: "{app}\app\LiveSubtitles.exe"; WorkingDir: "{app}\app"; AppUserModelID: "optima-xu.LiveSubtitles"; Tasks: desktopicon

[InstallDelete]
Type: files; Name: "{userdesktop}\LiveSubtitles.lnk"; Tasks: not desktopicon; Check: OwnsDesktopShortcut

[Run]
Filename: "{app}\app\LiveSubtitles.exe"; WorkingDir: "{app}\app"; Description: "{cm:LaunchProgram,LiveSubtitles}"; Flags: postinstall nowait skipifsilent

[Code]
var
  ModelPage: TOutputMarqueeProgressWizardPage;

function OwnsDesktopShortcut: Boolean;
var
  Link: String;
  Shell, Folder, Item, Shortcut: Variant;
begin
  Result := False;
  Link := ExpandConstant('{userdesktop}\LiveSubtitles.lnk');
  if FileExists(Link) then
    try
      { Explorer's link reader preserves Unicode paths on every Windows system locale. }
      Shell := CreateOleObject('Shell.Application');
      Folder := Shell.NameSpace(ExtractFileDir(Link));
      Item := Folder.ParseName(ExtractFileName(Link));
      Shortcut := Item.GetLink;
      Result := CompareText(Shortcut.Path, ExpandConstant('{app}\app\LiveSubtitles.exe')) = 0;
    except
      Log(GetExceptionMessage);
    end;
end;

procedure InitializeWizard;
begin
  ModelPage := CreateOutputMarqueeProgressPage(CustomMessage('ModelTitle'), CustomMessage('ModelDescription'));
end;

procedure ModelOutput(const Line: String; const Error, FirstLine: Boolean);
begin
  Log(Line);
  if (Pos('Checking ', Line) = 1) or (Pos('Downloading ', Line) = 1) or (Pos('Importing ', Line) = 1) then
    ModelPage.SetText(CustomMessage('ModelProgress'), Line);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ScriptDir, Params: String;
  ExitCode: Integer;
begin
  Result := '';
  ModelPage.SetText(CustomMessage('ModelProgress'), '');
  ModelPage.Show;
  try
    ModelPage.Animate;
    ExtractTemporaryFiles('{app}\Install.ps1');
    ExtractTemporaryFiles('{app}\resources\*');
    { The extracted files retain their literal destination prefix in the temporary directory. }
    ScriptDir := ExpandConstant('{tmp}\') + '{app}';
    Params := '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
      ScriptDir + '\Install.ps1" -ApplicationDirectory "' + WizardDirValue + '"';
    if not ExecAndLogOutput(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      Params, ScriptDir, SW_HIDE, ewWaitUntilTerminated, ExitCode, @ModelOutput) or (ExitCode <> 0) then
      Result := FmtMessage(CustomMessage('ModelFailed'), [ExpandConstant('{log}')]);
  finally
    ModelPage.Hide;
  end;
end;
