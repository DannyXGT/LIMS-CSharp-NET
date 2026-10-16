param([switch]$SkipStart)
$ErrorActionPreference = 'Stop'
$driver = Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$folder = 'dashboard-header'
$evidence = Join-Path $PSScriptRoot '..\artifacts\validation\dashboard-header'
function Send($data) { & $driver -Action Command -Command ($data | ConvertTo-Json -Compress) -EvidenceFolder $folder }
function Check($condition, $message) { if (-not $condition) { throw $message } }
function Report {
    Send @{action='dashboardReport'}
    Get-Content (Join-Path $evidence 'dashboard-layout.json') -Raw | ConvertFrom-Json
}
if (-not $SkipStart) { & $driver -Action Start -SkipBuild -IsolatedIdentity -EvidenceFolder $folder }
$deadline = [DateTime]::UtcNow.AddSeconds(40)
while (-not (Test-Path (Join-Path $evidence 'window.json'))) {
    if ([DateTime]::UtcNow -gt $deadline) { throw 'No se abrió WinUI.' }
    Start-Sleep -Milliseconds 100
}
$results = @()
foreach ($theme in @('dark', 'light')) {
    Send @{action=$theme}
    foreach ($size in @(@{width=1920;height=1080}, @{width=1600;height=900}, @{width=1366;height=768})) {
        Send @{action='size';width=$size.width;height=$size.height}
        Send @{action='dashboard';scenario='Administrator'}
        Send @{action='dashboardScroll';offset=0}
        $report = Report
        $header = $report.elements | Where-Object name -eq 'DashboardHeader'
        $metrics = $report.elements | Where-Object name -eq 'MetricsGrid'
        Check ($report.theme -eq 'Dark') 'Inicio debe conservar el tema oscuro.'
        Check (-not $report.shellHeaderVisible) 'Inicio tiene una cabecera duplicada en el shell.'
        Check ($header.height -ge 40 -and $header.height -le 60) 'Altura desproporcionada de la cabecera.'
        Check ([Math]::Abs($metrics.y - $header.y - $header.height - 10) -lt 2) 'Hay espacio residual antes de las estadísticas.'
        Check ($report.extentWidth -le $report.scrollWidth + 2) 'Hay desbordamiento horizontal.'
        Check ($report.headerTitle -eq 'Resumen dashboard') 'El título del resumen no corresponde al encabezado solicitado.'
        $name = "$theme-$($size.width)x$($size.height)"
        $capture = & $driver -Action Capture -Name $name -EvidenceFolder $folder
        $report | ConvertTo-Json -Depth 16 | Set-Content (Join-Path $evidence ($name + '.json')) -Encoding utf8
        $results += @{name=$name;dpi=$capture.Dpi;headerHeight=$header.height;fixtureOnly=$true}
    }
}
Send @{action='dashboard';scenario='Error'}
$errorReport = Report
Check ($errorReport.Error.Length -gt 0 -and $null -eq $errorReport.data -and $errorReport.retryEnabled) 'El error de consulta debe ser visible y permitir reintentar.'
& $driver -Action Capture -Name 'api-error' -EvidenceFolder $folder | Out-Null
& $driver -Action Invoke -Name 'Reintentar' -EvidenceFolder $folder
Start-Sleep -Milliseconds 400
Send @{action='dashboard';scenario='Administrator'}
$recovered = Report
Check ($null -ne $recovered.data -and $recovered.Error.Length -eq 0 -and -not $recovered.IsBusy) 'La consulta no recuperó los datos.'
@{passed=$true;fixtureOnly=$true;matrix=$results;limitations='Las capturas usan el DPI real del escritorio. No se cambian los ajustes de Windows ni se simula otra escala.'} |
    ConvertTo-Json -Depth 8 | Set-Content (Join-Path $evidence 'validation.json') -Encoding utf8
Write-Output 'Cabecera del resumen y recuperación verificadas con datos aislados de prueba.'
