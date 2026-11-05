{ The installer is x64. Start the worker suspended so its entire process tree belongs
  to our job before it can start curl or Ollama. Closing this job stops only that tree. }
type
  TWorkerStartup = record
    Size, Padding: Cardinal;
    Reserved, Desktop, Title: NativeUInt;
    X, Y, Width, Height, XChars, YChars, Fill, Flags: Cardinal;
    ShowWindow, ReservedSize: Word;
    ReservedPadding: Cardinal;
    ReservedData, StdInput, StdOutput, StdError: NativeUInt;
  end;
  TWorkerProcess = record
    ProcessHandle, ThreadHandle: THandle;
    ProcessId, ThreadId: Cardinal;
  end;
  TJobLimits = record
    ProcessTime, JobTime: Int64;
    Flags: Cardinal;
    Remaining: array[0..123] of Byte;
  end;

function CreateJob(Attributes, Name: NativeUInt): THandle;
  external 'CreateJobObjectW@kernel32.dll stdcall';
function SetJobLimits(Job: THandle; InfoClass: Cardinal; var Limits: TJobLimits; Size: Cardinal): Boolean;
  external 'SetInformationJobObject@kernel32.dll stdcall';
function AttachToJob(Job, ProcessHandle: THandle): Boolean;
  external 'AssignProcessToJobObject@kernel32.dll stdcall';
function CreateWorker(ApplicationName, CommandLine: String; ProcessSecurity, ThreadSecurity: NativeUInt;
  Inherit: Boolean; Flags: Cardinal; Environment: NativeUInt; Directory: String;
  var Startup: TWorkerStartup; var Info: TWorkerProcess): Boolean;
  external 'CreateProcessW@kernel32.dll stdcall';
function ResumeWorker(ThreadHandle: THandle): Cardinal;
  external 'ResumeThread@kernel32.dll stdcall';
function StopWorker(ProcessHandle: THandle; ExitCode: Cardinal): Boolean;
  external 'TerminateProcess@kernel32.dll stdcall';
function WaitWorker(ProcessHandle: THandle; Timeout: Cardinal): Cardinal;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function WorkerExitCode(ProcessHandle: THandle; var Code: Cardinal): Boolean;
  external 'GetExitCodeProcess@kernel32.dll stdcall';
