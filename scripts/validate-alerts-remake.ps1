param([switch]$SkipStart, [switch]$SkipMatrix)
$ErrorActionPreference = 'Stop'
$driver = Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$evidence = Join-Path $PSScriptRoot '..\artifacts\validation\alerts-remake'
function Send($data) { & $driver -Action Command -Command ($data | ConvertTo-Json -Compress) -EvidenceFolder alerts-remake }
function Capture($name, [switch]$Popup) { & $driver -Action Capture -Name $name -IncludePopups:$Popup -EvidenceFolder alerts-remake | Out-Null }
function Report($name) { Send @{action='alertReport'}; $r = Get-Content -Encoding UTF8 (Join-Path $evidence 'alerts-report.json') -Raw | ConvertFrom-Json; $r | ConvertTo-Json -Depth 15 | Set-Content -Encoding UTF8 (Join-Path $evidence ($name + '-report.json')); return $r }
function Invoke($name) { & $driver -Action Invoke -Name $name -EvidenceFolder alerts-remake; Start-Sleep -Milliseconds 250 }
function Element($name) {
    $condition = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $script:processId),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name))
    $found = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition) | Where-Object { -not $_.Current.IsOffscreen } | Select-Object -First 1
    if ($null -eq $found) { throw "Control ausente: $name" }; return $found
}
function SelectItem($name) { (Element $name).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 250 }
function Search($text) { (Element 'Buscar alertas').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text); Start-Sleep -Milliseconds 550 }
function Visible($name) {
    $bounds = (Element $name).Current.BoundingRectangle; $viewport = $script:window.Current.BoundingRectangle
    if ($bounds.Width -le 0 -or $bounds.Height -le 0 -or $bounds.Top -lt $viewport.Top -or $bounds.Bottom -gt $viewport.Bottom -or $bounds.Left -lt $viewport.Left -or $bounds.Right -gt $viewport.Right) { throw "Control fuera de la ventana: $name" }
}
function NoHorizontalScroll {
    $owned = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $script:processId)
    foreach ($element in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $owned)) {
        $pattern = $null
        if (-not $element.Current.IsOffscreen -and $element.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern, [ref]$pattern) -and $pattern.Current.HorizontallyScrollable) { throw "Desbordamiento horizontal: $($element.Current.Name)" }
    }
}
if (-not $SkipStart) { & $driver -Action Start -SkipBuild -IsolatedIdentity -EvidenceFolder alerts-remake }
$deadline = [DateTime]::UtcNow.AddSeconds(40)
while (-not (Test-Path (Join-Path $evidence 'window.json'))) { if ([DateTime]::UtcNow -gt $deadline) { throw 'No se abrió WinUI.' }; Start-Sleep -Milliseconds 100 }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$window = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr](Get-Content (Join-Path $evidence 'window.json') -Raw | ConvertFrom-Json).hwnd)
$script:processId = $window.Current.ProcessId
$checks = [Collections.Generic.List[string]]::new()
foreach ($theme in $(if ($SkipMatrix) { @() } else { @('dark','light') })) {
    Send @{action=$theme}
    foreach ($size in @(@{width=1920;height=1080},@{width=1366;height=768})) {
        Send @{action='size';width=$size.width;height=$size.height}; Send @{action='alerts'}
        $name = "$theme-$($size.width)x$($size.height)"
        $r = Report $name
        if ($r.state -ne 'Ready' -or $r.count -ne 12 -or $r.scale -ne 1.25) { throw 'Estado, datos o escala real inesperados.' }
        Capture "$name-monitor"
        Send @{action='alertSelect'}; Start-Sleep -Milliseconds 200
        $r = Report "$name-detail"
        if ($null -eq $r.selected) { throw 'No se abrió el detalle.' }
        if ($size.width -eq 1366 -and -not $r.compact.visible) { throw 'Falta vista secundaria compacta.' }
        if ($size.width -eq 1920 -and (-not $r.detail.visible -or $r.monitor.width / ($r.monitor.width + $r.detail.width) -lt 0.65)) { throw 'Proporción de paneles incorrecta.' }
        Capture "$name-detail"
        if ($size.width -eq 1366) { Invoke 'Volver al monitor' }
        & $driver -Action Inspect -Name "$name-accessibility" -EvidenceFolder alerts-remake | Out-Null
        Send @{action='alertBell'}; Visible 'Ver todas las alertas'; NoHorizontalScroll; Capture "$name-campana" -Popup
        & $driver -Action Key -Keys '{ESC}' -EvidenceFolder alerts-remake
        Invoke 'Configuración de Alertas'; Visible 'Guardar configuración'; Visible 'Cerrar'; NoHorizontalScroll; Capture "$name-configuracion" -Popup
        SelectItem 'Destinatarios'; Visible 'Cerrar'; NoHorizontalScroll; Capture "$name-destinatarios" -Popup
        Invoke 'Cerrar'; Invoke 'Historial de envíos'; Visible 'Cerrar'; NoHorizontalScroll; Capture "$name-historial" -Popup; Invoke 'Cerrar'
        $checks.Add($name)
    }
}
Send @{action='dark'}; Send @{action='size';width=1920;height=1080}; Send @{action='alerts';pageSize=5}
(Element 'Actualizar alertas').SetFocus()
Invoke 'Página siguiente'; $r = Report 'pagination-next'
if ($r.filter.page -ne 2 -or $r.count -ne 5) { throw 'Paginación siguiente incorrecta.' }
Invoke 'Página anterior'; $r = Report 'pagination-previous'
if ($r.filter.page -ne 1 -or (Element 'Página anterior').Current.IsEnabled) { throw 'Paginación anterior incorrecta.' }
Send @{action='alertQuick';index=1}; $r = Report 'critical-filter'; if ($r.count -ne 3) { throw 'Filtro crítico incorrecto.' }
Send @{action='alertQuick';index=2}; $r = Report 'upcoming-filter'; if ($r.count -ne 4) { throw 'Filtro próximo incorrecto.' }
Send @{action='alertQuick';index=3}; $r = Report 'availability-filter'; if ($r.summary.active -ne 12 -or $r.filter.category -ne 'Availability') { throw 'Filtro disponibilidad incorrecto.' }
Send @{action='alertQuick';index=0}; Search 'inexistente-zxy'; $r = Report 'filtered-empty'; if ($r.state -ne 'FilteredEmpty' -or $r.summary.active -ne 12) { throw 'Vacío filtrado incorrecto.' }; Capture 'sin-coincidencias'
Invoke 'Limpiar filtros'; Search 'Naphthol'; $r = Report 'search'; if ($r.count -lt 1) { throw 'Búsqueda sin resultados conocidos.' }; Search ''
$module = Element 'Módulo de alerta'; $module.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); SelectItem 'Stock'; $r = Report 'module-filter'; if ($r.filter.module -ne 2 -or $r.state -ne 'FilteredEmpty') { throw 'Filtro de módulo incorrecto.' }
Send @{action='alerts'}; Invoke 'Filtros avanzados'; Capture 'filtros-avanzados' -Popup
Invoke 'Limpiar filtros'; Send @{action='alertSelect'}; $before = Report 'before-actions'
Invoke 'Marcar como leído'; $read = Report 'after-read'
if ($null -eq $read.selected.readAt -or $null -ne $read.selected.acknowledgedAt -or $read.summary.pending -ne 12 -or $read.summary.unread -ne 11) { throw 'Lectura y atención no son independientes.' }
Invoke 'Atender'; $attended = Report 'after-attend'
if ($null -eq $attended.selected.acknowledgedAt -or $null -ne $attended.selected.resolvedAt -or $attended.summary.pending -ne 11) { throw 'Atención resuelve indebidamente o no persiste.' }
Capture 'detalle-atendido'
Invoke 'Ver material'; Capture 'material-origen' -Popup; Invoke 'Cerrar ficha'
Send @{action='alertBell'}; Capture 'campana' -Popup
SelectItem 'No leídas'; Capture 'campana-no-leidas' -Popup
Invoke 'Marcar todas como leídas'; $r = Report 'all-read'; if ($r.summary.unread -ne 0) { throw 'No se guardó leer todas.' }; Capture 'campana-leidas' -Popup
& $driver -Action Key -Keys '{ESC}' -EvidenceFolder alerts-remake
Invoke 'Configuración de Alertas'
foreach ($section in @('Disponibilidad','Vencimientos','Canales','Destinatarios','Programación','Estado del servicio')) { SelectItem $section; Capture ('config-' + $section) -Popup }
if ((Element 'Encolar prueba a mi correo').Current.IsEnabled) { throw 'Prueba SMTP habilitada sin configurar transporte.' }
Invoke 'Cerrar'; Invoke 'Historial de envíos'; Capture 'historial-smtp-pendiente' -Popup; Invoke 'Cerrar'
foreach ($scenario in @(@{name='sin-alertas';empty=$true;expected='Empty'},@{name='motor-pendiente';pending=$true;expected='EnginePending'},@{name='api-404';status=404;expected='Unavailable'},@{name='servidor-503';error=$true;expected='Unavailable'})) {
    Send @{action='alerts';empty=($scenario.empty -eq $true);pending=($scenario.pending -eq $true);status=($scenario.status -as [int]);error=($scenario.error -eq $true)}
    $r = Report $scenario.name; if ($r.state -ne $scenario.expected) { throw "Estado incorrecto: $($scenario.name)" }
    if ($scenario.expected -eq 'Unavailable' -and $null -ne $r.summary) { throw 'Falso cero durante error.' }
    Capture $scenario.name
    if ($scenario.status -eq 404) { Send @{action='alertBell'}; Capture 'campana-error' -Popup; & $driver -Action Key -Keys '{ESC}' -EvidenceFolder alerts-remake }
}
Send @{action='alerts';delay=5000}; $r = Report 'loading'; if ($r.state -ne 'Loading' -or (Element 'Actualizar alertas').Current.IsEnabled) { throw 'Estado de carga incorrecto.' }; Capture 'cargando'
Send @{action='alerts'}
& $driver -Action HoverCapture -Name 'Actualizar alertas' -CaptureName 'hover-actualizar' -EvidenceFolder alerts-remake | Out-Null
(Element 'Actualizar alertas').SetFocus(); & $driver -Action Key -Keys '{TAB}' -EvidenceFolder alerts-remake; Capture 'foco-teclado'
$checks.Add('pagination, search, module, quick filters, empty, pending, error, loading, read, attend, source material, bell, configuration, SMTP status, hover, keyboard focus, disabled')
@{passed=$true;fixtureOnly=$true;scale=1.25;checks=$checks;limitations='WinUI real con fixtures aislados. API desplegada verificada por separado: rutas de Alertas 404. No demuestra JOB remoto, SMTP real ni roles autenticados del servidor.'} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $evidence 'validation.json') -Encoding UTF8
