#define MyAppName "نظام زراعي عواد سوفت"
#define MyAppVersion "2.2.3"
#define MyAppPublisher "AWAD SOFT"
#define MyAppExeName "QatFarm.Web.exe"

[Setup]
AppId={{A2E93651-9D36-4EC4-B31E-D2E0A8E541A6}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\QatFarmSystem
DefaultGroupName={#MyAppName}
OutputDir=..\Release\installer
OutputBaseFilename=QatFarmSystem_Setup_2.2.3_x64
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
SetupLogging=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\QatFarm.Web\wwwroot\qatfarm.ico
CloseApplications=yes
RestartApplications=no
DisableProgramGroupPage=yes
UsePreviousAppDir=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\Release\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Configure-Database.ps1"; DestDir: "{app}\Tools"; Flags: ignoreversion
Source: "Configure-Database.cmd"; DestDir: "{app}\Tools"; Flags: ignoreversion
Source: "Install-SqlExpress.ps1"; DestDir: "{app}\Tools"; Flags: ignoreversion
Source: "Install-SqlExpress.cmd"; DestDir: "{app}\Tools"; Flags: ignoreversion

[Dirs]
Name: "{commonappdata}\QatFarmSystem"; Permissions: users-modify
Name: "{commonappdata}\QatFarmSystem\Logs"; Permissions: users-modify
Name: "{commonappdata}\QatFarmSystem\Backups"; Permissions: users-modify

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autoprograms}\إعداد قاعدة بيانات نظام المزارع"; Filename: "{app}\Tools\Configure-Database.cmd"; WorkingDir: "{app}\Tools"
Name: "{autoprograms}\فحص أو تثبيت SQL Server Express"; Filename: "{app}\Tools\Install-SqlExpress.cmd"; WorkingDir: "{app}\Tools"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "إنشاء اختصار على سطح المكتب"; GroupDescription: "اختصارات إضافية:"; Flags: checkedonce
Name: "autostart"; Description: "تشغيل النظام عند تسجيل الدخول إلى Windows"; GroupDescription: "خيارات التشغيل:"; Flags: unchecked

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "QatFarmSystem"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{cmd}"; Parameters: "/C icacls ""{commonappdata}\QatFarmSystem"" /grant *S-1-5-32-545:(OI)(CI)M /T /C"; Flags: runhidden waituntilterminated
Filename: "{cmd}"; Parameters: "/C netsh advfirewall firewall delete rule name=""AWAD SOFT QatFarm WiFi Sync"" >nul 2>&1 & netsh advfirewall firewall add rule name=""AWAD SOFT QatFarm WiFi Sync"" dir=in action=allow program=""{app}\{#MyAppExeName}"" protocol=TCP localport=5276 profile=any remoteip=localsubnet"; Flags: runhidden waituntilterminated
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoLogo -NoProfile -ExecutionPolicy Bypass -File ""{app}\Tools\Install-SqlExpress.ps1"" -Silent"; StatusMsg: "تجهيز SQL Server Express وقاعدة البيانات..."; Flags: waituntilterminated; Check: ShouldInstallSqlExpress
Filename: "{cmd}"; Parameters: "/C icacls ""{commonappdata}\QatFarmSystem\Backups"" /grant ""{code:GetSqlServiceAccount}"":(OI)(CI)M /T /C"; Flags: runhidden waituntilterminated; Check: IsLocalSqlInstance
Filename: "{app}\{#MyAppExeName}"; Description: "تشغيل {#MyAppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C netsh advfirewall firewall delete rule name=""AWAD SOFT QatFarm WiFi Sync"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveQatFarmSyncFirewall"

[Code]
var
  SqlPage: TInputQueryWizardPage;

function EscapeJson(Value: String): String;
begin
  StringChangeEx(Value, '\', '\\', True);
  StringChangeEx(Value, '"', '\"', True);
  Result := Value;
end;

function GetSqlInstance(Param: String): String;
begin
  if Assigned(SqlPage) and (Trim(SqlPage.Values[0]) <> '') then
    Result := Trim(SqlPage.Values[0])
  else
    Result := '.\SQLEXPRESS';
end;

function IsLocalSqlInstance: Boolean;
var
  Value: String;
begin
  Value := LowerCase(GetSqlInstance(''));
  Result := (Pos('.\', Value) = 1) or
            (Pos('localhost\', Value) = 1) or
            (Value = '.') or
            (Value = 'localhost');
end;

function ServiceExists(ServiceName: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(
    ExpandConstant('{sys}\sc.exe'),
    'query "' + ServiceName + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function CommandLineParamExists(Value: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
  begin
    if CompareText(ParamStr(I), Value) = 0 then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function ShouldInstallSqlExpress: Boolean;
begin
  Result := (not CommandLineParamExists('/NOSQL')) and
            IsLocalSqlInstance and
            (not ServiceExists('MSSQL$SQLEXPRESS')) and
            (not ServiceExists('MSSQLSERVER'));
end;

function GetSqlServiceAccount(Param: String): String;
var
  Value, InstanceName: String;
  SlashPosition: Integer;
begin
  Value := GetSqlInstance('');
  SlashPosition := Pos('\', Value);
  if SlashPosition > 0 then
  begin
    InstanceName := Copy(Value, SlashPosition + 1, Length(Value));
    Result := 'NT SERVICE\MSSQL$' + InstanceName;
  end
  else
    Result := 'NT SERVICE\MSSQLSERVER';
end;

procedure InitializeWizard;
begin
  SqlPage := CreateInputQueryPage(
    wpSelectDir,
    'إعداد قاعدة البيانات',
    'حدد اسم خادم SQL Server',
    'اترك القيمة الافتراضية إذا كان SQL Server Express مثبتًا على الجهاز. مثال: .\SQLEXPRESS');
  SqlPage.Add('اسم الخادم أو النسخة:', False);
  SqlPage.Values[0] := '.\SQLEXPRESS';
end;

procedure WriteLocalConfiguration;
var
  ConfigDir, ConfigPath, ConfigBackupPath, JsonText, ServerName: String;
begin
  ConfigDir := ExpandConstant('{commonappdata}\QatFarmSystem');
  ConfigPath := ConfigDir + '\appsettings.Local.json';
  ForceDirectories(ConfigDir);
  if FileExists(ConfigPath) then
  begin
    ConfigBackupPath := ConfigPath + '.pre-2.2.3.bak';
    if not FileCopy(ConfigPath, ConfigBackupPath, False) then
    begin
      MsgBox('تعذر إنشاء نسخة احتياطية من إعدادات الإصدار السابق: ' + ConfigBackupPath, mbError, MB_OK);
      Exit;
    end;
  end;
  ServerName := EscapeJson(GetSqlInstance(''));
  JsonText :=
    '{' + #13#10 +
    '  "ConnectionStrings": {' + #13#10 +
    '    "DefaultConnection": "Server=' + ServerName + ';Database=QatFarmDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;"' + #13#10 +
    '  },' + #13#10 +
    '  "DesktopMode": {' + #13#10 +
    '    "Enabled": true,' + #13#10 +
    '    "Url": "http://127.0.0.1:5275",' + #13#10 +
    '    "AutoOpenBrowser": true' + #13#10 +
    '  },' + #13#10 +
    '  "LocalSync": {' + #13#10 +
    '    "Enabled": true,' + #13#10 +
    '    "Url": "http://0.0.0.0:5276"' + #13#10 +
    '  },' + #13#10 +
    '  "DatabaseBackup": {' + #13#10 +
    '    "Directory": "%ProgramData%\\QatFarmSystem\\Backups",' + #13#10 +
    '    "KeepLatest": 30' + #13#10 +
    '  }' + #13#10 +
    '}' + #13#10;

  if not SaveStringToFile(ConfigPath, JsonText, False) then
    MsgBox('تعذر كتابة إعداد قاعدة البيانات في: ' + ConfigPath, mbError, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    WriteLocalConfiguration;
end;

function InitializeSetup(): Boolean;
begin
  Result := IsWin64;
  if not Result then
    MsgBox('هذا الإصدار يحتاج Windows 64-bit.', mbError, MB_OK);
end;