function CloseNative(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';
function SystemMenu(Window: HWND; Revert: Boolean): THandle;
  external 'GetSystemMenu@user32.dll stdcall';
function EnableMenuCommand(Menu: THandle; Item, Flags: Cardinal): Cardinal;
  external 'EnableMenuItem@user32.dll stdcall';
function GetTickCount: Cardinal;
  external 'GetTickCount@kernel32.dll stdcall';

var
  ModelPage: TOutputProgressWizardPage;
  Preparing, CancelRequested: Boolean;
  WorkerJob: THandle;

procedure CloseWorkerJob;
begin
  if WorkerJob <> 0 then begin
    CloseNative(WorkerJob);
    WorkerJob := 0;
  end;
end;

function StartWorker(const Directory, StatusFile: String): THandle;
var
  Startup: TWorkerStartup;
  Info: TWorkerProcess;
  Limits: TJobLimits;
  Exe, Cmd: String;
begin
  Result := 0;
  WorkerJob := CreateJob(0, 0);
  Limits.Flags := $2000; { JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE }
  if (WorkerJob = 0) or not SetJobLimits(WorkerJob, 9, Limits, SizeOf(Limits)) then
    RaiseException(SysErrorMessage(DLLGetLastError));
  Exe := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  Cmd := '"' + Exe + '" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
    Directory + '\Install.ps1" -ApplicationDirectory "' + RemoveBackslashUnlessRoot(WizardDirValue) +
    '" -ProgressFile "' + StatusFile + '"';
  Startup.Size := SizeOf(Startup);
  if not CreateWorker(Exe, Cmd, 0, 0, False, $08000004, 0, Directory, Startup, Info) then
    RaiseException(SysErrorMessage(DLLGetLastError));
  try
    if not AttachToJob(WorkerJob, Info.ProcessHandle) or (ResumeWorker(Info.ThreadHandle) = $FFFFFFFF) then begin
      StopWorker(Info.ProcessHandle, 1);
      CloseNative(Info.ProcessHandle);
      RaiseException('Could not start the model worker in its private job.');
    end;
    Result := Info.ProcessHandle;
  finally
    CloseNative(Info.ThreadHandle);
  end;
end;

procedure InitializeWizard;
begin
  WizardForm.BorderStyle := bsSizeable;
  WizardForm.BorderIcons := [biSystemMenu, biMinimize, biMaximize];
  ModelPage := CreateOutputProgressPage(CustomMessage('ModelTitle'), CustomMessage('ModelDescription'));
end;

procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  if Preparing then begin
    Cancel := False;
    Confirm := False;
    if not CancelRequested then
      CancelRequested := SuppressibleMsgBox(CustomMessage('CancelModels'), mbConfirmation,
        MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES;
    if CancelRequested then begin
      CloseWorkerJob;
      Cancel := True; { let Setup set its normal cancelled-before-install exit code }
    end;
  end else if CancelRequested then
    Confirm := False;
end;

procedure ShowModelStatus(const StatusFile: String; var LastStep: String; var LastBytes: Int64;
  var LastChange: Cardinal);
var
  Stage, Item, Name, Step, Detail: String;
  Size, Bytes: Int64;
  Percent: Integer;
begin
  Stage := GetIniString('model', 'stage', '', StatusFile);
  Item := GetIniString('model', 'item', '', StatusFile);
  if Stage = 'download' then begin
    Name := ChangeFileExt(ExtractFileName(Item), ''); { remove .partial }
    Step := FmtMessage(CustomMessage('DownloadStep'), [Name]);
    Size := StrToInt64Def(GetIniString('model', 'size', '0', StatusFile), 0);
    if not FileSize64(Item, Bytes) then Bytes := 0;
    if (Bytes <> LastBytes) or (Step <> LastStep) then LastChange := GetTickCount;
    LastBytes := Bytes;
    Percent := 0;
    if Size > 0 then Percent := (Bytes * 100) div Size;
    if Percent > 100 then Percent := 100;
    Detail := Format('%d / %d MiB  (%d%%)', [Bytes div 1048576, (Size + 1048575) div 1048576, Percent]);
    if (GetTickCount - LastChange) >= 15000 then Detail := Detail + '   ' + CustomMessage('WaitingTransfer');
    ModelPage.SetText(Step, Detail);
    ModelPage.SetProgress(Percent, 100);
  end else begin
    if Stage = 'verify' then Step := FmtMessage(CustomMessage('VerifyStep'), [Item])
    else if Stage = 'extract' then Step := FmtMessage(CustomMessage('ExtractStep'), [Item])
    else if Stage = 'import' then Step := FmtMessage(CustomMessage('ImportStep'), [Item])
    else if Stage = 'start' then Step := CustomMessage('StartService')
    else if Stage = 'save' then Step := CustomMessage('SaveSettings')
    else Step := CustomMessage('ModelProgress');
    ModelPage.SetText(Step, '');
    ModelPage.SetProgress(0, 0);
  end;
  if Step <> LastStep then Log(Step);
  LastStep := Step;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ScriptDir, StatusFile, LastStep, Reason: String;
  Worker: THandle;
  ExitCode, WaitResult, LastChange: Cardinal;
  LastBytes: Int64;
begin
  Result := '';
  Worker := 0;
  CancelRequested := False;
  StatusFile := ExpandConstant('{tmp}\model-progress.ini');
  DeleteFile(StatusFile);
  ModelPage.Show;
  Preparing := True;
  WizardForm.CancelButton.Visible := True;
  WizardForm.CancelButton.Enabled := True;
  EnableMenuCommand(SystemMenu(WizardForm.Handle, False), $F060, 0); { SC_CLOSE }
  try
    try
      ExtractTemporaryFiles('{app}\Install.ps1');
      ExtractTemporaryFiles('{app}\resources\*');
      ScriptDir := ExpandConstant('{tmp}\') + '{app}';
      Worker := StartWorker(ScriptDir, StatusFile);
      repeat
        ShowModelStatus(StatusFile, LastStep, LastBytes, LastChange);
        if CancelRequested then CloseWorkerJob;
        WaitResult := WaitWorker(Worker, 150);
      until WaitResult <> 258; { WAIT_TIMEOUT }
      if CancelRequested then Log(CustomMessage('CancelledModels'))
      else if (WaitResult <> 0) or not WorkerExitCode(Worker, ExitCode) or (ExitCode <> 0) then begin
        Reason := GetIniString('model', 'item', '', StatusFile);
        if GetIniString('model', 'stage', '', StatusFile) <> 'error' then Reason := '';
        Result := FmtMessage(CustomMessage('ModelFailed'), [ExpandConstant('{log}')]) + #13#10 + Reason;
      end;
    except
      Result := GetExceptionMessage;
    end;
  finally
    CloseWorkerJob;
    if Worker <> 0 then CloseNative(Worker);
    Preparing := False;
    ModelPage.Hide;
  end;
  if CancelRequested then Abort; { unwind the current Next operation before it can install files }
end;
