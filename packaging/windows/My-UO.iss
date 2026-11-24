[Setup]
AppId=Rabiehw1.My-UO
AppName=My-UO
AppVersion=1.0
DefaultDirName={localappdata}\Programs\My-UO
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
OutputBaseFilename=My-UO-Setup-x64
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\TazUO.exe

[Files]
Source: "{#Payload}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\My-UO"; Filename: "{app}\TazUO.exe"; WorkingDir: "{app}"; Parameters: "-ip zuluhotelomega.dynu.net -port 2593"
