param([ValidateSet('Overview','Repeated','Cells','Layout','Prepare','Select','Save','Groups','Detail','Review','Verify','References','Inspect','ClockCombined','Filters','Volume')][string]$Step='Overview')
$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'validate-planning-desktop.ps1'));$correctionRoot=$PSScriptRoot
$begin=$source.IndexOf('$ErrorActionPreference=');$end=$source.IndexOf("if(`$Action -eq 'Inspect')")
Invoke-Expression $source.Substring($begin,$end-$begin).Replace('$PSScriptRoot','$correctionRoot')
$process=Get-Process -Id $window.Current.ProcessId
if(-not $process.Path.StartsWith((Join-Path $workspace 'artifacts/planificacion-dev/desktop-validation-'),[StringComparison]::OrdinalIgnoreCase)){throw 'Only isolated validation client'}
$out=Join-Path $workspace 'artifacts/report-list-correction';$evidence=Join-Path $out 'ui';New-Item -ItemType Directory -Path $evidence -Force|Out-Null
$r=Get-Content (Join-Path $local 'runtime.json') -Raw|ConvertFrom-Json
if($r.Host -ne '127.0.0.1' -or $r.Port -ne 55441 -or $r.Database -ne 'lims_planning_dev' -or -not $r.Restored){throw 'Only restored local database'}
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class ReportCorrectionDpi {
 [DllImport("user32.dll")]public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);
 [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int w,int v,uint f);
}
'@
[ReportCorrectionDpi]::SetThreadDpiAwarenessContext([IntPtr](-4))|Out-Null
function Combo([string]$name,[string]$item){$combo=Find-Named $name;$expand=$combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern);$expand.Expand();(Find-Named $item).GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();$expand.Collapse();Start-Sleep -Milliseconds 200}
function Items([string]$name){@((Find-Named $name).FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem)))}
function Click-Point($element,[switch]$Right){$b=$element.Current.BoundingRectangle;[PlanningCapture]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle)|Out-Null;[PlanningCapture]::SetCursorPos([int]($b.Left+$b.Width/2),[int]($b.Top+$b.Height/2))|Out-Null;$down=if($Right){8}else{2};$up=if($Right){16}else{4};[PlanningCapture]::mouse_event($down,0,0,0,[UIntPtr]::Zero);[PlanningCapture]::mouse_event($up,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 150}
function Capture([string]$name){$h=[IntPtr]$window.Current.NativeWindowHandle;$rect=[PlanningCapture+Rect]::new();[PlanningCapture]::GetWindowRect($h,[ref]$rect)|Out-Null;[PlanningCapture]::SetForegroundWindow($h)|Out-Null;[ReportCorrectionDpi]::SetWindowPos($h,[IntPtr]::Zero,$rect.Left,$rect.Top,$rect.Right-$rect.Left-1,$rect.Bottom-$rect.Top,0x14)|Out-Null;Start-Sleep -Milliseconds 120;[ReportCorrectionDpi]::SetWindowPos($h,[IntPtr]::Zero,$rect.Left,$rect.Top,$rect.Right-$rect.Left,$rect.Bottom-$rect.Top,0x14)|Out-Null;Start-Sleep -Milliseconds 300;Capture-Window $window $name}
function Connect-Api{$a=Get-Content (Join-Path $local 'acceptance-account.json') -Raw|ConvertFrom-Json;$auth=Invoke-RestMethod https://localhost:7441/api/auth/login -Method Post -ContentType application/json -Body (@{identifier=$a.email;password=$a.password;clientName='Report List correction isolated validation'}|ConvertTo-Json);$script:headers=@{Authorization='Bearer '+$auth.accessToken}}
function Get-Json([string]$path){Invoke-RestMethod ('https://localhost:7441/api/planning/'+$path) -Headers $script:headers}
function Report([string]$search){Invoke-Named 'Report List / Digitación';Invoke-Named 'Digitación';Invoke-Named 'Filtros de Report List';Invoke-Named 'Limpiar filtros';Set-NamedValue 'Buscar JOB en Report List' $search;Combo 'Vista' 'Todas';Invoke-Named 'Buscar Report List';Start-Sleep -Milliseconds 500}
if($Step -eq 'Inspect'){Inspect-Window;Capture 'current';exit}
if($Step -eq 'Overview'){
 Report '';foreach($label in @('Fecha de ingreso','Due Date','JOB','Prueba','Laboratorio','Cliente','Hora de digitación','Fecha de prueba','Estado de digitación','Digitado por','Selección')){Find-Named ('Ordenar Report List por '+$label)|Out-Null}
 $names=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|ForEach-Object{$_.Current.Name});if($names -contains 'Ordenar Report List por Ejecución' -or $names -contains 'Copiar'){throw 'Unexpected default technical column or copy button'}
 Capture '01-report-list-fechas';Inspect-Window
}
if($Step -eq 'Repeated'){
 Report 'GUAT26022934';Invoke-Named 'Filtros de Report List';Set-NamedValue 'Prueba de Planificación' 'PH';Set-NamedValue 'Laboratorio' 'Chemical';Invoke-Named 'Aplicar filtros';Start-Sleep -Milliseconds 350
 $items=@(Items 'Ejecuciones de Report List');if($items.Count -ne 2){throw 'Expected two independent PH executions'}
 if(@($items|Where-Object{$_.Current.Name -like '*Digitado*'}).Count -ne 1 -or @($items|Where-Object{$_.Current.Name -like '*Sin información*'}).Count -ne 1){throw 'Repeated PH states not preserved'}
 Capture '02-ph-repetidas';Inspect-Window
}
if($Step -eq 'Cells'){
 Invoke-Named 'Columnas';Invoke-Named 'Restablecer columnas';Start-Sleep -Milliseconds 200
 $cell=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|Where-Object{$_.Current.Name -like 'F1 C1 *' -and -not $_.Current.IsOffscreen})|Select-Object -First 1
 if(-not $cell){throw 'First source date cell missing'};Click-Point $cell;[Windows.Forms.SendKeys]::SendWait('^{HOME}+{DOWN}+{RIGHT}+{RIGHT}^c');Start-Sleep -Milliseconds 250
 $copy=Get-Clipboard -Raw;$matrix=@($copy.TrimEnd("`r","`n") -split "`r?`n");if($matrix.Count -ne 2 -or @($matrix|Where-Object{@($_ -split "`t").Count -ne 3}).Count){throw ('Expected actual Ctrl+C of two rows by three columns: '+$copy)}
 if($copy -notlike '*2026-10-15*2026-11-06*GUAT26022934*'){throw 'Clipboard lost actual source dates'}
 Capture '03-celdas-ctrl-c';@{Passed=$true;Rows=2;Columns=3;Clipboard=$copy;Boundary='Actual native keyboard and clipboard'}|ConvertTo-Json|Set-Content (Join-Path $out 'cells.json') -Encoding UTF8
 [Windows.Forms.SendKeys]::SendWait('{TAB}{ENTER}{LEFT}{RIGHT}{ESC}')
}
if($Step -eq 'Layout'){
 $open=$window.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'Aplicar ancho'));if($open -and -not $open.Current.IsOffscreen){Invoke-Named 'Cancelar'}
 Invoke-Named 'Columnas';Invoke-Named 'Restablecer columnas';Start-Sleep -Milliseconds 200
 $head=Find-Named 'Ordenar Report List por JOB';Click-Point $head -Right;Invoke-Named 'Mover columna a la izquierda'
 Click-Point (Find-Named 'Ordenar Report List por JOB') -Right;Invoke-Named 'Cambiar ancho de columna';Find-Named 'Ancho de columna en píxeles'|Out-Null;Set-NamedValue 'Ancho de columna en píxeles' '200';Invoke-Named 'Aplicar ancho';Start-Sleep -Milliseconds 250
 Connect-Api;$profile=(Get-Content (Join-Path $local 'acceptance-account.json') -Raw|ConvertFrom-Json);$preferences=Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Lims/ReportList') -Filter 'columns-*.json'|Sort-Object LastWriteTimeUtc -Descending|Select-Object -First 1
 $layout=Get-Content $preferences.FullName -Raw|ConvertFrom-Json;if($layout[1].Key -ne 'Job' -or $layout[1].Width -ne 200){throw 'Column order/width not persisted'}
 $layout|ConvertTo-Json|Set-Content (Join-Path $out 'user-layout.json') -Encoding UTF8;Capture '04-preferencias-columnas'
 Invoke-Named 'Columnas';Invoke-Named 'Restablecer columnas'
}
if($Step -eq 'Prepare'){
 Connect-Api;$candidates=@((Get-Json 'report-list?View=Pending&Test=PH&PageSize=500').items|Where-Object{$_.execution.job -ne 'GUAT26022934' -and $_.checkMode -eq 'manual' -and -not $_.execution.onHold -and -not $_.execution.noProgram -and $_.execution.planningTest -eq 'PH'}|Select-Object -First 2)
 if($candidates.Count -ne 2){throw 'Two real pending PH executions required'}
 $at=[DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(-6));$at=[DateTimeOffset]::new($at.Year,$at.Month,$at.Day,$at.Hour,$at.Minute,0,$at.Offset)
 @{Candidates=$candidates;OperationalAt=$at.ToString('O');Boundary='Actual restored executions selected for native validation; no synthetic data';Baseline=(Get-Json 'report-list?Search=GUAT26022934&View=All').items}|ConvertTo-Json -Depth 12|Set-Content (Join-Path $out 'baseline.json') -Encoding UTF8
}
if($Step -eq 'Select'){
 $baseline=Get-Content (Join-Path $out 'baseline.json') -Raw|ConvertFrom-Json;Report (($baseline.Candidates|ForEach-Object{$_.execution.job}|Select-Object -Unique) -join ';')
 Invoke-Named 'Filtros de Report List';Set-NamedValue 'Prueba de Planificación' 'PH';Set-NamedValue 'Laboratorio' 'Chemical';Invoke-Named 'Aplicar filtros';Start-Sleep -Milliseconds 400
 $items=@(Items 'Ejecuciones de Report List');$selected=0;foreach($item in $items){if(@($baseline.Candidates|Where-Object{$item.Current.Name -like ('*'+$_.execution.executionId+'*')}).Count){$item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).AddToSelection();$selected++}}
 if($selected -ne 2){throw 'Both actual candidates must be selected by identity'}
 $at=[DateTimeOffset]$baseline.OperationalAt;Set-NamedValue 'Fecha de digitación' $at.ToString('yyyy-MM-dd');Set-NamedValue 'Hora de digitación' $at.ToString('HH:mm');Capture '05-seleccion-multiple-fecha-hora';Inspect-Window
}
if($Step -eq 'Save'){
 Invoke-Named 'Guardar digitación';Find-Named 'Confirmar y guardar'|Out-Null;Capture '06-confirmacion';Invoke-Named 'Confirmar y guardar';Start-Sleep -Milliseconds 600;Capture '07-digitacion-guardada';Inspect-Window
}
if($Step -eq 'Groups'){
 Invoke-Named 'Chequeo de horarios';Set-NamedValue 'Buscar JOB en Report List' '';Combo 'Estado de horarios' 'Todos los horarios';Invoke-Named 'Buscar Report List';Start-Sleep -Milliseconds 400;Capture '08-chequeo-horarios';Inspect-Window
}
if($Step -eq 'Detail'){
 $baseline=Get-Content (Join-Path $out 'baseline.json') -Raw|ConvertFrom-Json;$at=[DateTimeOffset]$baseline.OperationalAt
 $item=@(Items 'Horarios de digitación'|Where-Object{$_.Current.Name -like ('*'+$at.ToString('HH:mm')+'*2 pruebas*')})|Select-Object -First 1
 if(-not $item){throw 'Persisted two-member schedule missing'};$item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();Find-Named 'Chequear horario'|Out-Null;Capture '09-detalle-horario';Inspect-Window
}
if($Step -eq 'Review'){Invoke-Named 'Chequear horario';Invoke-Named 'Confirmar';Start-Sleep -Milliseconds 400;Capture '10-horario-chequeado';Inspect-Window}
if($Step -eq 'Verify'){
 Connect-Api;$baseline=Get-Content (Join-Path $out 'baseline.json') -Raw|ConvertFrom-Json;$rows=@();$consulta=@()
 foreach($candidate in $baseline.Candidates){$id=$candidate.execution.executionId;$item=(Get-Json ('report-list?View=All&Search='+$candidate.execution.job)).items|Where-Object{$_.execution.executionId -eq $id};if(-not $item.execution.complete -or [DateTimeOffset]$item.execution.digitizedAt -ne [DateTimeOffset]$baseline.OperationalAt){throw 'Native date/time save not persisted'};$rows+=$item;$same=(Get-Json ('jobs?Scope=History&Search='+$candidate.execution.job)).items|Where-Object{$_.executionId -eq $id};if(-not $same.complete -or $same.scheduleReviewState -ne 'Chequeado'){throw 'Consulta does not reflect saved digitization and review'};$consulta+=$same}
 $detail=Get-Json ('report-list/schedules/'+$consulta[0].digitizationSessionId+'?groupByClock=true');if($detail.members.Count -ne 2 -or $detail.schedule.state -ne 'Chequeado'){throw 'Persisted schedule not reviewed'}
 $jobRows=@((Get-Json 'report-list?Search=GUAT26022934&View=All').items);$original=@($jobRows|Where-Object{$_.execution.planningTest -eq 'PH'});if($original.Count -ne 2 -or @($original|Where-Object{$_.execution.complete}).Count -ne 1 -or @($original|Where-Object{$_.execution.digitizationState -eq 'Sin información'}).Count -ne 1){throw 'Repeated PH baseline changed'}
 foreach($before in $baseline.Baseline){$after=$jobRows|Where-Object{$_.execution.executionId -eq $before.execution.executionId};if(-not $after -or ($before|ConvertTo-Json -Depth 12 -Compress) -ne ($after|ConvertTo-Json -Depth 12 -Compress)){throw 'Original job data or historical mark changed'}}
 Invoke-WebRequest 'https://localhost:7441/api/planning/report-list/schedules/export?GroupByClock=true' -Headers $script:headers -OutFile (Join-Path $out 'chequeados.csv') -UseBasicParsing
 $export=@(Import-Csv (Join-Path $out 'chequeados.csv') -Encoding UTF8);foreach($candidate in $baseline.Candidates){if(-not($export|Where-Object{$_.'Ejecución' -eq $candidate.execution.executionId -and $_.'Chequeado por'})){throw 'Checked export missing selected identity or reviewer'}}
 @{Passed=$true;Rows=$rows;Consulta=$consulta;Schedule=$detail;OriginalRepeatedPH=$original;ExportRows=$export.Count;Utc=[DateTime]::UtcNow.ToString('O');Boundary='Authenticated runtime and actual restored PostgreSQL, after native save and review'}|ConvertTo-Json -Depth 18|Set-Content (Join-Path $out 'persistence.json') -Encoding UTF8
}
if($Step -eq 'References'){
 Invoke-Named 'Estándares';Start-Sleep -Milliseconds 500;Capture '11-estandares-nativos';Invoke-Named 'Preparaciones';Start-Sleep -Milliseconds 500;Capture '12-preparaciones-nativas';Invoke-Named 'Report List / Digitación'
}
if($Step -eq 'ClockCombined'){
 Connect-Api;$groups=Get-Json 'report-list/schedules?GroupByClock=true';$combined=@($groups.items|Where-Object{@($_.sessionIds).Count -gt 1})|Select-Object -First 1
 if(-not $combined){throw 'No actual same-clock confirmations found'};$detail=Get-Json ('report-list/schedules/'+$combined.id+'?groupByClock=true');if($detail.sessions.Count -lt 2 -or $detail.members.Count -ne $combined.tests){throw 'Group membership lost confirmation identity'}
 Invoke-Named 'Chequeo de horarios';Set-NamedValue 'Buscar JOB en Report List' '';Invoke-Named 'Buscar Report List';Start-Sleep -Milliseconds 300;$clock=([DateTimeOffset]$combined.operationalAt).ToOffset([TimeSpan]::FromHours(-6)).ToString('HH:mm');$chosen=@(Items 'Horarios de digitación'|Where-Object{$_.Current.Name -like ('*'+$clock+'*'+$combined.tests+' pruebas*')})|Select-Object -First 1
 if(-not $chosen){throw 'Actual combined group not shown'};$chosen.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();Find-Named 'Pruebas del horario'|Out-Null;Start-Sleep -Milliseconds 250;Capture '13-horario-varias-confirmaciones';$detail|ConvertTo-Json -Depth 16|Set-Content (Join-Path $out 'combined-clock.json') -Encoding UTF8
}
if($Step -eq 'Filters'){
 Report 'GUAT26022934';Invoke-Named 'Filtros de Report List';Set-NamedValue 'Prueba de Planificación' 'PH';Set-NamedValue 'Laboratorio' 'Chemical';Invoke-Named 'Aplicar filtros'
 foreach($filter in @(@('Fecha de ingreso','InDate','2026-10-15'),@('Due Date','DueDate','2026-11-06'),@('Estado de digitación','Checked','Sin información'))){Invoke-Named ('Filtrar Report List por '+$filter[0]);Find-Named ('Filtro de columna '+$filter[1])|Out-Null;Set-NamedValue ('Filtro de columna '+$filter[1]) $filter[2];Invoke-Named 'Aplicar columna';Start-Sleep -Milliseconds 200}
 $items=@(Items 'Ejecuciones de Report List');if($items.Count -ne 1 -or $items[0].Current.Name -notlike '*Sin información*'){throw 'Native source-date and unknown-state filters failed'}
 Invoke-Named 'Ordenar Report List por Fecha de ingreso';Start-Sleep -Milliseconds 200;Capture '14-filtros-fechas-estado';Inspect-Window
}
if($Step -eq 'Volume'){
 Report '';for($i=0;$i -lt 5;$i++){$list=Find-Named 'Ejecuciones de Report List';$scroll=$list.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern);$scroll.SetScrollPercent(-1,100);Start-Sleep -Milliseconds 600}
 $status=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|Where-Object{$_.Current.Name -match '^[\d,.]+ de [\d,.]+ ejecuciones'}|Select-Object -Last 1);$loaded=[int]([regex]::Match($status[0].Current.Name,'^[\d,.]+').Value -replace '[,.]','');$visible=@(Items 'Ejecuciones de Report List'|Where-Object{-not $_.Current.IsOffscreen}).Count
 if($loaded -lt 1500 -or $visible -gt 100){throw 'Incremental loading / native row virtualization not verified'};Capture '15-volumen-virtualizado';@{Passed=$true;Loaded=$loaded;VisibleRows=$visible;Boundary='Actual native ListView with restored data, not LAN benchmark'}|ConvertTo-Json|Set-Content (Join-Path $out 'volume.json') -Encoding UTF8
}
@{Step=$Step;Passed=$true;ProcessId=$process.Id;AssemblySha256=(Get-FileHash (Join-Path (Split-Path $process.Path) 'Lims.Desktop.dll')).Hash;Utc=[DateTime]::UtcNow.ToString('O');Boundary='Authenticated isolated native LIMS with restored local data'}|ConvertTo-Json|Set-Content (Join-Path $out ('native-'+$Step+'.json')) -Encoding UTF8
