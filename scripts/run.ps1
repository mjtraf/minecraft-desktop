param([switch]$Windowed)
$root = Split-Path $PSScriptRoot -Parent
$env:DOTNET_ROOT = Join-Path $root '.tools/dotnet'
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$engine = Join-Path $root '.tools/godot/Godot_v4.5.2-stable_mono_win64/Godot_v4.5.2-stable_mono_win64.exe'
$helper = Join-Path $root 'src/Cave.Desktop/bin/Debug/net8.0-windows/Cave.Desktop.exe'
$argsList = @('--engine', ('"'+$engine+'"'), '--project', ('"'+$root+'"'))
if ($Windowed) { $argsList += '--windowed' }
Start-Process -FilePath $helper -ArgumentList $argsList -WindowStyle Hidden
