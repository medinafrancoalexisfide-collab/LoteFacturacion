; Script de Inno Setup para el programa de facturación por lote
#define MiApp "Facturación por lote"
#define MiVersion "0.3.0"
#define MiExe "FacturacionLote.exe"

; La carpeta con el programa publicado la indica instalador.ps1 (/DPublishDir=...).
; Si compilás este archivo a mano desde Inno Setup, se usa esta ruta por defecto:
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
; AppId identifica el programa para actualizarlo o desinstalarlo. Dejá este valor fijo en todas las versiones.
AppId={{C043E9C0-212B-47A3-A807-0A1DA406CA17}
AppName={#MiApp}
AppVersion={#MiVersion}
DefaultDirName={autopf}\FacturacionLote
DefaultGroupName={#MiApp}
DisableProgramGroupPage=yes
; "lowest" = se instala solo para el usuario actual y NO pide permisos de administrador
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=salida
OutputBaseFilename=Instalar-FacturacionPorLote-{#MiVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MiExe}

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"; Flags: unchecked

[Files]
; Copia todo lo que dejó "dotnet publish"
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MiApp}"; Filename: "{app}\{#MiExe}"
Name: "{autodesktop}\{#MiApp}"; Filename: "{app}\{#MiExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MiExe}"; Description: "Abrir {#MiApp}"; Flags: nowait postinstall skipifsilent
