$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$toolsDir = Join-Path $root '.tools'
New-Item -ItemType Directory -Force $toolsDir | Out-Null
Set-Content -LiteralPath "$toolsDir/.gdignore" -Value ''
foreach ($folder in @('artifacts', 'dist')) {
    New-Item -ItemType Directory -Force "$root/$folder" | Out-Null
    Set-Content -LiteralPath "$root/$folder/.gdignore" -Value ''
}
if (!(Test-Path "$toolsDir/dotnet/dotnet.exe")) {
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile "$toolsDir/dotnet-install.ps1"
    & "$toolsDir/dotnet-install.ps1" -Version 8.0.414 -InstallDir "$toolsDir/dotnet" -NoPath
}
if (!(Test-Path "$toolsDir/godot/Godot_v4.5.2-stable_mono_win64")) {
    Invoke-WebRequest 'https://github.com/godotengine/godot/releases/download/4.5.2-stable/Godot_v4.5.2-stable_mono_win64.zip' -OutFile "$toolsDir/godot.zip"
    Expand-Archive -LiteralPath "$toolsDir/godot.zip" -DestinationPath "$toolsDir/godot" -Force
}
if (!(Test-Path "$toolsDir/templates/templates/windows_release_x86_64.exe")) {
    Invoke-WebRequest 'https://github.com/godotengine/godot/releases/download/4.5.2-stable/Godot_v4.5.2-stable_mono_export_templates.tpz' -OutFile "$toolsDir/templates.zip"
    Expand-Archive -LiteralPath "$toolsDir/templates.zip" -DestinationPath "$toolsDir/templates" -Force
}
Write-Output 'Local tools ready: Godot 4.5.2 .NET and .NET SDK 8.0.414.'
