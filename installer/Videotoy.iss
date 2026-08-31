#define MyAppName "Videotoy"
#define MyAppPublisher "Patrick JAILLET"
#define MyAppURL "https://patrickjaillet.github.io/Videotoy"
#define MyAppExeName "Videotoy.exe"
#define MyAppExePath "..\src\Videotoy.App\bin\x64\Release\net8.0-windows10.0.19041.0\" + MyAppExeName
#define MyAppIcon "..\src\Videotoy.App\Assets\Icons\app.ico"
#define MyInstallerIcon "installer.ico"

#if !FileExists(MyAppExePath)
  #error "Videotoy.exe is missing from the build output. Run the Release build (dotnet build Videotoy.sln -c Release) before packaging the installer."
#endif

#if !FileExists(MyAppIcon)
  #error "Application icon is missing (src\Videotoy.App\Assets\Icons\app.ico)."
#endif

#if !FileExists(MyInstallerIcon)
  #error "Installer icon is missing (installer\installer.ico)."
#endif

; Read straight from the compiled executable's version resource
; (set by Directory.Build.props / FileVersion), so the installer's
; version always matches the build it packages with no manual edit.
;
; GetVersionComponents parses the resource into four integer fields
; (Major.Minor.Revision.Build) and returns "" if the resource could not
; be read at all (missing/corrupt version info) — checked explicitly
; below rather than trusting a possibly-empty MyAppVersionCheck string.
#define MyAppVersionMajor
#define MyAppVersionMinor
#define MyAppVersionRevision
#define MyAppVersionBuild
#define MyAppVersionCheck GetVersionComponents(MyAppExePath, MyAppVersionMajor, MyAppVersionMinor, MyAppVersionRevision, MyAppVersionBuild)

#if MyAppVersionCheck == ""
  #error "Could not read Videotoy.exe's version resource. Ensure the Release build ran with FileVersion set in Directory.Build.props before packaging the installer."
#endif

#if (MyAppVersionMajor == 0) && (MyAppVersionMinor == 0) && (MyAppVersionRevision == 0)
  #error "Videotoy.exe's version resource is unstamped (0.0.0.x). Set VersionPrefix/FileVersion in Directory.Build.props to a real SemVer before packaging the installer."
#endif

; Strict three-part SemVer (X.Y.Z) for display and for OutputBaseFilename —
; the resource's fourth (Build) component is a Windows file-version
; artifact, not part of this project's SemVer, and is intentionally
; dropped here.
#define MyAppVersion Str(MyAppVersionMajor) + "." + Str(MyAppVersionMinor) + "." + Str(MyAppVersionRevision)

[Setup]
AppId={{9F2E7C10-4A1B-4B7A-9D5A-000000000001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=Output
OutputBaseFilename=Videotoy-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
SetupIconFile={#MyInstallerIcon}
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
ShowLanguageDialog=auto
MinVersion=10.0.22000

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\src\Videotoy.App\bin\x64\Release\net8.0-windows10.0.19041.0\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs

#if !FileExists("..\src\Videotoy.App\bin\x64\Release\net8.0-windows10.0.19041.0\tools\ffmpeg\ffmpeg.exe")
  #error "ffmpeg.exe is missing from the build output. Run the Release build with tools\ffmpeg\ffmpeg.exe present before packaging the installer."
#endif

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"; IconFilename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
// AppVerName ("{#MyAppName} {#MyAppVersion}") already drives the wizard's
// window title and the "Ready to Install" summary page by default. This
// appends the same verified SemVer, explicitly labeled, to the Welcome
// page body text so the version is unambiguous (not just part of a
// window title the user may not read) before any file is installed.
procedure InitializeWizard();
begin
  WizardForm.WelcomeLabel2.Caption :=
    WizardForm.WelcomeLabel2.Caption + #13#10 + #13#10 +
    'Version : {#MyAppVersion}';
end;
