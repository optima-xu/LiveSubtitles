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
english.DownloadStep=Downloading %1
english.VerifyStep=Verifying %1
english.ExtractStep=Extracting %1
english.ImportStep=Importing %1
english.StartService=Starting Ollama
english.SaveSettings=Saving settings
english.WaitingTransfer=Waiting for data from the server…
english.CancelModels=Cancel installation? Downloaded data will be kept so you can resume by choosing the same folder next time.
english.CancelledModels=Installation cancelled. Run setup again in the same folder to resume.
english.PurgeData=Also remove this installation’s local models, settings and diagnostic logs?%n%nChoose Yes to free disk space, or No to keep these files for reuse. Your exported subtitles and separately installed Ollama will be kept.
english.PurgeError=Some local data could not be removed. Close LiveSubtitles and check this folder: %1
chinesesimplified.ModelTitle=正在准备语音识别和翻译模型
chinesesimplified.ModelDescription=首次安装会下载本地模型，请在所选磁盘预留至少 12 GB 空间。
chinesesimplified.ModelProgress=正在准备本地模型和 Ollama…
chinesesimplified.ModelFailed=模型准备未完成。请查看安装日志中的原因，处理后重试。日志位于 %1
chinesesimplified.DownloadStep=正在下载 %1
chinesesimplified.VerifyStep=正在校验 %1
chinesesimplified.ExtractStep=正在解压 %1
chinesesimplified.ImportStep=正在导入 %1
chinesesimplified.StartService=正在启动 Ollama
chinesesimplified.SaveSettings=正在保存设置
chinesesimplified.WaitingTransfer=正在等待服务器传输数据…
chinesesimplified.CancelModels=确定取消安装吗？已下载的部分会保留，下次选择相同目录即可继续。
chinesesimplified.CancelledModels=安装已取消。重新运行安装程序并选择相同目录即可继续。
chinesesimplified.PurgeData=是否同时删除本次安装的本地模型、个人设置和诊断日志？%n%n选择“是”可释放磁盘空间，选择“否”可保留这些文件供重装使用。已导出的字幕和单独安装的 Ollama 都会保留。
chinesesimplified.PurgeError=部分本地数据未能删除。请关闭 LiveSubtitles 并检查这个目录：%1

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Keep these first so the preparation step can extract them without unpacking the app.
Source: "{#PayloadDir}\Install.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\resources\*"; DestDir: "{app}\resources"; Flags: ignoreversion
Source: "{#PayloadDir}\app\*"; DestDir: "{app}\app"; Flags: ignoreversion recursesubdirs
Source: "{#PayloadDir}\scripts\Diagnose.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "{#PayloadDir}\scripts\Remove-AppData.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
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
#include "ModelPreparation.iss"

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

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Purge: Boolean;
  I, ExitCode: Integer;
  Params: String;
begin
  if CurUninstallStep <> usUninstall then Exit;
  Purge := False;
  if UninstallSilent then begin
    for I := 1 to ParamCount do
      if CompareText(ParamStr(I), '/PURGEAPPDATA') = 0 then Purge := True;
  end else
    Purge := MsgBox(CustomMessage('PurgeData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
  if Purge then begin
    Params := '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
      ExpandConstant('{app}\scripts\Remove-AppData.ps1') + '"';
    if not ExecAndLogOutput(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      Params, ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ExitCode, nil) or (ExitCode <> 0) then begin
      Log('Local data cleanup was incomplete.');
      SuppressibleMsgBox(FmtMessage(CustomMessage('PurgeError'), [ExpandConstant('{app}')]), mbError, MB_OK, IDOK);
    end;
  end;
end;
