; Pulpit 安装版脚本(Inno Setup 6)。由 tools\package.ps1 传入版本号编译:
;   ISCC.exe /DMyAppVersion=1.0.0 installer\pulpit.iss
;
; 关键取舍:PrivilegesRequired=lowest —— **按用户安装,不需要管理员**。
; 教会机器上日常操作账号(如 Chapel)通常不是管理员,双击安装即可完成,
; 装到 {autopf}(非管理员时解析为 %LOCALAPPDATA%\Programs)。
; 管理员想装给所有用户,可在安装时选择(OverridesAllowed=dialog)。

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#define MyAppName "Pulpit"
#define MyAppExeName "Pulpit.exe"

[Setup]
; AppId 固定不变:升级安装靠它识别旧版本,改了它就变成并存的第二份
AppId={{B5F9DB45-B39D-4DB5-BF52-DC71E2D98533}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\publish
OutputBaseFilename=Pulpit-Setup-{#MyAppVersion}
SetupIconFile=..\src\Pulpit.App\Assets\pulpit.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式 (Create a desktop shortcut)"; GroupDescription: "附加任务:"

[Files]
Source: "..\publish\win-x64\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\快速上手卡.md"; DestDir: "{app}\docs"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "安装完成后启动 Pulpit (Launch Pulpit)"; Flags: nowait postinstall skipifsilent

; 卸载时不动 %LOCALAPPDATA%\Pulpit(配置/经文库/日志):
; 重装/升级后设置原样保留;确要清干净就手动删那个文件夹。
