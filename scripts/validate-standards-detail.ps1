param([string]$EvidenceFolder = 'standards-detail')
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$evidence = Join-Path $workspace ('artifacts\validation\' + $EvidenceFolder)
$driver = Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$windowState = Get-Content -LiteralPath (Join-Path $evidence 'window.json') | ConvertFrom-Json
$fixtureWindow = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$windowState.hwnd)
$fixtureProcess = Get-Process -Id $fixtureWindow.Current.ProcessId
if (-not $fixtureProcess.Path.StartsWith($evidence, [StringComparison]::OrdinalIgnoreCase)) { throw 'Only the isolated fixture executable is allowed.' }
$processCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $fixtureProcess.Id)
$checks = [Collections.Generic.List[string]]::new()
function Command($data) { & $driver -Action Command -Command ($data | ConvertTo-Json -Compress) -EvidenceFolder $EvidenceFolder }
function State {
    Command @{ action='report' }
    Get-Content -LiteralPath (Join-Path $evidence 'layout.json') | ConvertFrom-Json
}
function WaitDetail([bool]$open) {
    $deadline = [DateTime]::UtcNow.AddSeconds(8)
    do {
        $state = State
        if ($state.detailOpen -eq $open) {
            if ($open) { FindElement 'Cerrar ficha' ([System.Windows.Automation.ControlType]::Button) | Out-Null }
            return $state
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Detail did not reach open=$open."
}
function Capture($name) { & $driver -Action Capture -Name $name -EvidenceFolder $EvidenceFolder | Out-Null }
function Key($keys) { & $driver -Action Key -Keys $keys -EvidenceFolder $EvidenceFolder }
function Invoke($name) { & $driver -Action Invoke -Name $name -EvidenceFolder $EvidenceFolder; Start-Sleep -Milliseconds 250 }
function FindElement($name, $type) {
    $nameCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $typeCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $type)
    $condition = [System.Windows.Automation.AndCondition]::new($processCondition, $nameCondition, $typeCondition)
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $element = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition) |
            Where-Object { -not $_.Current.IsOffscreen } | Select-Object -First 1
        if ($null -ne $element) { return $element }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Fixture control not found: $name"
}
function SetField($name, $value) {
    $element = FindElement $name ([System.Windows.Automation.ControlType]::Edit)
    $pattern = $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $pattern.SetValue($value)
}
function SelectRow($index) { Command @{action='select'; index=$index} }
function OpenRow($index) {
    SelectRow $index
    # Exercise the real ListView key handler, including keys consumed by WinUI.
    $row = FindElement ((@('Naphthol AS, CAS 92-77-3, Activo, 100 miligramos disponibles','4-Aminobiphenyl, CAS 92-67-1, Activo, 100 miligramos disponibles'))[$index]) ([System.Windows.Automation.ControlType]::ListItem)
    $row.SetFocus()
    Key '{ENTER}'
    WaitDetail $true | Out-Null
    Start-Sleep -Milliseconds 220
}

