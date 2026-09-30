param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [ValidateSet('x64', 'x86', 'ARM64')]
    [string]$Architecture = 'x64',

    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $projectRoot 'Frontend\Lims.Desktop\Lims.Desktop.csproj'
$runtimeIdentifier = 'win-' + $Architecture.ToLowerInvariant()

dotnet restore $projectPath `
    --runtime $runtimeIdentifier `
    --disable-parallel `
    -p:Configuration=$Configuration `
    -p:BuildInParallel=false
if ($LASTEXITCODE -ne 0) {
    throw 'Frontend restore failed.'
}

if ($Clean) {
    dotnet clean $projectPath `
        --configuration $Configuration `
        --runtime $runtimeIdentifier `
        -m:1 `
        -p:BuildInParallel=false
    if ($LASTEXITCODE -ne 0) {
        throw 'Frontend clean failed.'
    }
}

dotnet build $projectPath `
    --configuration $Configuration `
    --runtime $runtimeIdentifier `
    --no-restore `
    -m:1 `
    -p:BuildInParallel=false
if ($LASTEXITCODE -ne 0) {
    throw 'Frontend build failed.'
}

Write-Host "Build completed: $Configuration | $Architecture"
Write-Host "Run with: .\scripts\run.ps1 -Configuration $Configuration -Architecture $Architecture"
