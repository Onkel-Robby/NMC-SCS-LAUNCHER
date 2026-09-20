#define MyAppName "NMC SCS LAUNCHER"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "NMC Network / Onkel_Robby"
#define MyAppExeName "NmcScsLauncher.App.exe"

[Setup]
AppId={{7F42D224-0D68-4E97-B034-65E84D9AD45B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
DefaultDirName={localappdata}\Programs\NMC SCS LAUNCHER
DefaultGroupName=NMC SCS LAUNCHER
DisableProgramGroupPage=yes
AllowNoIcons=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
MinVersion=10.0
SourceDir=..
OutputDir=artifacts\installer
OutputBaseFilename=NMC-SCS-LAUNCHER-{#MyAppVersion}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupLogging=yes
SetupIconFile=branding\nmc-scs-launcher.ico
WizardSmallImageFile=branding\nmc-scs-launcher-wizard-appicon.bmp

[Tasks]
Name: "desktopicon"; Description: "Desktop-Verknüpfung erstellen"; GroupDescription: "Zusätzliche Verknüpfungen:"; Flags: unchecked

[Files]
Source: "artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "branding\nmc-it-service-installer-footer.bmp"; Flags: dontcopy

[Icons]
Name: "{group}\NMC SCS LAUNCHER"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\NMC SCS LAUNCHER"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "NMC SCS LAUNCHER starten"; Flags: nowait postinstall skipifsilent


[Code]
var
  NmcBrandingImage: TBitmapImage;

procedure InitializeWizard;
begin
  ExtractTemporaryFile('nmc-it-service-installer-footer.bmp');

  NmcBrandingImage := TBitmapImage.Create(WizardForm);
  NmcBrandingImage.Parent := WizardForm;
  NmcBrandingImage.Bitmap.LoadFromFile(ExpandConstant('{tmp}\nmc-it-service-installer-footer.bmp'));
  NmcBrandingImage.Stretch := True;
  NmcBrandingImage.Width := ScaleX(170);
  NmcBrandingImage.Height := ScaleY(57);
  NmcBrandingImage.Left := ScaleX(18);
  NmcBrandingImage.Top := WizardForm.ClientHeight - NmcBrandingImage.Height - ScaleY(7);
end;
