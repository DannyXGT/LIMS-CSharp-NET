param([int]$Width=1920,[int]$Height=1080)
$ErrorActionPreference='Stop'
$Step='Html';$EvidenceName=('consulta-html-'+$Width+'x'+$Height)
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'validate-consulta-jobs.ps1'))
$htmlScriptsRoot=$PSScriptRoot;$start=$source.IndexOf('$ErrorActionPreference=');$end=$source.IndexOf("if(`$Step -eq 'Inspect')")
Invoke-Expression $source.Substring($start,$end-$start).Replace('(Join-Path $PSScriptRoot ','(Join-Path $htmlScriptsRoot ').Replace('$consultaScriptsRoot=$PSScriptRoot','$consultaScriptsRoot=$htmlScriptsRoot')
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class HtmlWindow {
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int v,uint f);
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
}
'@
[HtmlWindow]::SetThreadDpiAwarenessContext([IntPtr](-4))|Out-Null
$window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState([Windows.Automation.WindowVisualState]::Normal)
[HtmlWindow]::SetWindowPos([IntPtr]$window.Current.NativeWindowHandle,[IntPtr]::Zero,0,0,$Width,$Height,0x40)|Out-Null
Start-Sleep -Milliseconds 800
$main=$window;$dpi=[HtmlWindow]::GetDpiForWindow([IntPtr]$window.Current.NativeWindowHandle)
$captureOriginal=(Get-Item Function:Capture-Window).ScriptBlock
function Fit-MainWindow {
    [HtmlWindow]::SetThreadDpiAwarenessContext([IntPtr](-4))|Out-Null
    $rect=[PlanningCapture+Rect]::new();$handle=[IntPtr]$main.Current.NativeWindowHandle;[PlanningCapture]::GetWindowRect($handle,[ref]$rect)|Out-Null
    if($rect.Right-$rect.Left -eq $Width -and $rect.Bottom-$rect.Top -eq $Height){return}
    $main.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState([Windows.Automation.WindowVisualState]::Normal)
    [HtmlWindow]::SetWindowPos($handle,[IntPtr]::Zero,0,0,$Width,$Height,0x40)|Out-Null;Start-Sleep -Milliseconds 600
    [PlanningCapture]::GetWindowRect($handle,[ref]$rect)|Out-Null
    if($rect.Right-$rect.Left -ne $Width -or $rect.Bottom-$rect.Top -ne $Height){throw 'El tamaño físico de la ventana no corresponde al caso solicitado.'}
}
function Capture-Window($element,[string]$captureName){if($element.Current.NativeWindowHandle -eq $main.Current.NativeWindowHandle){Fit-MainWindow};& $captureOriginal $element $captureName}
function Combo([string]$name,[int]$index){
    $combo=Find-Named $name;$expand=$combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern);$expand.Expand();Start-Sleep -Milliseconds 180
    $items=@($combo.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem)))
    if($items.Count -eq 0){$items=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ClassNameProperty,'ComboBoxItem'))|Where-Object{-not $_.Current.IsOffscreen})}
    if($items.Count -le $index){throw "Opciones nativas no disponibles: $name"}
    $items[$index].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();$expand.Collapse();Start-Sleep -Milliseconds 150
}
function Set-NamedValue([string]$controlName,[string]$value){
    $deadline=[DateTime]::UtcNow.AddSeconds(15)
    do{
        try{foreach($element in $window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$controlName))){
            $pattern=$null;if($element.TryGetCurrentPattern([Windows.Automation.ValuePattern]::Pattern,[ref]$pattern)){$pattern.SetValue($value);Start-Sleep -Milliseconds 200;return}
        }}catch [Windows.Automation.ElementNotAvailableException]{}
        Start-Sleep -Milliseconds 150
    }while([DateTime]::UtcNow -lt $deadline)
    throw "Campo editable no disponible tras estabilizar la ventana: $controlName"
}
function Dossier {
    Invoke-Named 'Estado del JOB';Start-Sleep -Milliseconds 600
    $script:window=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$main.Current.ProcessId))|Where-Object{$_.Current.Name -eq ('Estado del JOB · '+$job)}|Select-Object -First 1
    if(-not $script:window){throw 'No abrió el expediente del JOB fuente.'}
}
function Close-Dossier {Invoke-Named 'Cerrar';$script:window=$main;Start-Sleep -Milliseconds 400}
function Assert-Counters($page){
    Fit-MainWindow
    $title=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Text))|Where-Object{$_.Current.Name -eq 'Consulta de Jobs' -and -not $_.Current.IsOffscreen}|Select-Object -First 1
    $r=$title.Current.BoundingRectangle
    $values=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Text))|Where-Object{-not $_.Current.IsOffscreen -and $_.Current.Name -match '^\d[\d,]*$' -and $_.Current.BoundingRectangle.Top -ge ($r.Top-10) -and $_.Current.BoundingRectangle.Top -lt ($r.Bottom+10)}|Sort-Object{$_.Current.BoundingRectangle.Left})
    $expected=@($page.counts.records,$page.counts.pending,$page.counts.overdue,$page.counts.onHoldTests)
    if($values.Count -ne 4){throw "Se esperaban cuatro indicadores y hay $($values.Count)."}
    for($i=0;$i -lt 4;$i++){if([int]($values[$i].Current.Name -replace ',','') -ne $expected[$i]){throw "Indicador $i no coincide con API."}}
    Find-Named ('Pruebas On Hold ('+$page.counts.onHoldJobs+' '+$(if($page.counts.onHoldJobs -eq 1){'JOB'}else{'JOBs'})+')')|Out-Null
}
Keys '{ESC}{ESC}';Invoke-Named 'Columnas';Invoke-Named 'Restablecer columnas';Keys '{ESC}'
$reset=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'Restablecer filtros'))|Where-Object{-not $_.Current.IsOffscreen}|Select-Object -First 1
if($reset){Invoke-Named 'Restablecer filtros';Start-Sleep -Milliseconds 500}
Invoke-Named 'Filtros';Combo 'Alcance de datos' 0;Invoke-Named 'Aplicar filtros';Start-Sleep -Milliseconds 500
$operational=Api-Jobs 'Scope=Operational';Assert-Counters $operational;Capture-Window $window '01-periodo-operativo'
Passed 'Período operativo anunciado; cuatro indicadores coinciden con API sobre todos los resultados'
History-Scope
Search-Job $job
$before=Api-Jobs ('SearchMode=Exact&Search='+[uri]::EscapeDataString($job))
$original=(Invoke-RestMethod -Uri ('https://localhost:7441/api/planning/jobs/'+$before.items[0].episodeId) -Headers $headers).control
if($original.onHold){throw 'El JOB fuente ya está retenido; no se utiliza para una retención de prueba.'}
Assert-Counters $before
if($before.counts.pending -ne 24 -or $before.counts.overdue -ne 24){throw 'El JOB fuente vencido debe conservar 24 pendientes con los datos disponibles.'}
$headersInUi=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Button))|Where-Object{$_.Current.Name -like 'Ordenar por *'})
if($headersInUi.Count -ne 9){throw 'La vista principal no tiene las nueve columnas solicitadas.'}
foreach($name in @('Fecha Ingreso','Due Date','JOB','Prueba','Laboratorio','Hora','Fecha Prueba','Estado de digitación','Digitado por')){if(-not ($headersInUi|Where-Object{$_.Current.Name -eq ('Ordenar por '+$name)})){throw ('Falta columna '+$name)}}
Capture-Window $window '02-job-real-vencido'
Passed 'JOB real con 24 pruebas vencidas, nueve columnas y estado de digitación de solo lectura'
Select-First;Keys '+({RIGHT}{RIGHT}{RIGHT}{DOWN}{DOWN}{DOWN}{DOWN})';Keys '^c'
$selection=[Windows.Forms.Clipboard]::GetText()
$expected=($before.items|Select-Object -First 5|ForEach-Object{$_.inDate+"`t"+$_.dueDate+"`t"+$_.job+"`t"+$_.originalTest}) -join "`r`n"
if($selection -ne $expected){throw 'El nuevo orden alteró el rango copiado.'}
$changedVersion=$null
try{
    Dossier;Invoke-Named 'Gestionar On Hold y etiqueta'
    Combo 'Estado operativo del JOB' 1
    Set-NamedValue 'Etiqueta del JOB' 'VALIDACION LOCAL'
    Set-NamedValue 'Motivo obligatorio del control' 'VALIDACION LOCAL de equivalencia HTML; no operativo; se restaurará el control previo'
    Invoke-Named 'Guardar control';Wait-Text 'Control del JOB guardado y auditado.'|Out-Null
    $persisted=Invoke-RestMethod -Uri ('https://localhost:7441/api/planning/jobs/'+$before.items[0].episodeId) -Headers $headers
    $changedVersion=$persisted.control.version
    if(-not $persisted.control.onHold -or $persisted.control.label -ne 'VALIDACION LOCAL'){throw 'On Hold/etiqueta no persistieron.'}
    if(-not ($persisted.history|Where-Object{$_.action -eq 'JobControlChanged'})){throw 'Falta historial del control.'}
    Capture-Window $window '03-expediente-on-hold';Close-Dossier
    $held=Api-Jobs ('Search='+[uri]::EscapeDataString($job));Assert-Counters $held
    if($held.counts.onHoldTests -ne 24 -or $held.counts.onHoldJobs -ne 1 -or $held.counts.pending -ne 0 -or $held.counts.overdue -ne 0 -or $held.counts.unknown -ne 24){throw 'Se mezclaron On Hold, pendientes o información de digitación.'}
    foreach($row in $held.items){$old=$before.items|Where-Object{$_.executionId -eq $row.executionId}|Select-Object -First 1;foreach($property in @('job','originalTest','equivalence','laboratory','digitizedAt','testedOn','digitizedBy','executionId')){if($old.$property -ne $row.$property){throw ('Cambio oficial indebido: '+$property)}}}
    (Find-Named 'Tabla de Consulta de Jobs').SetFocus();Keys '^c'
    if([Windows.Forms.Clipboard]::GetText() -ne $selection){throw 'Cambiar el control perdió el rango o contaminó la copia del JOB con la etiqueta.'}
    Start-Sleep -Milliseconds 5100;Capture-Window $window '04-tabla-on-hold'
    Passed 'On Hold y etiqueta guardados desde UI, auditados y releídos; pendientes/vencidas excluidos; fuente y rango conservados'
    & (Join-Path $htmlScriptsRoot 'validate-consulta-html-api.ps1') -Name ('on-hold-'+$Width)
    Set-NamedValue 'Buscar JOB' ''
    Invoke-Named 'Filtros';Combo 'Filtrar On Hold' 1;Combo 'Filtrar etiquetas' 1;Invoke-Named 'Aplicar filtros';Start-Sleep -Milliseconds 500
    $filtered=Api-Jobs 'OnHold=true&Tagged=true';Assert-Counters $filtered;Capture-Window $window '05-filtro-on-hold-etiquetas'
    Passed 'Consulta y contadores de On Hold/etiquetas se basan en controles persistidos y filtros del servidor'
    Invoke-Named 'Restablecer filtros';Start-Sleep -Milliseconds 400;Search-Job 'VALIDACION LOCAL'
    if((Api-Jobs 'Search=VALIDACION%20LOCAL').total -ne 24){throw 'El filtro JOB no encuentra etiquetas persistidas.'}
    Passed 'La búsqueda por JOB encuentra también su etiqueta, sin sustituir el número original'
}finally{
    $window=$main
    # Restore only our own version; never overwrite another actor's later decision.
    $now=Invoke-RestMethod -Uri ('https://localhost:7441/api/planning/jobs/'+$before.items[0].episodeId) -Headers $headers
    if($now.control.version -ne $original.version){
        $ours=if($changedVersion){$changedVersion}else{$original.version+1}
        if($now.control.version -ne $ours -or $now.control.label -ne 'VALIDACION LOCAL'){throw 'Otro actor modificó el control; se preserva su decisión y no se restaura automáticamente.'}
        $restore=@{version=$now.control.version;onHold=$original.onHold;label=$original.label;reason='VALIDACION LOCAL finalizada: restaurar control previo; no operativo'}|ConvertTo-Json
        Invoke-RestMethod -Uri ('https://localhost:7441/api/planning/jobs/'+$before.items[0].episodeId+'/control') -Method Put -ContentType application/json -Body $restore -Headers $headers|Out-Null
    }
}
Invoke-Named 'Restablecer filtros';Start-Sleep -Milliseconds 400;Search-Job $job
$restored=Api-Jobs ('Search='+[uri]::EscapeDataString($job));Assert-Counters $restored
if($restored.items[0].onHold -ne $original.onHold -or $restored.items[0].jobLabel -ne $original.label){throw 'El control no volvió al estado previo.'}
Passed 'Control operativo restaurado al estado previo, conservando auditoría de la validación local'
Invoke-Named 'Filtrar Prueba';Set-NamedValue 'Valor para filtrar Prueba' $before.items[0].originalTest;Invoke-Named 'Aplicar columna';Start-Sleep -Milliseconds 500
$column=@{OriginalTest=$before.items[0].originalTest}|ConvertTo-Json -Compress
$expected=Api-Jobs ('Search='+[uri]::EscapeDataString($job)+'&ColumnFilters='+[uri]::EscapeDataString($column));Assert-Counters $expected;Capture-Window $window '06-filtro-individual-prueba'
Invoke-Named 'Filtrar Prueba';Invoke-Named 'Limpiar columna';Start-Sleep -Milliseconds 400
Passed 'Filtro individual de Prueba aplicado en servidor y limpieza independiente'
Invoke-Named 'Filtrar Due Date';Invoke-Named 'Fecha para filtrar Due Date';Start-Sleep -Milliseconds 300
$calendar=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ClassNameProperty,'CalendarView'))|Where-Object{-not $_.Current.IsOffscreen}|Select-Object -First 1
if(-not $calendar){throw 'No se abrió un calendario nativo.'}
$day=[DateTime]::ParseExact($before.items[0].dueDate,'yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)
$today=[DateTime]::ParseExact($before.scope.today,'yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)
$months=($today.Year-$day.Year)*12+$today.Month-$day.Month
$direction=if($months -ge 0){'PreviousButton'}else{'NextButton'}
for($i=0;$i -lt [Math]::Abs($months);$i++){
    $move=$calendar.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$direction));$move.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke();Start-Sleep -Milliseconds 180
}
Capture-Window $window '07-calendario-abierto'
$target=$calendar.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ClassNameProperty,'CalendarViewDayItem'))|Where-Object{-not $_.Current.IsOffscreen -and $_.Current.Name -match ('^'+$day.Day+'(,|$)')}|Select-Object -First 1
if(-not $target){throw 'No se encontró el día solicitado en el calendario.'}
$invoke=$null;$select=$null
if($target.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern,[ref]$invoke)){$invoke.Invoke()}elseif($target.TryGetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern,[ref]$select)){$select.Select()}else{Click-Element $target}
Start-Sleep -Milliseconds 250
$calendarValue=(Find-Named 'Fecha para filtrar Due Date').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
$calendarValue=$calendarValue -replace '[\u200e\u200f\u2066-\u2069]',''
$selectedDate=[DateTime]::MinValue
if(-not [DateTime]::TryParseExact($calendarValue,[string[]]@('dd/MM/yyyy','d/M/yyyy','yyyy-MM-dd','M/d/yyyy'),[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::None,[ref]$selectedDate) -or $selectedDate.Date -ne $day.Date){throw "El calendario no confirmó la fecha solicitada: $calendarValue"}
Invoke-Named 'Aplicar columna';Start-Sleep -Milliseconds 400
$dateFilter=@{DueDate=$before.items[0].dueDate}|ConvertTo-Json -Compress
$calendarExpected=Api-Jobs ('Search='+[uri]::EscapeDataString($job)+'&ColumnFilters='+[uri]::EscapeDataString($dateFilter));Assert-Counters $calendarExpected
if($calendarExpected.total -ne 24){throw 'La fecha escogida no conserva el JOB fuente esperado.'}
Capture-Window $window '07b-calendario-aplicado';Passed 'Calendario nativo abre, navega meses, selecciona fecha exacta y aplica filtro en servidor'
Invoke-Named 'Filtrar Due Date';Invoke-Named 'Limpiar columna';Start-Sleep -Milliseconds 300
Search-Job $job;Select-First;Capture-Window $window '08-final'
@{Width=$Width;Height=$Height;Dpi=$dpi;SourceJob=$job;Initial=$before.counts;Restored=$restored.counts;Operational=$operational.counts;Passed=$checks.ToArray();ControlRestored=$true;Boundary='Native WinUI, authenticated isolated PostgreSQL, original HTML JavaScript oracle; locally held source JOB restored'}|ConvertTo-Json -Depth 7|Set-Content -LiteralPath (Join-Path $evidence 'acceptance.json') -Encoding UTF8
