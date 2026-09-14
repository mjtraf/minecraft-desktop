# Optional offline voice model. Reuses Handy's Parakeet V3 download when available.
$ErrorActionPreference = 'Stop'
$modelName = 'parakeet-tdt-0.6b-v3-int8'
$required = @('encoder-model.int8.onnx', 'decoder_joint-model.int8.onnx', 'nemo128.onnx', 'vocab.txt')
$modelRoot = Join-Path $env:LOCALAPPDATA 'CozyCave/models'
$target = Join-Path $modelRoot $modelName
$handy = Join-Path $env:APPDATA "com.pais.handy/models/$modelName"
foreach ($candidate in @($target, $handy)) {
    if (@($required | Where-Object { !(Test-Path -LiteralPath (Join-Path $candidate $_)) }).Count -eq 0) {
        Write-Output "Voice model ready: $candidate"
        return
    }
}
if (Test-Path -LiteralPath $target) { throw "Incomplete model at $target. Rename that folder before retrying." }
New-Item -ItemType Directory -Path $modelRoot -Force | Out-Null
$staging = Join-Path $modelRoot ('.download-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
$archive = Join-Path $staging 'parakeet-v3-int8.tar.gz'
Write-Output 'Downloading Parakeet V3 (about 480 MB). Model license: CC BY 4.0; see docs/VOICE-LICENSES.txt.'
& curl.exe -L --fail --retry 2 -A 'Handy/0.9.6' -o $archive 'https://blob.handy.computer/parakeet-v3-int8.tar.gz'
if ($LASTEXITCODE) { throw "Download failed. Temporary files are in $staging" }
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne '43d37191602727524a7d8c6da0eef11c4ba24320f5b4730f1a2497befc2efa77') { throw 'Voice model checksum mismatch.' }
& tar.exe -xf $archive -C $staging
if ($LASTEXITCODE) { throw 'Model extraction failed.' }
$extracted = Join-Path $staging $modelName
foreach ($file in $required) { if (!(Test-Path -LiteralPath (Join-Path $extracted $file))) { throw "Missing model file: $file" } }
Move-Item -LiteralPath $extracted -Destination $target
Remove-Item -LiteralPath $archive
Write-Output "Voice model ready: $target"
