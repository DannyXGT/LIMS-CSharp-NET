param([ValidateSet('Overview','Inspect','Selection','Save','Groups','Detail','Review','Closures','Consulta','History','Multi','Volume','ConsultaReview','ConfirmCancel','Rapid','Export')][string]$Step='Overview',[string]$SavedExportPath)
$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'validate-planning-desktop.ps1'));$scheduleScriptsRoot=$PSScriptRoot
$begin=$source.IndexOf('$ErrorActionPreference=');$end=$source.IndexOf("if(`$Action -eq 'Inspect')")
Invoke-Expression $source.Substring($begin,$end-$begin).Replace('$PSScriptRoot','$scheduleScriptsRoot')
$process=Get-Process -Id $window.Current.ProcessId
if(-not $process.Path.StartsWith((Join-Path $workspace 'artifacts/planificacion-dev/desktop-validation-'),[StringComparison]::OrdinalIgnoreCase)){throw 'Only isolated validation client.'}
$evidence=Join-Path $workspace 'artifacts/report-schedules/ui';New-Item -ItemType Directory -Path $evidence -Force|Out-Null
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class ScheduleGeometry {
 [DllImport("user32.dll")]public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
 [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int w,int v,uint f);
}
'@
[ScheduleGeometry]::SetThreadDpiAwarenessContext([IntPtr](-4))|Out-Null
function Capture([string]$name){
 $handle=[IntPtr]$window.Current.NativeWindowHandle;$rect=[PlanningCapture+Rect]::new();[PlanningCapture]::GetWindowRect($handle,[ref]$rect)|Out-Null
 [PlanningCapture]::SetForegroundWindow($handle)|Out-Null
 [ScheduleGeometry]::SetWindowPos($handle,[IntPtr]::Zero,$rect.Left,$rect.Top,$rect.Right-$rect.Left-1,$rect.Bottom-$rect.Top,0x14)|Out-Null;Start-Sleep -Milliseconds 150
 [ScheduleGeometry]::SetWindowPos($handle,[IntPtr]::Zero,$rect.Left,$rect.Top,$rect.Right-$rect.Left,$rect.Bottom-$rect.Top,0x14)|Out-Null;Start-Sleep -Milliseconds 500
 Capture-Window $window $name
}
function Combo([string]$name,[string]$item){
 $combo=Find-Named $name;$expand=$combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern);$expand.Expand()
 (Find-Named $item).GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();$expand.Collapse();Start-Sleep -Milliseconds 250
}
function Items([string]$name){
 $list=Find-Named $name
 @($list.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem)))
}
if($Step -eq 'Inspect'){Inspect-Window;Capture 'current';exit}
if($Step -eq 'Overview'){
 Invoke-Named 'Report List / Digitación';Invoke-Named 'Digitación';Set-NamedValue 'Buscar JOB en Report List' '';Combo 'Vista' 'Todas';Invoke-Named 'Buscar Report List';Start-Sleep -Milliseconds 700
 if(@(Items 'Ejecuciones de Report List').Count -lt 4){throw 'Real rows not loaded'};Capture '01-report-list';Inspect-Window
}
if($Step -eq 'Selection'){
 Invoke-Named 'Report List / Digitación';Invoke-Named 'Digitación';Set-NamedValue 'Buscar JOB en Report List' 'GUAT26022934';Combo 'Vista' 'Todas'
 Invoke-Named 'Filtros de Report List';Set-NamedValue 'Prueba de Planificación' 'PH';Set-NamedValue 'Laboratorio' 'Chemical';Invoke-Named 'Aplicar filtros';Start-Sleep -Milliseconds 500
 $items=@(Items 'Ejecuciones de Report List');if($items.Count -ne 2){throw 'Expected two real PH rows.'}
 $chosen=$items|Where-Object{$_.Current.Name -notlike '*Digitado*'}|Select-Object -First 1
 if(-not $chosen){throw 'No pending PH available'}
 $chosen.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
 Set-NamedValue 'Fecha de digitación' '2026-11-20';Set-NamedValue 'Hora de digitación' '14:54'
 Capture '02-seleccion-fecha-hora';Inspect-Window
}
if($Step -eq 'Save'){
 Invoke-Named 'Guardar digitación';Find-Named 'Confirmar y guardar'|Out-Null;Capture '03-confirmacion';Invoke-Named 'Confirmar y guardar';Start-Sleep -Milliseconds 900
 $items=@(Items 'Ejecuciones de Report List');if(@($items|Where-Object{$_.Current.Name -like '*Digitado*'}).Count -ne 1){throw 'Expected one digitized and one pending PH'}
 Capture '04-digitacion-guardada';Inspect-Window
}
if($Step -eq 'Groups'){
 Invoke-Named 'Report List / Digitación';Invoke-Named 'Chequeo de horarios';Set-NamedValue 'Buscar JOB en Report List' '';Invoke-Named 'Buscar Report List';Start-Sleep -Milliseconds 600
 if(@(Items 'Horarios de digitación').Count -lt 1){throw 'No persisted schedules'};Capture '05-chequeo-horarios';Inspect-Window
}
if($Step -eq 'Detail'){
 $items=@(Items 'Horarios de digitación');$chosen=$items|Where-Object{$_.Current.Name -like '*2 pruebas*'}|Select-Object -First 1
 if(-not $chosen){$chosen=$items|Select-Object -First 1}
 $chosen.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();Find-Named 'Chequear horario'|Out-Null;Start-Sleep -Milliseconds 350;Capture '06-detalle-horario';Inspect-Window
}
if($Step -eq 'Review'){
 Invoke-Named 'Chequear horario';Invoke-Named 'Confirmar';Start-Sleep -Milliseconds 700;Capture '07-horario-chequeado';Inspect-Window
}
if($Step -eq 'History'){Invoke-Named 'Consultar historial';Capture '08-historial';Inspect-Window}
if($Step -eq 'Closures'){Invoke-Named 'Cierres';Start-Sleep -Milliseconds 400;Capture '09-cierres';Inspect-Window}
if($Step -eq 'Consulta'){
 Invoke-Named 'Consulta de Jobs';Set-NamedValue 'Buscar JOB' 'GUAT26022934';Invoke-Named 'Buscar';Start-Sleep -Milliseconds 600;Capture '10-consulta';Inspect-Window
}
if($Step -eq 'Multi'){
 Invoke-Named 'Búsqueda múltiple de Report List';Find-Named 'JOBs de búsqueda múltiple'|Out-Null;Set-NamedValue 'JOBs de búsqueda múltiple' "GUAT26022934`r`nGUAT26022855";Capture '11-busqueda-multiple';Invoke-Named 'Cancelar'
}
if($Step -eq 'ConfirmCancel'){Invoke-Named 'Guardar digitación';Find-Named 'Confirmar y guardar'|Out-Null;Capture '03-confirmacion';Invoke-Named 'Cancelar'}
if($Step -eq 'Volume'){
 Invoke-Named 'Report List / Digitación';Invoke-Named 'Digitación';Set-NamedValue 'Buscar JOB en Report List' '';Invoke-Named 'Filtros de Report List';Invoke-Named 'Limpiar filtros';Combo 'Vista' 'Todas';Invoke-Named 'Buscar Report List';Start-Sleep -Milliseconds 500
 $timer=[Diagnostics.Stopwatch]::StartNew();$loaded=0
 for($i=0;$i -lt 12 -and $loaded -lt 2000;$i++){
  $list=Find-Named 'Ejecuciones de Report List';$scroll=$list.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern);$scroll.SetScrollPercent(-1,100);Start-Sleep -Milliseconds 650
  $status=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|Where-Object{$_.Current.Name -match '^[\d,.]+ de [\d,.]+ ejecuciones'}|Select-Object -Last 1)
  if($status.Count){$loaded=[int]([regex]::Match($status[0].Current.Name,'^[\d,.]+').Value -replace '[,.]','')}
 }
 if($loaded -lt 2000){throw 'Native incremental scrolling did not reach 2000 real executions'}
 $visible=@(Items 'Ejecuciones de Report List'|Where-Object{-not $_.Current.IsOffscreen}).Count
 Capture '12-volumen-nativo';@{Passed=$true;Loaded=$loaded;VisibleRows=$visible;ElapsedMs=$timer.Elapsed.TotalMilliseconds;Boundary='Native incremental scrolling with actual restored executions'}|ConvertTo-Json|Set-Content (Join-Path (Split-Path $evidence) 'native-scroll-volume.json') -Encoding UTF8
}
if($Step -eq 'ConsultaReview'){
 Invoke-Named 'Consulta de Jobs';Set-NamedValue 'Buscar JOB' 'GUAT26022934';Invoke-Named 'Buscar';Invoke-Named 'Columnas';Find-Named 'Columnas de Consulta'|Out-Null
 $desired=@('JOB','Prueba','Hora','Fecha Prueba','Estado de digitación','Digitado por','Chequeo de horario','Ejecución')
 $checks=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::CheckBox)))
 foreach($check in $checks){if(-not $check.Current.IsEnabled){continue};$toggle=$null;if($check.TryGetCurrentPattern([Windows.Automation.TogglePattern]::Pattern,[ref]$toggle)){$wanted=$desired -contains $check.Current.Name;$on=$toggle.Current.ToggleState -eq [Windows.Automation.ToggleState]::On;if($wanted -ne $on){$toggle.Toggle()}}}
 $bounds=(Find-Named 'Buscar JOB').Current.BoundingRectangle
 [PlanningCapture]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle)|Out-Null
 [PlanningCapture]::SetCursorPos([int]($bounds.Left+$bounds.Width/2),[int]($bounds.Top+$bounds.Height/2))|Out-Null
 [PlanningCapture]::mouse_event(2,0,0,0,[UIntPtr]::Zero);[PlanningCapture]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
 Start-Sleep -Milliseconds 500;Capture '10-consulta-chequeo';Inspect-Window
 $names=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|ForEach-Object{$_.Current.Name})
 if(-not($names|Where-Object{$_ -like '*Chequeado*'})){throw 'Consulta does not expose the persisted review state'}
}
if($Step -eq 'Rapid'){
 $items=@(Items 'Horarios de digitación');if($items.Count -lt 3){throw 'Need real sessions for cancellation check'}
 for($i=0;$i -lt 15;$i++){$items[$i%$items.Count].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()}
 Start-Sleep -Milliseconds 700;Find-Named 'Pruebas del horario'|Out-Null
 if((Get-Process -Id $process.Id -ErrorAction Stop).HasExited){throw 'Rapid schedule changes terminated the native client'}
 Capture '13-cambio-rapido-horarios'
}
if($Step -eq 'Export'){
 if($SavedExportPath){
  Find-Named 'Chequeados exportados.'|Out-Null
  $file=Get-Item -LiteralPath $SavedExportPath;$rows=@(Import-Csv -LiteralPath $SavedExportPath -Encoding UTF8)
  if($rows.Count -ne 3 -or @($rows|Where-Object{-not $_.'Chequeado por'}).Count -gt 0){throw 'Unexpected checked export'}
  Capture '14-exportacion-chequeados';@{Passed=$true;Rows=$rows.Count;Source=$file.FullName;Sha256=(Get-FileHash $file.FullName).Hash;SavePicker='Completed separately; actual file and native success state verified'}|ConvertTo-Json|Set-Content (Join-Path (Split-Path $evidence) 'native-checked-export.json') -Encoding UTF8
  exit
 }
 Invoke-Named 'Exportar chequeados';$deadline=[DateTime]::UtcNow.AddSeconds(25);$dialog=$null
 do{
  foreach($candidate in @($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|Where-Object{$_.Current.ClassName -eq '#32770'})){
   $edit=@($candidate.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|Where-Object{$_.Current.ControlType -eq [Windows.Automation.ControlType]::Edit -and $_.Current.Name -eq 'Nombre de archivo:'})|Select-Object -First 1
   if($edit){$dialog=$candidate;break}
  };if(-not $dialog){Start-Sleep -Milliseconds 150}
 }while(-not $dialog -and [DateTime]::UtcNow -lt $deadline)
 if(-not $dialog){throw 'Native CSV save picker did not open'}
 $input=$edit.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Edit));if(-not $input){$input=$edit}
 $path=Join-Path (Split-Path $evidence) ('native-checked-'+[Guid]::NewGuid().ToString('N')+'.csv')
 $input.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($path)
 $save=$dialog.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|Where-Object{$_.Current.AutomationId -eq '1' -and $_.Current.NativeWindowHandle -ne 0}|Select-Object -First 1
 if(-not $save){throw 'Native Save button unavailable'}
 [PlanningCapture]::SendMessage([IntPtr]$save.Current.NativeWindowHandle,0xF5,[IntPtr]::Zero,[IntPtr]::Zero)|Out-Null
 $deadline=[DateTime]::UtcNow.AddSeconds(20);do{if((Test-Path -LiteralPath $path) -and (Get-Item -LiteralPath $path).Length -gt 0){break};Start-Sleep -Milliseconds 150}while([DateTime]::UtcNow -lt $deadline)
 if(-not(Test-Path -LiteralPath $path)){throw 'Native CSV was not saved'}
 $rows=@(Import-Csv -LiteralPath $path -Encoding UTF8);if($rows.Count -ne 3){throw 'Expected the three actually checked tests; corrected group must be absent'}
 if($rows|Where-Object{-not $_.'Chequeado por'}){throw 'Checked export missing reviewer'}
 Find-Named 'Chequeados exportados.'|Out-Null;Capture '14-exportacion-chequeados'
 @{Passed=$true;Rows=$rows.Count;Path=$path;Sha256=(Get-FileHash $path).Hash;Boundary='Native save picker and actual CSV from authenticated API'}|ConvertTo-Json|Set-Content (Join-Path (Split-Path $evidence) 'native-checked-export.json') -Encoding UTF8
}
@{Step=$Step;Passed=$true;Boundary='Authenticated native WinUI on restored development database';Utc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json|Set-Content (Join-Path (Split-Path $evidence) ('native-'+$Step+'.json')) -Encoding UTF8
