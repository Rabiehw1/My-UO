$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot/../..").Path
$stage = Join-Path ([IO.Path]::GetTempPath()) ("My-UO-" + [Guid]::NewGuid())
$payload = Join-Path $stage 'client'
$output = Join-Path $root 'build/installer'
$compiler = @((Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source,
    "${env:ProgramFiles(x86)}/Inno Setup 6/ISCC.exe",
    "$env:LOCALAPPDATA/Programs/Inno Setup 6/ISCC.exe") |
    Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
if (!$compiler) { throw 'Install Inno Setup 6 before building the installer.' }
Push-Location $root
try {
    git -c url.https://.insteadOf=git:// submodule update --init --recursive
    if ($LASTEXITCODE) { throw 'Could not retrieve the source dependencies.' }
    dotnet publish src/ClassicUO.Client/ClassicUO.Client.csproj -c Release -r win-x64 --self-contained true -o $payload
    if ($LASTEXITCODE) { throw 'Client compilation failed.' }
    if (Test-Path "$payload-Lib") { Copy-Item "$payload-Lib/*" $payload -Recurse -Force }
    if (!(Test-Path "$payload/TazUO.exe")) { throw 'Missing published TazUO.exe.' }
    New-Item -ItemType Directory -Force $output | Out-Null
    & $compiler "/DPayload=$payload" "/O$output" "$PSScriptRoot/My-UO.iss"
    if ($LASTEXITCODE) { throw 'Installer compilation failed.' }
    Write-Host "Installer: $output/My-UO-Setup-x64.exe"
} finally {
    Pop-Location
    if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}
