param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [ValidateSet('Development', 'Production')]
    [string]$Environment = 'Development',

    [ValidateSet('x64', 'x86', 'ARM64')]
    [string]$Architecture = 'x64'
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $projectRoot 'Frontend\Lims.Desktop\Lims.Desktop.csproj'
$winApp = 'C:\Tools\WinAppCLI\winapp.exe'

if (-not (Test-Path -LiteralPath $winApp)) {
    throw "WinApp CLI was not found at $winApp."
}

function Assert-ValidApiConfiguration {
    $settingsPath = Join-Path $projectRoot 'Frontend\Lims.Desktop\appsettings.json'
    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    $apiBaseUrl = [string]$settings.Desktop.ApiBaseUrl
    $environmentSettingsPath = Join-Path $projectRoot "Frontend\Lims.Desktop\appsettings.$Environment.json"

    if (Test-Path -LiteralPath $environmentSettingsPath) {
        $environmentSettings = Get-Content -LiteralPath $environmentSettingsPath -Raw | ConvertFrom-Json
        if ($environmentSettings.Desktop.ApiBaseUrl) {
            $apiBaseUrl = [string]$environmentSettings.Desktop.ApiBaseUrl
        }
    }

    $apiUri = $null
    $isAbsolute = [Uri]::TryCreate($apiBaseUrl, [UriKind]::Absolute, [ref]$apiUri)
    $isApprovedInternalHttp = $isAbsolute -and
        $apiUri.Scheme -eq [Uri]::UriSchemeHttp -and
        $apiUri.Host -eq '10.226.248.191' -and
        $apiUri.Port -eq 8080 -and
        $apiUri.AbsolutePath -eq '/' -and
        -not $apiUri.Query -and
        -not $apiUri.Fragment

    if (-not $isAbsolute -or
        ($apiUri.Scheme -ne [Uri]::UriSchemeHttps -and -not $isApprovedInternalHttp)) {
        throw "No se puede iniciar LIMS en $Environment. Desktop:ApiBaseUrl es '$apiBaseUrl'; use HTTPS o el endpoint interno aprobado http://10.226.248.191:8080/."
    }
}

$savedEnvironment = $env:DOTNET_ENVIRONMENT

try {
    $env:DOTNET_ENVIRONMENT = $Environment
    Assert-ValidApiConfiguration

    & $winApp run $projectPath `
        --configuration $Configuration `
        --arch $Architecture.ToLowerInvariant() `
        --with-alias `
        --no-build `
        --no-restore
    $runExitCode = $LASTEXITCODE
}
finally {
    $env:DOTNET_ENVIRONMENT = $savedEnvironment
}

exit $runExitCode
