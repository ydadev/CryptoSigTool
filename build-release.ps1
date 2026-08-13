param(
    [string]$Version = '1.4.0'
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$artifacts = Join-Path $repoRoot 'artifacts'
$appOutput = Join-Path $artifacts 'app'
$installerOutput = Join-Path $artifacts 'installer'
$bundle = Join-Path $repoRoot 'CryptoSigTool.Installer\Bundle'
$dist = Join-Path $repoRoot 'dist'

foreach ($target in @($artifacts, $dist)) {
    $full = [IO.Path]::GetFullPath($target)
    if (-not $full.StartsWith([IO.Path]::GetFullPath($repoRoot) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe build target: $full"
    }
    if (Test-Path -LiteralPath $full) {
        Remove-Item -LiteralPath $full -Recurse -Force
    }
}

New-Item -ItemType Directory -Path $appOutput, $installerOutput, $bundle, $dist -Force | Out-Null
Get-ChildItem -LiteralPath $bundle -File | Where-Object Name -ne '.gitkeep' | Remove-Item -Force

dotnet publish (Join-Path $repoRoot 'CryptoSigTool\CryptoSigTool.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false -p:DebugType=None -o $appOutput --nologo
if ($LASTEXITCODE -ne 0) { throw 'CryptoSigTool publish failed.' }

Copy-Item -LiteralPath (Join-Path $appOutput 'CryptoSigTool.exe') -Destination $bundle
Copy-Item -LiteralPath (Join-Path $repoRoot 'CryptoSigTool\INSTRUCTIONS-RU.txt') -Destination $bundle
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination $bundle

dotnet publish (Join-Path $repoRoot 'CryptoSigTool.Installer\CryptoSigTool.Installer.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false -p:DebugType=None -o $installerOutput --nologo
if ($LASTEXITCODE -ne 0) { throw 'Installer publish failed.' }

$portableDirectory = Join-Path $artifacts "CryptoSigTool-$Version-win-x64"
New-Item -ItemType Directory -Path $portableDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $appOutput 'CryptoSigTool.exe') -Destination $portableDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot 'CryptoSigTool\INSTRUCTIONS-RU.txt') -Destination $portableDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination $portableDirectory

$zipPath = Join-Path $dist "CryptoSigTool-$Version-win-x64.zip"
Compress-Archive -Path (Join-Path $portableDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal

$setupPath = Join-Path $dist "CryptoSigTool-Setup-$Version.exe"
Copy-Item -LiteralPath (Join-Path $installerOutput 'CryptoSigTool-Setup.exe') -Destination $setupPath

$checksums = @($setupPath, $zipPath) | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_))"
}
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'), $checksums, [Text.UTF8Encoding]::new($false))

Write-Host "Release artifacts created in $dist"
Get-ChildItem -LiteralPath $dist | Select-Object Name, Length
