; CaptureOK 설치 파일 (Inno Setup 6). build.ps1이 게시 폴더와 버전을 넘겨 준다.
; 관리자 권한 없이 지금 사용자에게만 설치한다: %LOCALAPPDATA%\Programs\CaptureOk

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "publish"
#endif

#define AppName "CaptureOK"
#define AppExe "CaptureOk.exe"
#define AppMutex "Local\CaptureOk.SingleInstance"

[Setup]
AppId={{B55E2A07-A8E9-4957-A765-3516D50D82B3}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=LeeHanryang
AppPublisherURL=https://github.com/LeeHanryang
AppCopyright=© 2026 LeeHanryang
VersionInfoVersion={#AppVersion}
PrivilegesRequired=lowest
DefaultDirName={userpf}\CaptureOk
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=output
OutputBaseFilename=CaptureOK-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\CaptureOk\Assets\CaptureOk.ico
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
; 실행 중인 앱은 아래 [Code]에서 직접 끈다 (재시작 관리자 대화 상자를 따로 띄우지 않게)
CloseApplications=no
RestartApplications=no

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "autostart"; Description: "Windows 시작 시 자동 실행"
Name: "desktopicon"; Description: "바탕 화면에 바로 가기 만들기"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; 앱의 환경설정 "Windows 시작 시 실행"과 같은 값이다 (앱은 이 값을 실제 상태로 본다)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CaptureOk"; ValueData: """{app}\{#AppExe}"""; Tasks: autostart

[UninstallDelete]
; 설치 프로그램이 만들지 않은(전에 직접 복사해 둔) 폴더도 비었으면 지운다
Type: dirifempty; Name: "{app}"

[Run]
Filename: "{app}\{#AppExe}"; Description: "CaptureOK 실행"; Flags: nowait postinstall skipifsilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

function IsAppRunning(): Boolean;
begin
  Result := CheckForMutexes('{#AppMutex}');
end;

function WaitForExit(Milliseconds: Integer): Boolean;
var
  Waited: Integer;
begin
  Waited := 0;
  while IsAppRunning() and (Waited < Milliseconds) do
  begin
    Sleep(200);
    Waited := Waited + 200;
  end;
  Result := not IsAppRunning();
end;

{ 실행 중인 앱에 종료를 요청하고(고정 캡처의 마지막 편집을 저장하고 끝남), 응답이 없으면 강제로 끈다. }
function CloseRunningApp(): Boolean;
var
  Code: Integer;
  Exe: String;
begin
  Exe := ExpandConstant('{app}\{#AppExe}');
  if FileExists(Exe) then
    Exec(Exe, '--exit', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := WaitForExit(5000);
  if not Result then
  begin
    { --exit를 모르는 이전 버전 }
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, Code);
    Result := WaitForExit(5000);
  end;
end;

{ 설치 중에는 UninstallSilent를, 제거 중에는 WizardSilent를 부를 수 없다 }
function IsSilent(): Boolean;
begin
  if IsUninstaller() then
    Result := UninstallSilent()
  else
    Result := WizardSilent();
end;

function AskToClose(): Boolean;
begin
  Result := True;
  if not IsAppRunning() then Exit;
  if not IsSilent() then
    Result := MsgBox('CaptureOK가 실행 중입니다. 종료하고 계속할까요?' + #13#10 + #13#10 +
      '저장하거나 복사하지 않은 캡처는 사라집니다. 고정한 캡처는 남습니다.',
      mbConfirmation, MB_YESNO) = IDYES;
  if Result then
    Result := CloseRunningApp();
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not AskToClose() then
    Result := 'CaptureOK를 끄지 못했습니다. 트레이 아이콘에서 종료한 뒤 다시 설치해 주세요.';
end;

function InitializeUninstall(): Boolean;
begin
  Result := AskToClose();
  if not Result and not UninstallSilent() and IsAppRunning() then
    MsgBox('CaptureOK를 종료한 뒤 다시 제거해 주세요.', mbInformation, MB_OK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    { 설치할 때 고르지 않았더라도 앱 환경설정에서 켰을 수 있다 }
    RegDeleteValue(HKEY_CURRENT_USER, RunKey, 'CaptureOk');
    { 앱이 저장 알림(Windows 알림)을 띄우려고 등록한 앱 이름·아이콘 }
    RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\AppUserModelId\LeeHanryang.CaptureOK');
  end;
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent() then
    if MsgBox('환경설정과 고정한 캡처도 지울까요?' + #13#10 + #13#10 +
      '저장 폴더에 저장한 그림은 지우지 않습니다.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      DelTree(ExpandConstant('{userappdata}\CaptureOk'), True, True, True);
      DelTree(ExpandConstant('{localappdata}\CaptureOk'), True, True, True);
    end;
end;
