param([ValidateSet('All','Selection','Paste','Edit','Status','Filters','Inspect')][string]$Step='All')
$ErrorActionPreference='Stop'
$helpers=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'validate-planning-desktop.ps1'))
$start=$helpers.IndexOf('$ErrorActionPreference=')
$end=$helpers.IndexOf("if(`$Action -eq 'Inspect')")
$consultaScriptsRoot=$PSScriptRoot
Invoke-Expression $helpers.Substring($start,$end-$start).Replace('$PSScriptRoot','$consultaScriptsRoot')
$processId=$window.Current.ProcessId
$roots=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$processId))
$window=$roots|Where-Object{$_.Current.Name -eq 'LIMS'}|Select-Object -First 1
if(-not $window){throw 'No se encontro la ventana principal LIMS del proceso de validacion.'}
$mainWindow=$window
$evidence=Join-Path $local 'consulta-evidence'
New-Item -ItemType Directory -Path $evidence -Force|Out-Null
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ConsultaInput {
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
}
'@
$process=Get-Process -Id $window.Current.ProcessId
$allowed=[IO.Path]::GetFullPath((Join-Path $workspace 'artifacts\planificacion-dev\desktop-validation-'))
if(-not $process.Path.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Solo se admite el ejecutable de validacion aislado.'}
$runtime=Get-Content -LiteralPath (Join-Path $local 'runtime.json') -Raw|ConvertFrom-Json
if($runtime.Host -ne '127.0.0.1' -or $runtime.Port -ne 55441 -or $runtime.Database -ne 'lims_planning_dev'){throw 'Base no autorizada.'}
$account=Get-Content -LiteralPath (Join-Path $local 'acceptance-account.json') -Raw|ConvertFrom-Json
$auth=Invoke-RestMethod -Uri https://localhost:7441/api/auth/login -Method Post -ContentType application/json -Body (@{identifier=$account.email;password=$account.password;clientName='Consulta UI acceptance'}|ConvertTo-Json)
$headers=@{Authorization='Bearer '+$auth.accessToken}
$source=Get-Content -LiteralPath (Join-Path $local 'acceptance-api.json') -Raw|ConvertFrom-Json
$job=$source.sourceJob
$runId=[DateTime]::UtcNow.ToString('HHmmssfff')
$checks=[Collections.Generic.List[string]]::new()
function Passed([string]$name){$checks.Add($name);@{Passed=$checks.ToArray();Boundary='Actual WinUI interaction + Windows clipboard + authenticated local API and PostgreSQL';SourceJob=$job;Database='lims_planning_dev'}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $evidence ('acceptance-'+$Step+'.json')) -Encoding UTF8;Write-Output ('PASS: '+$name)}
function Api-Jobs([string]$query){Invoke-RestMethod -Uri ('https://localhost:7441/api/planning/jobs?'+$query) -Headers $headers}
function Cell([int]$row,[int]$col){
    $prefix="F$row C$col "
    $deadline=[DateTime]::UtcNow.AddSeconds(15)
    do{
        $condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Text)
        foreach($e in $window.FindAll([Windows.Automation.TreeScope]::Descendants,$condition)){if($e.Current.Name.StartsWith($prefix) -and -not $e.Current.IsOffscreen){return $e}}
        Start-Sleep -Milliseconds 100
    }while([DateTime]::UtcNow -lt $deadline)
    throw "Celda visible no encontrada: $prefix"
}
function Click-Element($element,[switch]$Double){
    $r=$element.Current.BoundingRectangle
    [PlanningCapture]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle)|Out-Null
    [ConsultaInput]::SetCursorPos([int]($r.Left+[Math]::Min(30,$r.Width/2)),[int]($r.Top+$r.Height/2))|Out-Null
    [ConsultaInput]::mouse_event(2,0,0,0,[UIntPtr]::Zero);[ConsultaInput]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
    if($Double){Start-Sleep -Milliseconds 90;[ConsultaInput]::mouse_event(2,0,0,0,[UIntPtr]::Zero);[ConsultaInput]::mouse_event(4,0,0,0,[UIntPtr]::Zero)}
    Start-Sleep -Milliseconds 180
}
function Keys([string]$keys){[Windows.Forms.SendKeys]::SendWait($keys);Start-Sleep -Milliseconds 200}
function Wait-Text([string]$pattern,[int]$seconds=20){
    $deadline=[DateTime]::UtcNow.AddSeconds($seconds)
    do{
        try{foreach($e in $window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)){if(-not $e.Current.IsOffscreen -and $e.Current.Name -like $pattern){return $e}}}catch{Start-Sleep -Milliseconds 100;continue}
        Start-Sleep -Milliseconds 100
    }while([DateTime]::UtcNow -lt $deadline)
    Inspect-Window;throw "No aparecio el estado: $pattern"
}
function Search-Job([string]$text){Set-NamedValue 'Buscar JOB' $text;Invoke-Named 'Buscar';Wait-Text 'Resultados completos filtrados*'|Out-Null;Start-Sleep -Milliseconds 400}
function Select-First{
    $list=Find-Named 'Tabla de Consulta de Jobs';$scroll=$null
    if($list.TryGetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern,[ref]$scroll) -and $scroll.Current.VerticallyScrollable){$scroll.SetScrollPercent(-1,0);Start-Sleep -Milliseconds 200}
    Click-Element (Cell 1 1)
}
if($Step -eq 'Inspect'){Inspect-Window;exit 0}
Search-Job $job
$before=Api-Jobs ('Search='+[uri]::EscapeDataString($job)+'&PageSize=100')
if($before.items.Count -lt 10){throw 'El JOB real debe tener al menos 10 ejecuciones para esta aceptacion.'}
Capture-Window $window '01-consulta-real'
Passed 'Consulta dentro del AppShell original y JOB fuente consultado'
if($Step -in @('All','Selection')){
    Select-First
    Keys '^c'
    if([Windows.Forms.Clipboard]::GetText() -ne $job){throw 'Copia de una celda incorrecta.'}
    Passed 'Una celda copiada exactamente'
    Keys '+({RIGHT}{RIGHT}{RIGHT}{DOWN}{DOWN}{DOWN}{DOWN})'
    Wait-Text 'Selección: 5 fila(s) × 4 columna(s)*'|Out-Null
    Keys '^c'
    $copied=[Windows.Forms.Clipboard]::GetText()
    $expected=($before.items|Select-Object -First 5|ForEach-Object{$_.job+"`t"+$_.inDate+"`t"+$_.dueDate+"`t"+$_.originalTest}) -join "`r`n"
    if($copied -ne $expected){throw 'El contenido tabulado no corresponde exactamente al rango 5 x 4.'}
    [IO.File]::WriteAllText((Join-Path $evidence 'seleccion-5x4.tsv'),$copied,[Text.UTF8Encoding]::new($false))
    Capture-Window $window '02-seleccion-5x4'
    Passed 'Shift + flechas, rango 5 x 4 y Ctrl+C exacto en portapapeles'
    Select-First;Keys '^ ';Keys '^c'
    if(([Windows.Forms.Clipboard]::GetText() -split '\r?\n').Count -ne $before.items.Count){throw 'La columna completa no coincide con las filas cargadas.'}
    Passed 'Copia de columna completa de la pagina cargada'
    Select-First
    $first=(Cell 1 1).Current.BoundingRectangle;$last=(Cell 5 4).Current.BoundingRectangle
    [ConsultaInput]::SetCursorPos([int]($first.Left+20),[int]($first.Top+10))|Out-Null
    [ConsultaInput]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds 100
    $screen=[Windows.Forms.Screen]::PrimaryScreen.Bounds
    for($i=1;$i -le 12;$i++){
        $x=$first.Left+20+($last.Left-$first.Left)*$i/12;$y=$first.Top+10+($last.Top-$first.Top)*$i/12
        [ConsultaInput]::mouse_event(0x8001,[uint32]($x*65535/($screen.Width-1)),[uint32]($y*65535/($screen.Height-1)),0,[UIntPtr]::Zero)
        Start-Sleep -Milliseconds 50
    }
    [ConsultaInput]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
    Wait-Text 'Selección: 5 fila(s) × 4 columna(s)*'|Out-Null
    Keys '^c';if([Windows.Forms.Clipboard]::GetText() -ne $expected){throw 'El rango arrastrado no coincide con el copiado esperado.'}
    Passed 'Arrastre rectangular real con raton'
    Select-First
    # Native key state is held across the pointer click for Shift + click.
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ConsultaShift { [DllImport("user32.dll")] public static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra); }
'@
    [ConsultaShift]::keybd_event(0x10,0,0,[UIntPtr]::Zero)
    try{Click-Element (Cell 5 4)}finally{[ConsultaShift]::keybd_event(0x10,0,2,[UIntPtr]::Zero)}
    Keys '^c';if([Windows.Forms.Clipboard]::GetText() -ne $expected){throw 'Shift + clic no amplio el rango.'}
    Passed 'Shift + clic amplia el rango'
    $point=(Cell 2 2).Current.BoundingRectangle
    [ConsultaInput]::SetCursorPos([int]($point.Left+20),[int]($point.Top+10))|Out-Null
    [ConsultaInput]::mouse_event(8,0,0,0,[UIntPtr]::Zero);[ConsultaInput]::mouse_event(16,0,0,0,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds 200
    $menu=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::MenuItem))|Where-Object{$_.Current.Name -eq 'Copiar selección'}|Select-Object -First 1
    if(-not $menu){throw 'No se abrio el menu contextual de celdas.'}
    $menu.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    if([Windows.Forms.Clipboard]::GetText() -ne $expected){throw 'La copia del menu contextual no coincide.'}
    Passed 'Copiado desde menu contextual conserva el rango'
    Select-First
    $first=(Cell 1 1).Current.BoundingRectangle;$table=(Find-Named 'Tabla de Consulta de Jobs').Current.BoundingRectangle
    [ConsultaInput]::SetCursorPos([int]($first.Left+20),[int]($first.Top+10))|Out-Null;[ConsultaInput]::mouse_event(2,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 100
    $x=$first.Left+20;$y=$table.Bottom-8
    [ConsultaInput]::mouse_event(0x8001,[uint32]($x*65535/($screen.Width-1)),[uint32]($y*65535/($screen.Height-1)),0,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds 1100
    [ConsultaInput]::mouse_event(4,0,0,0,[UIntPtr]::Zero);Keys '^c'
    if(([Windows.Forms.Clipboard]::GetText() -split '\r?\n').Count -le 17){throw 'La seleccion no se extendio con desplazamiento automatico al borde.'}
    Passed 'Seleccion con desplazamiento automatico hacia filas no visibles'
}
if($Step -in @('All','Paste')){
    Select-First;Keys '{RIGHT 11}'
    $values=1..10|ForEach-Object{'VALIDACION LOCAL Consulta '+$_+' '+$runId+' - no operativo'}
    [Windows.Forms.Clipboard]::SetText(($values -join "`r`n"))
    Keys '^v'
    Wait-Text 'Inicio F1 C12*10 válidos*0 inválidos*0 omitidos'|Out-Null
    Capture-Window $window '03-pegado-revision'
    Invoke-Named 'Confirmar cambios válidos'
    Wait-Text 'Guardado: 10 celda(s)*'|Out-Null
    $saved=Api-Jobs ('Search='+[uri]::EscapeDataString($job)+'&PageSize=100')
    for($i=0;$i -lt 10;$i++){if($saved.items[$i].observation -ne $values[$i]){throw 'El pegado no persistio en PostgreSQL.'}}
    for($i=0;$i -lt $before.items.Count;$i++){foreach($property in @('job','inDate','dueDate','originalTest','equivalence','laboratory','digitizedAt','digitizedBy','testedOn','executionId')){if($before.items[$i].$property -ne $saved.items[$i].$property){throw "Cambio en columna protegida: $property"}}}
    @{Count=10;Column='Observation';Values=$values;ExecutionIds=@($saved.items|Select-Object -First 10|ForEach-Object{$_.executionId});VerifiedAgainst='Authenticated local API re-read after UI confirmation';ProtectedColumnsUnchanged=$true}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $evidence 'pegado-persistido.json') -Encoding UTF8
    Capture-Window $window '04-pegado-persistido'
    Passed 'Ctrl+V de 10 valores, revision, confirmacion y persistencia; columnas importadas intactas'
    Select-First;[Windows.Forms.Clipboard]::SetText('NO CAMBIAR JOB');Keys '^v';Wait-Text 'Inicio F1 C1*0 válidos*1 omitidos'|Out-Null;Invoke-Named 'Cancelar';Start-Sleep -Milliseconds 500
    Passed 'Pegado sobre JOB protegido omitido y cancelable'
}
if($Step -in @('All','Edit')){
    Select-First;Keys '{RIGHT 11}';Click-Element (Cell 1 12) -Double
    Find-Named 'Editar Observación'|Out-Null
    Set-NamedValue 'Valor de Observación' 'ESTO SE CANCELA'
    (Find-Named 'Valor de Observación').SetFocus();Keys '{ESC}'
    $unchanged=Api-Jobs ('Search='+[uri]::EscapeDataString($job)+'&PageSize=100')
    if($unchanged.items[0].observation -eq 'ESTO SE CANCELA'){throw 'Escape guardo indebidamente.'}
    Passed 'Doble clic editable y Escape sin escritura'
    Click-Element (Cell 1 12) -Double
    $committed='VALIDACION LOCAL Enter '+$runId+' - no operativo'
    Set-NamedValue 'Valor de Observación' $committed
    (Find-Named 'Valor de Observación').SetFocus();Keys '{ENTER}'
    Wait-Text 'Guardado: 1 celda(s)*'|Out-Null
    $saved=Api-Jobs ('Search='+[uri]::EscapeDataString($job)+'&PageSize=100')
    if($saved.items[0].observation -ne $committed){throw 'Enter no persistio.'}
    Passed 'Edicion individual con Enter persistida y auditada'
    Click-Element (Cell 1 12) -Double
    $tabValue='VALIDACION LOCAL Tab '+$runId+' - no operativo'
    Set-NamedValue 'Valor de Observación' $tabValue
    (Find-Named 'Valor de Observación').SetFocus();Keys '{TAB}'
    Find-Named 'Editar Responsable'|Out-Null
    Keys '{ESC}';Start-Sleep -Milliseconds 400
    $saved=Api-Jobs ('Search='+[uri]::EscapeDataString($job)+'&PageSize=100')
    if($saved.items[0].observation -ne $tabValue){throw 'Tab no guardo la celda antes de continuar.'}
    Passed 'Tab guarda y continua a la siguiente celda editable'
}
if($Step -in @('All','Status')){
    Select-First;Keys '+({RIGHT}{RIGHT}{RIGHT}{DOWN}{DOWN}{DOWN}{DOWN})';Keys '^c';$selection=[Windows.Forms.Clipboard]::GetText()
    Invoke-Named 'Estado del JOB'
    $deadline=[DateTime]::UtcNow.AddSeconds(15);$dossier=$null
    do{foreach($candidate in [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$mainWindow.Current.ProcessId))){if($candidate.Current.Name -like ('*'+$job+'*')){$dossier=$candidate;break}};if(-not $dossier){Start-Sleep -Milliseconds 100}}while(-not $dossier -and [DateTime]::UtcNow -lt $deadline)
    if(-not $dossier){throw 'Estado del JOB no abrio en una ventana independiente.'}
    $window=$dossier;Find-Named 'Copiar estado para Teams'|Out-Null;Start-Sleep -Milliseconds 500
    Capture-Window $window '05-estado-job-resumen'
    Invoke-Named 'Pruebas';Find-Named 'Prueba original'|Out-Null;Start-Sleep -Milliseconds 300;Capture-Window $window '06-estado-job-pruebas'
    Invoke-Named 'WIP';Find-Named 'Buyer / Marca'|Out-Null;Start-Sleep -Milliseconds 300;Capture-Window $window '07-estado-job-wip'
    Invoke-Named 'Historial';Wait-Text 'Historial · últimos 1000*'|Out-Null;Start-Sleep -Milliseconds 300;Capture-Window $window '08-estado-job-historial'
    Invoke-Named 'Copiar estado para Teams';$teams=[Windows.Forms.Clipboard]::GetText()
    if($teams -notlike ('JOB: '+$job+'*') -or $teams -notlike '*Estado oficial: No verificado*'){throw 'Resumen para Teams incorrecto.'}
    [IO.File]::WriteAllText((Join-Path $evidence 'estado-para-teams.txt'),$teams,[Text.UTF8Encoding]::new($false))
    $dossier.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close();$window=$mainWindow
    [PlanningCapture]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle)|Out-Null
    (Find-Named 'Tabla de Consulta de Jobs').SetFocus();Keys '^c'
    if([Windows.Forms.Clipboard]::GetText() -ne $selection){throw 'La seleccion se perdio al cerrar Estado del JOB.'}
    Passed 'Estado no modal, pruebas, WIP, historial y copia Teams; cierre conserva seleccion'
    Keys '{ENTER}'
    Start-Sleep -Milliseconds 500
    $keyboardDossier=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$mainWindow.Current.ProcessId))|Where-Object{$_.Current.Name -like ('*'+$job+'*')}|Select-Object -First 1
    if(-not $keyboardDossier){throw 'Enter con foco en tabla no abrio Estado del JOB.'}
    $keyboardDossier.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
    Passed 'Enter sobre la tabla abre Estado del JOB'
    Invoke-Named 'Solo tabla';Find-Named 'Recuperar herramientas'|Out-Null;Capture-Window $window '09-solo-tabla';Invoke-Named 'Recuperar herramientas';Find-Named 'Buscar JOB'|Out-Null
    Passed 'Solo tabla y recuperacion de herramientas'
}
if($Step -in @('All','Filters')){
    Invoke-Named 'Limpiar';Wait-Text 'Página 1*'|Out-Null;Start-Sleep -Milliseconds 400
    $all=Api-Jobs 'PageSize=100';$second=$all.items|Where-Object{$_.job -ne $job}|Select-Object -First 1
    if(-not $second){$second=(Api-Jobs 'Page=2&PageSize=100').items|Select-Object -First 1}
    Search-Job ($job+"`n"+$second.job)
    $multi=Api-Jobs ('Search='+[uri]::EscapeDataString($job+"`n"+$second.job))
    Wait-Text ('Página 1* / '+('{0:N0}' -f $multi.total)+' filas')|Out-Null
    Passed 'Busqueda multiple de JOBs con saltos de linea'
    Invoke-Named 'Limpiar';Start-Sleep -Milliseconds 500;Invoke-Named 'Siguiente';Wait-Text 'Página 2*'|Out-Null
    $page2=Api-Jobs 'Page=2&PageSize=100'
    Select-First;Keys '^c';if([Windows.Forms.Clipboard]::GetText() -ne $page2.items[0].job){throw 'Paginacion no coincide con el servidor.'}
    Passed 'Paginacion del servidor con registros de otra pagina'
    Invoke-Named 'Filtros';Set-NamedValue 'Ingreso desde · yyyy-MM-dd' '2040-01-01';Invoke-Named 'Aplicar filtros';Wait-Text 'Sin resultados.'|Out-Null
    Invoke-Named 'Limpiar';Wait-Text 'Resultados completos filtrados*'|Out-Null
    Passed 'Filtro de ingreso actua sobre consulta completa y puede limpiarse'
}
Search-Job $job
$table=Find-Named 'Tabla de Consulta de Jobs'
if($table.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::CheckBox)).Count -ne 0){throw 'Se encontraron checks dentro de Consulta.'}
Passed 'Tabla sin checks ni controles de marcado de digitacion'
Capture-Window $window '10-consulta-final'
Write-Output ('Aceptacion UI completada: '+$checks.Count+' comprobaciones.')
