; Instalador de Lanzador (Inno Setup 7).
; Compilado por build\distribuir.ps1.
; Por usuario y sin permisos de administrador: {userpf}\Lanzador (%LOCALAPPDATA%\Programs\Lanzador).

#ifndef Version
  #error Falta /DVersion
#endif
#ifndef Origen
  #error Falta /DOrigen
#endif
#ifndef Licencia
  #error Falta /DLicencia
#endif

[Setup]
AppId={{2E96FE84-2A94-43AC-A1A6-B618776E0A63}
AppName=Lanzador
AppVersion={#Version}
AppVerName=Lanzador {#Version}
AppPublisher=Dathinel
AppPublisherURL=https://github.com/Dathinel/lanzador
AppSupportURL=https://github.com/Dathinel/lanzador/issues
AppUpdatesURL=https://github.com/Dathinel/lanzador/releases
AppCopyright=Copyright (C) 2026 Dathinel
VersionInfoVersion={#Version}.0
VersionInfoProductName=Lanzador
VersionInfoDescription=Instalador de Lanzador
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=
DefaultDirName={userpf}\Lanzador
DisableProgramGroupPage=yes
DisableDirPage=auto
UsePreviousAppDir=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
LicenseFile={#Licencia}
CloseApplications=yes
RestartApplications=no
UninstallDisplayName=Lanzador
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
OutputBaseFilename=Lanzador-{#Version}-instalador

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "escritorio"; Description: "Crear un acceso directo en el escritorio"; Flags: unchecked

[Files]
Source: "{#Origen}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Lanzador"; Filename: "{app}\Lanzador.exe"; Comment: "Lanzador de herramientas de IA de Dathinel"
Name: "{autodesktop}\Lanzador"; Filename: "{app}\Lanzador.exe"; Comment: "Lanzador de herramientas de IA de Dathinel"; Tasks: escritorio

[Run]
Filename: "{app}\Lanzador.exe"; Description: "Abrir Lanzador"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
