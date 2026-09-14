param([switch]$Package, [switch]$Test)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    if (!(Test-Path "$root/Assets/Vanilla/SOURCE.json")) {
        throw 'The portfolio source preview excludes the required personal resource pack. See ASSETS.md. Core tests can run separately without that pack.'
    }
    & "$PSScriptRoot/bootstrap.ps1"
    $env:DOTNET_ROOT = Join-Path $root '.tools/dotnet'
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $engine = Join-Path $root '.tools/godot/Godot_v4.5.2-stable_mono_win64/Godot_v4.5.2-stable_mono_win64_console.exe'
    & dotnet build CozyCave.csproj
    if ($LASTEXITCODE) { throw 'Game build failed' }
    & dotnet build src/Cave.Desktop
    if ($LASTEXITCODE) { throw 'Desktop helper build failed' }
    & $engine --headless --editor --path $root --import
    if ($LASTEXITCODE) { throw 'Godot import failed' }
    if ($Test) {
        & dotnet run --project tests/Cave.Tests
        if ($LASTEXITCODE) { throw 'Core tests failed' }
    }
    if ($Package) {
        New-Item -ItemType Directory -Force "$root/dist/CozyCave" | Out-Null
        $exportLog = & $engine --headless --path $root --export-release 'Windows Desktop' "$root/dist/CozyCave/Cave.exe" 2>&1
        $exportLog | Write-Output
        if ($LASTEXITCODE -or ($exportLog -match '^ERROR:')) { throw 'Game export failed; inspect the export log' }
        & dotnet publish src/Cave.Desktop -c Release -r win-x64 --self-contained true -o "$root/dist/CozyCave" -p:PublishSingleFile=true
        if ($LASTEXITCODE) { throw 'Helper publish failed' }
        Copy-Item -LiteralPath "$root/README.md" -Destination "$root/dist/CozyCave/README.md" -Force
        Copy-Item -LiteralPath "$root/ASSETS.md" -Destination "$root/dist/CozyCave/ASSETS.md" -Force
        $licenseDir = "$root/dist/CozyCave/licenses"
        New-Item -ItemType Directory -Force $licenseDir | Out-Null
        Copy-Item -LiteralPath "$root/Assets/Vanilla/SOURCE.json" -Destination "$licenseDir/Minecraft-asset-sources.json" -Force
        Copy-Item -LiteralPath "$root/Assets/Fonts/OFL.txt" -Destination "$licenseDir/Pixel-font-OFL.txt" -Force
        Copy-Item -LiteralPath "$env:DOTNET_ROOT/LICENSE.txt" -Destination "$licenseDir/dotnet-LICENSE.txt" -Force
        Copy-Item -LiteralPath "$env:DOTNET_ROOT/ThirdPartyNotices.txt" -Destination "$licenseDir/dotnet-ThirdPartyNotices.txt" -Force
        if (!(Test-Path "$licenseDir/Godot-LICENSE.txt")) { Invoke-WebRequest 'https://raw.githubusercontent.com/godotengine/godot/4.5.2-stable/LICENSE.txt' -OutFile "$licenseDir/Godot-LICENSE.txt" }
        if (!(Test-Path "$licenseDir/Godot-COPYRIGHT.txt")) { Invoke-WebRequest 'https://raw.githubusercontent.com/godotengine/godot/4.5.2-stable/COPYRIGHT.txt' -OutFile "$licenseDir/Godot-COPYRIGHT.txt" }
        Write-Output "Ready: $root/dist/CozyCave/Cave.Desktop.exe"
    }
} finally { Pop-Location }
