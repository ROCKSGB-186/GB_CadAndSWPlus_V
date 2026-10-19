#define MyAppName "GB CADPLUS SolidWorks 插件"
#define MyAppVersion "3.26.1013.188"
#define MyAppPublisher "GB CADPLUS"
#define MyAppExeName "GB_CadAndSWPlus_V_SolidWorksAddIn.dll"

[Setup]
AppId={{8A9C2B5D-61A6-4F0A-9F5A-GBCADPLUSSW01}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\GB_CADPLUS\SolidWorksAddIn
DefaultGroupName=GB CADPLUS
OutputDir=Output
OutputBaseFilename=GB_CADPLUS_SolidWorksAddIn_Setup_{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64
PrivilegesRequired=admin
UninstallDisplayName={#MyAppName}
WizardStyle=modern

[Files]
Source: "Payload\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Register-SolidWorksAddIn.ps1"; DestDir: "{app}\Deployment"; Flags: ignoreversion
Source: "Unregister-SolidWorksAddIn.ps1"; DestDir: "{app}\Deployment"; Flags: ignoreversion
Source: "Prerequisites\MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall skipifsourcedoesntexist

[Icons]
Name: "{group}\手动注册 SolidWorks 插件"; Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File \\"{app}\Deployment\Register-SolidWorksAddIn.ps1\\""; WorkingDir: "{app}"; Flags: runascurrentuser
Name: "{group}\卸载 SolidWorks 插件"; Filename: "{uninstallexe}"

[Run]
Filename: "{tmp}\MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; Parameters: "/silent /install"; StatusMsg: "正在安装 Microsoft WebView2 Runtime..."; Flags: waituntilterminated skipifsilent skipifdoesntexist
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File \"{app}\Deployment\Register-SolidWorksAddIn.ps1\""; Description: "注册 SolidWorks 插件"; StatusMsg: "正在注册 SolidWorks 插件..."; Flags: postinstall runascurrentuser runhidden waituntilterminated

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File \"{app}\Deployment\Unregister-SolidWorksAddIn.ps1\""; Flags: runhidden waituntilterminated

[Code]
function IsDotNet472OrLaterInstalled(): Boolean;
var
  Release: Cardinal;
begin
  Result := False;
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) then
	Result := Release >= 461808;
  if not Result then
	MsgBox('.NET Framework 4.7.2 或更高版本未安装，请先安装后再继续。', mbError, MB_OK);
end;

function IsWebView2Installed(): Boolean;
begin
  Result := DirExists(ExpandConstant('{commonpf32}\Microsoft\EdgeWebView\Application')) or
	DirExists(ExpandConstant('{commonpf}\Microsoft\EdgeWebView\Application'));
end;

function FindSolidWorksApiPath(): String;
var
  Roots: TArrayOfString;
  I: Integer;
  Candidate: String;
begin
  SetArrayLength(Roots, 4);
  Roots[0] := ExpandConstant('{autopf}\SOLIDWORKS Corp\SOLIDWORKS');
  Roots[1] := ExpandConstant('{autopf}\SolidWorks Corp\SOLIDWORKS');
  Roots[2] := ExpandConstant('{autopf}\SolidWorks Corp\SOLIDWORKS 2022');
  Roots[3] := ExpandConstant('{autopf}\SOLIDWorks Corp\SOLIDWORKS');
  Result := '';
  for I := 0 to GetArrayLength(Roots) - 1 do begin
	Candidate := AddBackslash(Roots[I]) + 'api\redist\SolidWorks.Interop.sldworks.dll';
	if FileExists(Candidate) then begin
	  Result := ExtractFileDir(Candidate);
	  exit;
	end;
  end;
end;

function InitializeSetup(): Boolean;
var
  ApiPath: String;
begin
  Result := IsDotNet472OrLaterInstalled();
  if not Result then exit;
  ApiPath := FindSolidWorksApiPath();
  if ApiPath = '' then begin
	MsgBox('未检测到 SolidWorks API。请先安装 64 位 SolidWorks，再运行此安装包。', mbError, MB_OK);
	Result := False;
	  exit;
  end;
  if not IsWebView2Installed() then begin
	if not FileExists(ExpandConstant('{src}\Prerequisites\MicrosoftEdgeWebView2RuntimeInstallerX64.exe')) then begin
	  MsgBox('未检测到 Microsoft WebView2 Runtime，且安装包未包含离线安装程序。请先安装 WebView2 Runtime，或将安装程序放入 Prerequisites 目录后重新生成安装包。', mbError, MB_OK);
	  Result := False;
	end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ApiPath: String;
  Marker: String;
begin
  if CurStep = ssInstall then begin
	ApiPath := FindSolidWorksApiPath();
	Marker := ExpandConstant('{app}\SolidWorksApiPath.txt');
	SaveStringToFile(Marker, ApiPath + #13#10, False);
  end;
end;
