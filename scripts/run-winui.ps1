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

# Match WinAppCLI project-mode evaluation: RID selects the architecture without
# adding a separate Platform output directory that leaves bin/Debug/AppX stale.
dotnet build $projectPath -c $Configuration -r $runtimeIdentifier -m:1 -p:BuildInParallel=false
if ($LASTEXITCODE -ne 0) {
    throw 'WinUI build failed.'
}

$sourcePath = Join-Path $projectRoot "Frontend\Lims.Desktop\bin\$Configuration\$targetFramework\$runtimeIdentifier"
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

Write-Output ('Compiled desktop: ' + (Join-Path $sourcePath 'Lims.Desktop.dll'))
Write-Output ('SHA256: ' + (Get-FileHash -LiteralPath (Join-Path $sourcePath 'Lims.Desktop.dll')).Hash)
& $winApp run $projectPath `
    --configuration $Configuration `
    --arch $Architecture `
    --with-alias `
    --no-build `
    --no-restore
exit $LASTEXITCODE
