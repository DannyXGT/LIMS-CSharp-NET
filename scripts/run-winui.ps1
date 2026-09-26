param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [ValidateSet('x64', 'x86', 'ARM64')]
    [string]$Architecture = 'x64'
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $projectRoot 'Frontend\Lims.Desktop\Lims.Desktop.csproj'
$targetFramework = 'net10.0-windows10.0.26100.0'
$runtimeIdentifier = 'win-' + $Architecture.ToLowerInvariant()

dotnet build $projectPath -c $Configuration -p:Platform=$Architecture
if ($LASTEXITCODE -ne 0) {
    throw 'WinUI build failed.'
}

$sourcePath = Join-Path $projectRoot "Frontend\Lims.Desktop\bin\$Architecture\$Configuration\$targetFramework\$runtimeIdentifier"
if (-not (Test-Path -LiteralPath (Join-Path $sourcePath 'AppxManifest.xml'))) {
    throw "Compiled AppxManifest.xml was not found at $sourcePath."
}

$preferredWinApp = 'C:\Tools\WinAppCLI\winapp.exe'
$nugetRootOutput = dotnet nuget locals global-packages --list
$nugetRoot = ($nugetRootOutput -split ': ', 2)[1].Trim()
$packageWinApp = Join-Path $nugetRoot 'microsoft.windows.sdk.buildtools.winapp\0.7.0\tools\win-x64\winapp.exe'
$winApp = if (Test-Path -LiteralPath $preferredWinApp) { $preferredWinApp } else { $packageWinApp }
if (-not (Test-Path -LiteralPath $winApp)) {
    throw "WinApp CLI was not found at $preferredWinApp or $packageWinApp."
}

$temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$stagePath = Join-Path $temporaryRoot ('LimsWinUI-run-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stagePath | Out-Null

try {
    Get-ChildItem -LiteralPath $sourcePath |
        Where-Object { $_.Name -ne 'Lims.Desktop.build.appxrecipe' -and $_.Name -ne 'publish' } |
        Copy-Item -Destination $stagePath -Recurse

    & $winApp run $stagePath `
        --manifest (Join-Path $stagePath 'AppxManifest.xml') `
        --exe Lims.Desktop.exe `
        --with-alias
    $winAppExitCode = $LASTEXITCODE
}
finally {
    $resolvedStage = [System.IO.Path]::GetFullPath($stagePath)
    if ($resolvedStage.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -and
        [System.IO.Path]::GetFileName($resolvedStage).StartsWith('LimsWinUI-run-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force -ErrorAction SilentlyContinue
    }
}

exit $winAppExitCode
