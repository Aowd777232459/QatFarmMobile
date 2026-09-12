#define MyAppName "نظام زراعي عواد سوفت"
#define MyAppVersion "2.3.0"
#define MyAppPublisher "AWAD SOFT"
#define MyAppExeName "QatFarm.Mobile.exe"

[Setup]
AppId={{7F6B985D-4B40-4FC7-A3E8-2B14D3D46A91}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\AWAD SOFT\QatFarm
DefaultGroupName=AWAD SOFT
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\..\artifacts\installer
OutputBaseFilename=AWAD-SOFT-QatFarm-Windows-PREMIUM-2.3.0-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupLogging=yes
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\..\artifacts\windows-publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{userdocs}\عواد سوفت"
Name: "{userdocs}\عواد سوفت\البيانات"
Name: "{userdocs}\عواد سوفت\النسخ الاحتياطية"
Name: "{userdocs}\عواد سوفت\فواتير البيع"
Name: "{userdocs}\عواد سوفت\التقارير"
Name: "{userdocs}\عواد سوفت\العملاء"
Name: "{userdocs}\عواد سوفت\المزامنة"

[Icons]
Name: "{autoprograms}\AWAD SOFT\نظام زراعي عواد سوفت"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\نظام زراعي عواد سوفت"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "إنشاء اختصار على سطح المكتب"; GroupDescription: "اختصارات:"; Flags: checkedonce

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "تشغيل نظام زراعي عواد سوفت"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\Programs\AWAD SOFT\QatFarm"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  InfoFile: string;
begin
  if CurStep = ssPostInstall then
  begin
    InfoFile := ExpandConstant('{userdocs}\عواد سوفت\المزامنة\تعليمات.txt');
    SaveStringToFile(InfoFile,
      'نظام زراعي عواد سوفت - AWAD SOFT' + #13#10 +
      'يتم حفظ قاعدة البيانات والنسخ الاحتياطية والفواتير والتقارير داخل مجلد عواد سوفت.' + #13#10 +
      'للمزامنة افتح الإعدادات في نسخة الكمبيوتر وانسخ عنوان الكمبيوتر ورمز الربط إلى تطبيق الجوال.' + #13#10 +
      'إذا ظهر تنبيه جدار الحماية عند أول تشغيل، اسمح للتطبيق على الشبكات الخاصة فقط.' + #13#10,
      False);
  end;
end;
