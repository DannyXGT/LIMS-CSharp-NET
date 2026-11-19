param([int]$Scale=125,[int]$Width=1920,[int]$Height=1080)
$ErrorActionPreference='Stop'
$helpers=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'validate-planning-desktop.ps1'))
$start=$helpers.IndexOf('$ErrorActionPreference=')
$end=$helpers.IndexOf("if(`$Action -eq 'Inspect')")
$consultaScriptsRoot=$PSScriptRoot
Invoke-Expression $helpers.Substring($start,$end-$start).Replace('$PSScriptRoot','$consultaScriptsRoot')
$validationProcess=$window.Current.ProcessId
$window=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$validationProcess))|Where-Object{$_.Current.Name -eq 'LIMS'}|Select-Object -First 1
$evidence=Join-Path $local ('consulta-approved-'+$Width+'x'+$Height+'-'+$Scale)
New-Item -ItemType Directory -Path $evidence -Force|Out-Null
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class RefinementDpi {
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr w,IntPtr after,int x,int y,int width,int height,uint flags);
}
'@
[RefinementDpi]::SetThreadDpiAwarenessContext([IntPtr](-4))|Out-Null
$window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState([Windows.Automation.WindowVisualState]::Normal)
[RefinementDpi]::SetWindowPos([IntPtr]$window.Current.NativeWindowHandle,[IntPtr]::Zero,0,0,$Width,$Height,0x40)|Out-Null
Start-Sleep -Milliseconds 700
$frame=[PlanningCapture+Rect]::new();[PlanningCapture]::GetWindowRect([IntPtr]$window.Current.NativeWindowHandle,[ref]$frame)|Out-Null
if($frame.Right-$frame.Left -ne $Width -or $frame.Bottom-$frame.Top -ne $Height){throw 'La ventana no tiene el tamaño solicitado.'}
$dpi=[RefinementDpi]::GetDpiForWindow([IntPtr]$window.Current.NativeWindowHandle)
if($dpi -ne ($Scale*96/100)){throw "La ventana tiene $dpi DPI; no corresponde a $Scale %."}
$account=Get-Content -LiteralPath (Join-Path $local 'acceptance-account.json') -Raw|ConvertFrom-Json
$auth=Invoke-RestMethod -Uri https://localhost:7441/api/auth/login -Method Post -ContentType application/json -Body (@{identifier=$account.email;password=$account.password;clientName='Consulta visual regression'}|ConvertTo-Json)
$headers=@{Authorization='Bearer '+$auth.accessToken}
$source=Get-Content -LiteralPath (Join-Path $local 'acceptance-api.json') -Raw|ConvertFrom-Json
$job=$source.sourceJob
$checks=[Collections.Generic.List[string]]::new()
function Passed([string]$text){$checks.Add($text);Write-Output ('PASS: '+$text)}
function Keys([string]$keys){[Windows.Forms.SendKeys]::SendWait($keys);Start-Sleep -Milliseconds 200}
function Wait-Result($page){
    $total=$page.total;$counts=$page.counts
    $expected=('{0} {1} · {2} {3}' -f $counts.executions.ToString('N0',[Globalization.CultureInfo]::InvariantCulture),$(if($counts.executions -eq 1){'ejecución'}else{'ejecuciones'}),$counts.jobs.ToString('N0',[Globalization.CultureInfo]::InvariantCulture),$(if($counts.jobs -eq 1){'JOB'}else{'JOBs'}))
    $deadline=[DateTime]::UtcNow.AddSeconds(20)
    do{try{foreach($e in $window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)){if($e.Current.Name -eq $expected){return}}}catch [Windows.Automation.ElementNotAvailableException]{};Start-Sleep -Milliseconds 150}while([DateTime]::UtcNow -lt $deadline)
    throw "El total mostrado no coincide con API: $total"
}
function Target([int]$index){$combo=Find-Named 'Buscar por';$combo.SetFocus();Keys '{HOME}';if($index -gt 0){Keys ('{DOWN '+$index+'}')};Keys '{ENTER}'}
function Api-Jobs([string]$query){Invoke-RestMethod -Uri ('https://localhost:7441/api/planning/jobs?'+$query) -Headers $headers}
Target 0;Set-NamedValue 'Buscar JOB' $job;Invoke-Named 'Buscar'
$page=Api-Jobs ('Search='+[uri]::EscapeDataString($job));Wait-Result $page
$jobHeader=Find-Named 'Ordenar por JOB'
$jobCell=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|Where-Object{$_.Current.Name -like 'F1 C1 *'}|Select-Object -First 1
$padding=$jobCell.Current.BoundingRectangle.Left-$jobHeader.Current.BoundingRectangle.Left
Capture-Window $window 'consulta'
if([Math]::Abs($padding-10*$Scale/100) -gt 4){throw "Las filas no se alinean con el encabezado JOB: $padding px."}
Capture-Window $window 'consulta'
Passed 'DPI real y búsqueda JOB contrastada con API local autenticada'
Passed 'Filas alineadas con encabezados, incluso cuando cabe toda la tabla'
# A separate native window accepts the same clipboard text that Excel places on Windows.
Invoke-Named 'Búsqueda múltiple';Start-Sleep -Milliseconds 500
$main=$window
$window=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$main.Current.ProcessId))|Where-Object{$_.Current.Name -eq 'LIMS · Búsqueda múltiple'}|Select-Object -First 1
if(-not $window){throw 'La búsqueda múltiple no abrió una ventana independiente.'}
$input=Find-Named 'Lista de JOBs';[PlanningCapture]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle)|Out-Null;$input.SetFocus()
[Windows.Forms.Clipboard]::SetText($job+"`r`n"+$job+"`r`nUI-VALIDACION-NO-EXISTE");Keys '^v'
if($input.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -notlike ($job+'*'+$job+'*UI-VALIDACION-NO-EXISTE')){throw 'No se pegó la lista de Excel.'}
Invoke-Named 'Buscar JOBs'
$deadline=[DateTime]::UtcNow.AddSeconds(25);$result=$null
 do{$result=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|Where-Object{$_.Current.Name -like '1 encontrados · 1 no encontrados*'}|Select-Object -First 1;if(-not $result){Start-Sleep -Milliseconds 150}}while(-not $result -and [DateTime]::UtcNow -lt $deadline)
