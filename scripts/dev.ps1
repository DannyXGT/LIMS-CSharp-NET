param(
    [ValidateSet('Development', 'Production')]
    [string]$Environment = 'Development',

    [ValidateSet('x64', 'x86', 'ARM64')]
    [string]$Architecture = 'x64'
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $projectRoot 'Frontend\Lims.Desktop\Lims.Desktop.csproj'
$winApp = 'C:\Tools\WinAppCLI\winapp.exe'
$runtimeIdentifier = 'win-' + $Architecture.ToLowerInvariant()
$sourceRoots = @(
    (Join-Path $projectRoot 'Frontend\Lims.Desktop'),
    (Join-Path $projectRoot 'Frontend\Lims.DesignSystem'),
    (Join-Path $projectRoot 'Shared\Lims.Contracts')
)
$watchedExtensions = @('.cs', '.xaml', '.json', '.csproj', '.props', '.targets')

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

function Get-SourceSnapshot {
    $entries = foreach ($sourceRoot in $sourceRoots) {
        Get-ChildItem -LiteralPath $sourceRoot -File -Recurse |
            Where-Object {
                $watchedExtensions -contains $_.Extension -and
                $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
            } |
            ForEach-Object {
                '{0}|{1}|{2}' -f $_.FullName, $_.LastWriteTimeUtc.Ticks, $_.Length
            }
    }

    return ($entries | Sort-Object) -join "`n"
}

function Build-Frontend {
    Write-Host 'Compilando cambios...'
    dotnet build $projectPath `
        --configuration Debug `
        --runtime $runtimeIdentifier `
        --no-restore `
        -m:1 `
        -p:BuildInParallel=false

    return $LASTEXITCODE -eq 0
}

function Stop-RunningApp {
    if ($script:launcherProcess -and -not $script:launcherProcess.HasExited) {
        Stop-Process -Id $script:launcherProcess.Id -Force -ErrorAction SilentlyContinue
    }

    Get-Process -Name 'Lims.Desktop' -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue

    $script:launcherProcess = $null
}

function Start-Frontend {
    Write-Host 'Abriendo LIMS...'
    $arguments = @(
        'run',
        $projectPath,
        '--configuration', 'Debug',
        '--arch', $Architecture.ToLowerInvariant(),
        '--with-alias',
        '--no-build',
        '--no-restore',
        '--quiet'
    )

    $script:launcherProcess = Start-Process `
        -FilePath $winApp `
        -ArgumentList $arguments `
        -WorkingDirectory $projectRoot `
        -NoNewWindow `
        -PassThru
}

$savedEnvironment = $env:DOTNET_ENVIRONMENT
$script:launcherProcess = $null

try {
    $env:DOTNET_ENVIRONMENT = $Environment
    Assert-ValidApiConfiguration

    dotnet restore $projectPath `
        --runtime $runtimeIdentifier `
        --disable-parallel `
        -p:Configuration=Debug `
        -p:BuildInParallel=false
    if ($LASTEXITCODE -ne 0) {
        throw 'Frontend restore failed.'
    }

    if (-not (Build-Frontend)) {
        throw 'Initial frontend build failed.'
    }

    Stop-RunningApp
    Start-Frontend

    Write-Host 'Observando cambios C# y XAML. Detener con Ctrl+C.'
    Write-Host 'Este flujo recompila incrementalmente y relanza; no usa Hot Reload.'
    $snapshot = Get-SourceSnapshot

    while ($true) {
        Start-Sleep -Milliseconds 750
        $newSnapshot = Get-SourceSnapshot
        if ($newSnapshot -eq $snapshot) {
            continue
        }

        Start-Sleep -Milliseconds 350
        $snapshot = Get-SourceSnapshot
        Stop-RunningApp

        if (Build-Frontend) {
            Start-Frontend
        }
        else {
            Write-Warning 'La compilacion fallo. Corrija el archivo; el siguiente cambio volvera a intentarlo.'
        }
    }
}
finally {
    Stop-RunningApp
    $env:DOTNET_ENVIRONMENT = $savedEnvironment
}
