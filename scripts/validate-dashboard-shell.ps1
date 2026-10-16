param([switch]$SkipStart, [string]$EvidenceFolder = 'dashboard-shell-restoration')
$ErrorActionPreference = 'Stop'
$driver = Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$evidence = Join-Path $PSScriptRoot ('..\artifacts\validation\' + $EvidenceFolder)
function Send($data) { & $driver -Action Command -EvidenceFolder $EvidenceFolder -Command ($data | ConvertTo-Json -Compress) }
function Check($condition, $message) { if (-not $condition) { throw $message } }
function Report { Send @{action='dashboardReport'}; Get-Content (Join-Path $evidence 'dashboard-layout.json') -Raw | ConvertFrom-Json }
function Capture($name) { & $driver -Action Capture -EvidenceFolder $EvidenceFolder -Name $name }
if (-not $SkipStart) {
    $root = [IO.Path]::GetFullPath($evidence) + [IO.Path]::DirectorySeparatorChar
    Get-Process -Name ('Lims.VisualValidation.' + ($EvidenceFolder -replace '[^A-Za-z0-9.-]', '')) -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) } | Stop-Process
    & $driver -Action Start -SkipBuild -IsolatedIdentity -EvidenceFolder $EvidenceFolder
}
$deadline = [DateTime]::UtcNow.AddSeconds(30)
while (-not (Test-Path (Join-Path $evidence 'window.json'))) {
    if ([DateTime]::UtcNow -gt $deadline) { throw 'No abrió la validación WinUI.' }
    Start-Sleep -Milliseconds 100
}
$captures = @()
foreach ($size in @(@{width=1920;height=1080}, @{width=1600;height=900}, @{width=1366;height=768})) {
    $suffix = "$($size.width)x$($size.height)"
    Send @{action='size';width=$size.width;height=$size.height}
    Send @{action='dashboard';scenario='Administrator'}
    Send @{action='dashboardScroll';offset=0}
    Send @{action='pane';open=$false}
    $r = Report
    Check ($r.headerTitle -eq 'Resumen dashboard' -and -not $r.shellHeaderVisible) 'El título de Inicio está duplicado.'
    Check ($r.data.Standards.Registered -eq 53 -and $r.data.Expirations.Count -eq 6) 'El escenario no separa 53 estándares y seis prioridades.'
    Check (($r.data.MethodStatistics.Methods | Measure-Object Count -Sum).Sum -eq 53) 'La distribución no cuenta el catálogo completo.'
    Check ($r.extentWidth -le $r.scrollWidth + 2) 'Inicio desborda horizontalmente.'
    $captures += Capture "home-collapsed-$suffix"
    Send @{action='pane';open=$true}
    $captures += Capture "home-expanded-$suffix"
    Send @{action='standards'}
    Send @{action='pane';open=$false}
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 150
        $r = Report
    } while (($r.module -ne 'standards' -or $r.standardsBusy -or -not $r.standardsLoaded) -and [DateTime]::UtcNow -lt $deadline)
    Check ($r.module -eq 'standards' -and $r.shellHeaderVisible) 'Estándares no conservó la cabecera original.'
    $captures += Capture "standards-$suffix"
    Send @{action='permissions'}
    Start-Sleep -Milliseconds 350
    Send @{action='permissionSelect';index=0}
    Send @{action='pane';open=$false}
    Send @{action='permissionReport'}
    $permission = Get-Content (Join-Path $evidence 'permission-layout.json') -Raw | ConvertFrom-Json
    $captures += Capture "permissions-$suffix"
    $r = Report
    Check ($r.module -eq 'permissions' -and $r.shellHeaderVisible) 'Permisos no conservó la cabecera original.'
}
@{passed=$true;fixtureOnly=$true;captures=$captures;scope='Shell original, catálogo de 53 registros y seis prioridades; no evidencia de despliegue en la API remota.'} |
    ConvertTo-Json -Depth 6 | Set-Content (Join-Path $evidence 'shell-validation.json') -Encoding utf8
Write-Output 'Inicio contraído/expandido, Estándares y Permisos capturados en tres resoluciones.'