if(-not $result){throw 'La búsqueda múltiple no mostró encontrados y no encontrados.'}
Find-Named '2 JOBs únicos · 3 introducidos · 1 duplicados omitidos'|Out-Null
Capture-Window $window 'busqueda-multiple';Invoke-Named 'Cancelar';$window=$main;Wait-Result $page
Passed 'Ventana independiente, Ctrl+V de Excel, normalización, duplicados omitidos y JOBs no encontrados desde API'
Invoke-Named 'Restablecer filtros';Start-Sleep -Milliseconds 350
$cases=@(@{Index=1;Field='Test';Value=$page.items[0].originalTest},@{Index=2;Field='Equivalence';Value=($page.items|Where-Object{$_.equivalence}|Select-Object -First 1).equivalence},@{Index=3;Field='Laboratory';Value=$page.items[0].laboratory},@{Index=4;Field='Buyer';Value=$page.items[0].buyer})
foreach($case in $cases){
    if(-not $case.Value){throw "No hay valor real para comprobar $($case.Field)."}
    Target $case.Index;Set-NamedValue 'Buscar JOB' $case.Value;Invoke-Named 'Buscar'
    $expected=Api-Jobs ($case.Field+'='+[uri]::EscapeDataString($case.Value));Wait-Result $expected
    Passed ('Selector '+$case.Field+' consulta en servidor: '+$expected.total+' filas')
    Invoke-Named 'Restablecer filtros';Start-Sleep -Milliseconds 300
}
Set-NamedValue 'Buscar JOB' $job;Invoke-Named 'Buscar';Wait-Result $page
Invoke-Named 'Ordenar por Prueba original';Start-Sleep -Milliseconds 600
$ordered=Api-Jobs ('Search='+[uri]::EscapeDataString($job)+'&Sort=test')
$cell=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|Where-Object{$_.Current.Name -like 'F1 C4 *'}|Select-Object -First 1
if(-not $cell -or $cell.Current.Name -notlike ('*'+$ordered.items[0].originalTest)){
    Invoke-Named 'Ordenar por Prueba original';Start-Sleep -Milliseconds 800
    $cell=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|Where-Object{$_.Current.Name -like 'F1 C4 *'}|Select-Object -First 1
    if(-not $cell -or $cell.Current.Name -notlike ('*'+$ordered.items[0].originalTest)){throw 'El orden de prueba no coincide con API.'}
}
Capture-Window $window 'orden-prueba';Passed 'Ordenamiento desde encabezado coincide con servidor'
Invoke-Named 'Filtrar Laboratorio';Find-Named 'Filtros por columna'|Out-Null
Set-NamedValue 'Laboratorio' $page.items[0].laboratory
$apply=Find-Named 'Aplicar filtros'
if($apply.Current.BoundingRectangle.Height -lt (34*$Scale/100)){throw 'El botón de aplicar filtros está cortado.'}
Capture-Window $window 'filtros';Invoke-Named 'Aplicar filtros'
$filtered=Api-Jobs ('Search='+[uri]::EscapeDataString($job)+'&Laboratory='+[uri]::EscapeDataString($page.items[0].laboratory));Wait-Result $filtered
Passed 'Icono real de filtro de columna abre el panel y filtra en servidor'
Invoke-Named 'Restablecer filtros';Start-Sleep -Milliseconds 300;Set-NamedValue 'Buscar JOB' $job;Invoke-Named 'Buscar';Wait-Result $page
Invoke-Named 'Columnas';Start-Sleep -Milliseconds 250
$check=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::CheckBox))|Where-Object{$_.Current.Name -eq 'Equivalencia'}|Select-Object -First 1
$check.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle();Keys '{ESC}'
$header=$window.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'Ordenar por Equivalencia'))
if(-not $header){throw 'Mostrar Equivalencia no agregó la columna real.'}
Invoke-Named 'Columnas';Capture-Window $window 'columnas';Invoke-Named 'Restablecer columnas';Keys '{ESC}'
$header=$window.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'Ordenar por Equivalencia'))
if($header){throw 'Restablecer columnas no recuperó la distribución original.'}
Passed 'Mostrar y restablecer columnas actúa sobre la tabla real' 
Set-NamedValue 'Buscar JOB' 'SIN-RESULTADOS-VALIDACION-VISUAL';Invoke-Named 'Buscar';Wait-Result @{total=0;counts=@{executions=0;jobs=0}}
Find-Named 'Reintentar'|Out-Null;Capture-Window $window 'sin-resultados';Passed 'Estado vacío explicativo y reintento disponible'
Invoke-Named 'Restablecer filtros';Start-Sleep -Milliseconds 300;Set-NamedValue 'Buscar JOB' $job;Invoke-Named 'Buscar';Wait-Result $page
Invoke-Named 'Estado del JOB';Start-Sleep -Milliseconds 600
$window=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$main.Current.ProcessId))|Where-Object{$_.Current.Name -like ('Estado del JOB · '+$job)}|Select-Object -First 1
if(-not $window){throw 'No abrió el expediente.'}
Capture-Window $window 'expediente';Invoke-Named 'WIP';Invoke-Named 'Actualizar';Start-Sleep -Milliseconds 800
Find-Named 'Buyer / Marca'|Out-Null;Capture-Window $window 'expediente-wip-actualizado';Passed 'Actualizar expediente conserva la pestaña WIP'
Invoke-Named 'Cerrar';$window=$main
$buttons=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Button))|Where-Object{-not $_.Current.IsOffscreen}
foreach($name in @('Buscar','Búsqueda múltiple','Filtros','Columnas','Exportar CSV','Solo tabla','Estado del JOB')){if(@($buttons|Where-Object{$_.Current.Name -eq $name}).Count -ne 1){throw "Acción duplicada o ausente: $name"}}
$actions=@($buttons|Where-Object{$_.Current.Name -in @('Buscar','Búsqueda múltiple','Filtros','Columnas','Exportar CSV','Restablecer filtros','Solo tabla','Estado del JOB')})
for($i=0;$i -lt $actions.Count;$i++){
    $r=$actions[$i].Current.BoundingRectangle;$frame=$window.Current.BoundingRectangle
    if($r.Left -lt $frame.Left -or $r.Right -gt $frame.Right -or $r.Top -lt $frame.Top -or $r.Bottom -gt $frame.Bottom){throw 'Un botón está fuera de la ventana.'}
    for($j=$i+1;$j -lt $actions.Count;$j++){$other=$actions[$j].Current.BoundingRectangle;if($r.IntersectsWith($other)){throw 'Hay botones superpuestos.'}}
}
$bounds=@($buttons|Where-Object{$_.Current.Name -in @('Buscar','Búsqueda múltiple','Filtros','Columnas','Exportar CSV','Restablecer filtros','Solo tabla','Estado del JOB')}|ForEach-Object{@{Name=$_.Current.Name;Bounds=$_.Current.BoundingRectangle.ToString()}})
Passed 'Acciones visibles únicas, dentro de la ventana y sin superposición'
@{Passed=$checks.ToArray();Dpi=$dpi;Scale=$Scale;Width=$Width;Height=$Height;Boundary='Actual .NET WinUI, Windows clipboard and authenticated local PostgreSQL API; historical TIPS source';SourceJob=$job;Buttons=$bounds}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $evidence 'refinement.json') -Encoding UTF8
