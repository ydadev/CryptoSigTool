param(
    [string]$Version = '1.8.0',
    [string]$InnoCompiler
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
        foreach ($item in Get-ChildItem -LiteralPath $full -Force) {
            $itemFull = [IO.Path]::GetFullPath($item.FullName)
            if (-not $itemFull.StartsWith($full + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Unsafe build item: $itemFull"
            }
            Remove-Item -LiteralPath $itemFull -Recurse -Force
        }
    }
}

New-Item -ItemType Directory -Path $appOutput, $installerOutput, $bundle, $dist -Force | Out-Null
Get-ChildItem -LiteralPath $bundle -File | Where-Object Name -ne '.gitkeep' | Remove-Item -Force

if ([string]::IsNullOrWhiteSpace($InnoCompiler)) {
    $compilerCandidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $repoRoot 'tools\Inno Setup 6\ISCC.exe')
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    $InnoCompiler = $compilerCandidates |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($InnoCompiler) -or -not (Test-Path -LiteralPath $InnoCompiler -PathType Leaf)) {
    throw 'Inno Setup 6 compiler (ISCC.exe) was not found. Install Inno Setup 6 or pass -InnoCompiler.'
}

dotnet publish (Join-Path $repoRoot 'CryptoSigTool\CryptoSigTool.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false -p:DebugType=None -p:NuGetAudit=false -o $appOutput --nologo
if ($LASTEXITCODE -ne 0) { throw 'CryptoSigTool publish failed.' }

Copy-Item -LiteralPath (Join-Path $appOutput 'CryptoSigTool.exe') -Destination $bundle
Copy-Item -LiteralPath (Join-Path $repoRoot 'CryptoSigTool\INSTRUCTIONS-RU.txt') -Destination $bundle
Copy-Item -LiteralPath (Join-Path $repoRoot 'DISCLAIMER-RU.txt') -Destination $bundle
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination $bundle
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md') -Destination $bundle

& $InnoCompiler "/DMyAppVersion=$Version" (Join-Path $repoRoot 'CryptoSigTool.Installer\CryptoSigTool.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }

$portableDirectory = Join-Path $artifacts "CryptoSigTool-$Version-win-x64"
New-Item -ItemType Directory -Path $portableDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $appOutput 'CryptoSigTool.exe') -Destination $portableDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot 'CryptoSigTool\INSTRUCTIONS-RU.txt') -Destination $portableDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot 'DISCLAIMER-RU.txt') -Destination $portableDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination $portableDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md') -Destination $portableDirectory

$zipPath = Join-Path $dist "CryptoSigTool-$Version-win-x64.zip"
Compress-Archive -Path (Join-Path $portableDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal

$setupPath = Join-Path $dist "CryptoSigTool-Setup-$Version.exe"
Copy-Item -LiteralPath (Join-Path $installerOutput "CryptoSigTool-Setup-$Version.exe") -Destination $setupPath

$checksums = @($setupPath, $zipPath) | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_))"
}
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'), $checksums, [Text.UTF8Encoding]::new($false))

$releaseBundleDirectory = Join-Path $artifacts 'release-bundle'
New-Item -ItemType Directory -Path $releaseBundleDirectory -Force | Out-Null
Copy-Item -LiteralPath $setupPath, $zipPath, (Join-Path $dist 'SHA256SUMS.txt') -Destination $releaseBundleDirectory
$releaseNotes = Join-Path $repoRoot "docs\RELEASE_NOTES_$Version.md"
if (-not (Test-Path -LiteralPath $releaseNotes -PathType Leaf)) { throw "Release notes not found: $releaseNotes" }
Copy-Item -LiteralPath $releaseNotes -Destination $releaseBundleDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\INSTALLATION.md') -Destination $releaseBundleDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot 'DISCLAIMER-RU.txt') -Destination $releaseBundleDirectory
$releaseBundlePath = Join-Path $dist "CryptoSigTool-$Version-Release.zip"
Compress-Archive -Path (Join-Path $releaseBundleDirectory '*') -DestinationPath $releaseBundlePath -CompressionLevel Optimal

Write-Host "Release artifacts created in $dist"
Get-ChildItem -LiteralPath $dist | Select-Object Name, Length
