param([switch]$SkipStart)
$ErrorActionPreference = 'Stop'
$driver = Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$evidence = Join-Path $PSScriptRoot '..\artifacts\validation\alerts'
function Send($data) { & $driver -Action Command -Command ($data | ConvertTo-Json -Compress) -EvidenceFolder alerts }
function Capture($name) { & $driver -Action Capture -Name $name -EvidenceFolder alerts }
function Report { Send @{action='alertReport'}; Get-Content (Join-Path $evidence 'alerts-report.json') -Raw | ConvertFrom-Json }
if (-not $SkipStart) { & $driver -Action Start -SkipBuild -IsolatedIdentity -EvidenceFolder alerts }
$deadline = [DateTime]::UtcNow.AddSeconds(40)
while (-not (Test-Path (Join-Path $evidence 'window.json'))) { if ([DateTime]::UtcNow -gt $deadline) { throw 'No se abrió el arnés WinUI.' }; Start-Sleep -Milliseconds 100 }
$captures = @()
foreach ($theme in @('dark','light')) {
    Send @{action=$theme}
    foreach ($size in @(@{width=1920;height=1080},@{width=1366;height=768})) {
        Send @{action='size';width=$size.width;height=$size.height}
        Send @{action='alerts'}; Send @{action='alertSelect'}
        $report = Report
        if ($report.count -ne 12 -or $null -eq $report.selected -or $report.summary.critical -ne 3) { throw 'La pantalla no representa el escenario de prueba esperado.' }
        $captures += Capture "$theme-$($size.width)x$($size.height)"
    }
}
Send @{action='dark'}; Send @{action='size';width=1920;height=1080}; Send @{action='alerts'}
Send @{action='alertBell'}; & $driver -Action Capture -Name 'campana' -IncludePopups -EvidenceFolder alerts | Out-Null
& $driver -Action Key -Keys '{ESC}' -EvidenceFolder alerts
& $driver -Action Invoke -Name 'Configuración' -EvidenceFolder alerts
Start-Sleep -Milliseconds 300
Capture 'configuracion' | Out-Null
& $driver -Action Invoke -Name 'Cerrar' -EvidenceFolder alerts
Send @{action='alerts';empty=$true}; $empty = Report
if ($empty.count -ne 0 -or $empty.summary.active -ne 0) { throw 'El estado vacío muestra cifras ficticias.' }
Capture 'sin-alertas' | Out-Null
Send @{action='alerts';error=$true}; $errorState = Report
if ($errorState.error.Length -eq 0 -or $null -ne $errorState.summary) { throw 'La indisponibilidad debe distinguirse de un cero real.' }
Capture 'error-servidor' | Out-Null
Send @{action='alerts'}; Send @{action='alertSelect'}
@{passed=$true;fixtureOnly=$true;captures=$captures;limitations='Capturas WinUI reales con datos ficticios aislados. No verifican API desplegada, envío SMTP ni activación de Windows. DPI real del escritorio, sin cambiar la escala del sistema.'} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $evidence 'validation.json') -Encoding utf8