Command @{action='size'; width=1920; height=1080}
SelectRow 0
$rowName = 'Naphthol AS, CAS 92-77-3, Activo, 100 miligramos disponibles'
& $driver -Action Click -Name $rowName -EvidenceFolder $EvidenceFolder
if ((State).detailOpen) { throw 'Single click opened the detail.' }
Capture '02-fila-seleccionada'
$checks.Add('Single click selects without opening the detail')
& $driver -Action DoubleClick -Name $rowName -EvidenceFolder $EvidenceFolder
WaitDetail $true | Out-Null
Capture '03-detalle-naphthol-as'
Capture '06-disponibilidad'
$checks.Add('Real double click opens the selected material')
Command @{action='detailScroll'; offset=900}
Capture '07-auditoria'
Command @{action='detailScroll'; offset=0}
Key '{ESC}'
WaitDetail $false | Out-Null
$checks.Add('Escape closes and returns focus to the selected row')
OpenRow 1
if ((State).selectedName -ne '4-Aminobiphenyl') { throw 'Enter opened the wrong row.' }
Capture '04-detalle-4-aminobiphenyl'
$checks.Add('Enter opens the selected row')
foreach ($dimensions in @(@(1920,1080), @(1600,900), @(1366,768))) {
    Command @{action='size'; width=$dimensions[0]; height=$dimensions[1]}
    Start-Sleep -Milliseconds 250
    $layout = State
    if (-not $layout.detailOpen -or $layout.detailViewport -le 0 -or $layout.detailHeight -gt ($layout.clientHeight * 0.88) -or $layout.detailHeight -lt ($layout.clientHeight * 0.82) -or $layout.detailWidth -gt ($layout.clientWidth * 0.92) -or $layout.detailWidth -lt ($layout.clientWidth * 0.88)) { throw 'Invalid detail layout.' }
    Copy-Item -LiteralPath (Join-Path $evidence 'layout.json') -Destination (Join-Path $evidence ("layout-$($dimensions[0])x$($dimensions[1]).json"))
    Capture "detalle-$($dimensions[0])x$($dimensions[1])"
}
Capture '05-detalle-1366x768'
$checks.Add('Resize keeps detail open with fixed header and internal scroll at three resolutions')
for ($index=0; $index -lt 18; $index++) { Key '{TAB}' }
& $driver -Action Inspect -Name detalle-focus-uia -EvidenceFolder $EvidenceFolder | Out-Null
$focused = Get-Content -LiteralPath (Join-Path $evidence 'detalle-focus-uia.json') | ConvertFrom-Json | Where-Object { $_.Focused -and $_.Type -eq 'ControlType.Button' }
if (-not ($focused | Where-Object { $_.Name -in @('Editar','Reemplazar','Archivar','Cerrar','Cerrar ficha') })) { throw 'Tab focus escaped the sheet.' }
$checks.Add('Tab cycles inside the detail')
Invoke 'Cerrar ficha'
WaitDetail $false | Out-Null
Command @{action='size'; width=1920; height=1080}
for ($index=0; $index -lt 3; $index++) { OpenRow 0; Key '{ESC}'; WaitDetail $false | Out-Null }
$checks.Add('Repeated keyboard open/close restores row focus')
for ($index=0; $index -lt 15; $index++) { SelectRow ($index % 3) }
OpenRow 0
$checks.Add('Rapid row selection resolves the correct detail')
Invoke 'Editar'
SetField 'Lote' 'DEMO-EDITADO'
Invoke 'Guardar estándar'
WaitDetail $true | Out-Null
Start-Sleep -Milliseconds 250
Capture 'editar-regreso-a-ficha'
if ((State).saveCalls -ne 1) { throw 'Edit did not save exactly once.' }
$checks.Add('Edit uses the existing editor, saves once, and resumes the same detail')
Invoke 'Reemplazar'
SetField 'Lote' 'DEMO-REEMPLAZO'
Invoke 'Continuar'
SetField 'Motivo de la acción' 'Reemplazo seguro de fixture visual'
Invoke 'Reemplazar'
WaitDetail $true | Out-Null
Capture 'reemplazar-regreso-a-ficha'
if ((State).selectedName -ne 'Naphthol AS' -or (State).saveCalls -ne 2) { throw 'Replacement did not resume the source detail.' }
$checks.Add('Replace refreshes table and original detail')
Invoke 'Cerrar ficha'
WaitDetail $false | Out-Null
OpenRow 1
Invoke 'Archivar'
SetField 'Motivo de la acción' 'Archivo seguro de fixture visual'
Invoke 'Archivar'
WaitDetail $true | Out-Null
Capture '08-estado-archivado'
Command @{action='detailScroll'; offset=900}
Capture 'archivo-auditoria'
if ((State).saveCalls -ne 3) { throw 'Archive did not save exactly once.' }
$checks.Add('Archive refreshes status and archive audit in the open sheet')
Invoke 'Cerrar ficha'
WaitDetail $false | Out-Null
$status = FindElement 'Estado' ([System.Windows.Automation.ControlType]::ComboBox)
$status.SetFocus()
Key '{HOME}{DOWN}{ENTER}'
Start-Sleep -Milliseconds 250
SelectRow 0
Command @{action='detail'}
WaitDetail $true | Out-Null
Invoke 'Archivar'
SetField 'Motivo de la acción' 'Fixture sale del filtro Activo'
Invoke 'Archivar'
WaitDetail $false | Out-Null
$checks.Add('Archive closes cleanly when the material leaves the Active filter')
$checks | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'detail-interaction-checks.json') -Encoding UTF8
$checks
