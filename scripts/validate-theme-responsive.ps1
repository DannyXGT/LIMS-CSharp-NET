param([switch]$SkipStart, [string]$EvidenceFolder = 'theme-responsive')
$ErrorActionPreference = 'Stop'
$driver = Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$evidence = Join-Path $PSScriptRoot ('..\artifacts\validation\' + $EvidenceFolder)
function Send($data) { & $driver -Action Command -Command ($data | ConvertTo-Json -Compress) -EvidenceFolder $EvidenceFolder }
function Capture($name) { Start-Sleep -Milliseconds 250; & $driver -Action Capture -Name $name -IncludePopups -EvidenceFolder $EvidenceFolder }
function Report($name) {
    Send @{action='themeReport'}
    $report = Get-Content (Join-Path $evidence 'theme-layout.json') -Raw | ConvertFrom-Json
    $report | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $evidence ($name + '.json')) -Encoding utf8
    if ($report.requestedTheme -ne 'Dark' -or $report.actualTheme -ne 'Dark' -or $report.text -ne '#FFF5F7FA') { throw 'El tema global o el texto programatico no utiliza la paleta oscura.' }
    if ($name.StartsWith('stock-') -and $null -eq $report.dialog) { throw 'No se abrio el editor Stock.' }
    if ($null -ne $report.dialog) {
        if ($report.dialog.theme -ne 'Dark') { throw 'El editor Stock perdio el tema oscuro.' }
        foreach ($button in @($report.dialog.cancel, $report.dialog.save)) {
            if ($button.y -lt 0 -or $button.y + $button.height -gt $report.rootSize.Height + 2) { throw 'Accion de Stock fuera del area efectiva.' }
        }
        foreach ($popup in $report.dialog.popup) {
            $placement = $popup.Placement
            if ($placement.Y -lt 0 -or $placement.Y + $placement.Height -gt $report.dialog.cancel.y) { throw 'Un desplegable invade el footer de Stock.' }
        }
    }
    return $report
}
if (-not $SkipStart) { & $driver -Action Start -SkipBuild -IsolatedIdentity -EvidenceFolder $EvidenceFolder }
$deadline = [DateTime]::UtcNow.AddSeconds(45)
while (-not (Test-Path (Join-Path $evidence 'window.json'))) {
    if ([DateTime]::UtcNow -gt $deadline) { throw 'No inicio la ventana WinUI aislada.' }
    Start-Sleep -Milliseconds 100
}
Send @{action='size';width=1366;height=768}
Send @{action='permissions'}
Start-Sleep -Milliseconds 600
Capture 'permissions-unselected-1366x768'
Send @{action='permissionSelect';index=0}
Capture 'permissions-selected-1366x768'
$environment = Report 'environment'
& $driver -Action HoverCapture -Name 'Consultar estándares' -EvidenceFolder $EvidenceFolder
& $driver -Action HoverCapture -Name 'Registrar estándares' -EvidenceFolder $EvidenceFolder
Send @{action='pane';open=$true}
Capture 'sidebar-expanded-1366x768'
& $driver -Action Click -Name 'Preparaciones' -EvidenceFolder $EvidenceFolder
Capture 'sidebar-collapsed-1366x768'
if ((Report 'sidebar-navigation').paneOpen) { throw 'El sidebar no se contrajo al invocar el modulo.' }
foreach ($size in @(@{width=1366;height=768}, @{width=1600;height=900}, @{width=1920;height=1080}, @{width=1093;height=614})) {
    Send @{action='size';width=$size.width;height=$size.height}
    Send @{action='preparations'}
    Start-Sleep -Milliseconds 300
    Send @{action='stockEditor'}
    Start-Sleep -Milliseconds 600
    $name = 'stock-empty-' + $size.width + 'x' + $size.height
    Capture $name
    $null = Report $name
    if ($size.width -eq 1366) {
        & $driver -Action Click -Name 'Buscar estándar origen' -EvidenceFolder $EvidenceFolder
        Capture 'stock-source-dropdown'
        $null = Report 'stock-source-dropdown'
        & $driver -Action Key -Keys '{ESC}' -EvidenceFolder $EvidenceFolder
        Send @{action='stockSelect';index=0}
        foreach ($field in @(@{field='NameBox';text='Validacion visual aislada'}, @{field='ConcentrationBox';text='1000'}, @{field='FinalQuantityBox';text='100'}, @{field='ActualQuantityBox';text='100.9'}, @{field='StorageTemperatureBox';text='20 C'})) {
            Send @{action='stockText';field=$field.field;text=$field.text}
        }
        Send @{action='stockDate';field='ExpirationDatePicker';text='2027-04-14T00:00:00-06:00'}
        Start-Sleep -Milliseconds 500
        Send @{action='stockReport'}
        Send @{action='stockScroll';offset=9999}
        Capture 'stock-complete-real-calculation-fixture'
        Send @{action='stockScroll';offset=9999}
        & $driver -Action Click -Name 'Fecha de preparación' -EvidenceFolder $EvidenceFolder
        Capture 'stock-calendar-footer-visible'
        $null = Report 'stock-calendar-footer-visible'
        & $driver -Action Key -Keys '{ESC}' -EvidenceFolder $EvidenceFolder
        Send @{action='stockScroll';offset=0}
        Capture 'stock-scroll-top'
    }
    Send @{action='stockClose'}
}
Send @{action='size';width=1366;height=768}
Send @{action='standards'}
Start-Sleep -Milliseconds 400
Send @{action='editor'}
Start-Sleep -Milliseconds 400
Send @{action='checks'}
$checks = Get-Content (Join-Path $evidence 'interaction-checks.json') -Raw | ConvertFrom-Json
foreach ($check in $checks.PSObject.Properties | Where-Object Name -ne 'systemAnimationsEnabled') {
    if (-not $check.Value) { throw ('Fallo de popup: ' + $check.Name) }
}
& $driver -Action Invoke -Name 'Cancelar' -EvidenceFolder $EvidenceFolder
@{fixtureOnly=$true;passed=$true;environment=$environment;limitations='All window sizes use current desktop DPI; consult environment.scale. 1093x614 is an additional small window, not a separate DPI setting. No Windows theme/DPI settings changed. No real users or preparations saved.'} |
    ConvertTo-Json -Depth 12 | Set-Content (Join-Path $evidence 'theme-validation.json') -Encoding utf8
