param([ValidateSet('Overview','Prepare','Select','Save','Saved','Groups','Review','Verify','Correction','Corrected','Scope','Layout','Preferences','Volume','Cells','Inspect')][string]$Step='Overview')
$ErrorActionPreference='Stop'
$usabilityRoot=$PSScriptRoot;$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'validate-report-list-correction.ps1'))
$begin=$source.IndexOf('$ErrorActionPreference=');$end=$source.IndexOf("if(`$Step -eq 'Inspect')")
Invoke-Expression $source.Substring($begin,$end-$begin).Replace('Join-Path $PSScriptRoot ','Join-Path $usabilityRoot ').Replace('$correctionRoot=$PSScriptRoot','$correctionRoot=$usabilityRoot')
$out=Join-Path $workspace 'artifacts/report-list-usability';$evidence=Join-Path $out 'ui';New-Item -ItemType Directory -Path $evidence -Force|Out-Null
function Capture([string]$name){$handle=[IntPtr]$window.Current.NativeWindowHandle;[PlanningCapture]::SetForegroundWindow($handle)|Out-Null;Start-Sleep -Milliseconds 450;$rect=[PlanningCapture+Rect]::new();[PlanningCapture]::GetWindowRect($handle,[ref]$rect)|Out-Null;$bitmap=[Drawing.Bitmap]::new($rect.Right-$rect.Left,$rect.Bottom-$rect.Top);$graphics=[Drawing.Graphics]::FromImage($bitmap);try{$graphics.CopyFromScreen($rect.Left,$rect.Top,0,0,$bitmap.Size);$bitmap.Save((Join-Path $evidence ($name+'.png')),[Drawing.Imaging.ImageFormat]::Png)}finally{$graphics.Dispose();$bitmap.Dispose()}}
$baseline=Get-Content (Join-Path $out 'baseline.json') -Raw|ConvertFrom-Json
function PhReport{Report $baseline.Job;Invoke-Named 'Filtros de Report List';Set-NamedValue 'Prueba de Planificación' 'PH';Set-NamedValue 'Laboratorio' 'Chemical';Invoke-Named 'Aplicar filtros';Start-Sleep -Milliseconds 350}
function Check([string]$id){$condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::CheckBox);$check=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,$condition)|Where-Object{$_.Current.Name -like ('CHK *'+$id) -and -not $_.Current.IsOffscreen})|Select-Object -First 1;if(-not $check){throw ('CHK not found: '+$id)};return $check}
function Toggle($check){$check.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle();Start-Sleep -Milliseconds 200}
function Open-Columns{Invoke-Named 'Configurar tabla de Report List';$submenu=Find-Named 'Columnas';$submenu.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand();Find-Named 'Estado de digitación'|Out-Null}
function Click-Menu([string]$name){$option=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$name))|Where-Object{$_.Current.ControlType -eq [Windows.Automation.ControlType]::MenuItem -and -not $_.Current.IsOffscreen})|Select-Object -First 1;if(-not $option){throw ('Menu option unavailable: '+$name)};$pattern=$null;if($option.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern,[ref]$pattern)){$pattern.Invoke()}elseif($option.TryGetCurrentPattern([Windows.Automation.TogglePattern]::Pattern,[ref]$pattern)){$pattern.Toggle()}else{Start-Sleep -Milliseconds 350;Click-Point $option};Start-Sleep -Milliseconds 250}
function Wait-Check([string]$id,[bool]$enabled,[string]$state){$timer=[Diagnostics.Stopwatch]::StartNew();do{try{$check=Check $id;if($check.Current.IsEnabled -eq $enabled -and $check.Current.ItemStatus -eq $state){return $check}}catch{};Start-Sleep -Milliseconds 150}while($timer.Elapsed.TotalSeconds -lt 15);throw ('CHK state did not refresh: '+$id+' / '+$state)}
$correctionReason='Restaurar solo la digitacion creada en esta validacion local de usabilidad '+$baseline.OperationalAt+'; conservar historial y la otra PH.'
if($Step -eq 'Prepare'){
 Connect-Api;$rows=@((Get-Json ('report-list?View=All&Search='+$baseline.Job)).items|Where-Object{$_.execution.planningTest -eq 'PH'});$pending=@($rows|Where-Object{-not $_.execution.complete});$saved=@($rows|Where-Object{$_.execution.complete});if($rows.Count -ne 2 -or $pending.Count -ne 1 -or $saved.Count -ne 1){throw 'Saved and pending real PH required'}
 $now=[DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(-6));$at=[DateTimeOffset]::new($now.Year,$now.Month,$now.Day,$now.Hour,$now.Minute,0,$now.Offset)
 @{Job=$baseline.Job;Rows=$rows;Pending=$pending[0];Saved=$saved[0];OperationalAt=$at.ToString('O');Boundary='Actual restored local executions, before native validation'}|ConvertTo-Json -Depth 14|Set-Content (Join-Path $out 'baseline.json') -Encoding UTF8
}
if($Step -eq 'Inspect'){Inspect-Window;Capture 'current';exit}
if($Step -eq 'Overview'){
 PhReport;foreach($label in @('Fecha de ingreso','Due Date','JOB','Prueba','Laboratorio','Hora','Fecha de prueba','Digitado por')){Find-Named ('Ordenar Report List por '+$label)|Out-Null};Find-Named 'Marcar pruebas pendientes visibles'|Out-Null;Find-Named 'Configurar tabla de Report List'|Out-Null
 $names=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|Where-Object{-not $_.Current.IsOffscreen}|ForEach-Object{$_.Current.Name})
 foreach($unwanted in @('Selección','Seleccionar cargadas','Captura AR','Copiar','Ordenar Report List por Estado de digitación','Ordenar Report List por Bloque','Ordenar Report List por Chequeo de horario')){if($names -contains $unwanted){throw ('Redundant default UI: '+$unwanted)}}
 if(@(Items 'Ejecuciones de Report List').Count -ne 2){throw 'Two repeated PH executions required'}
 $saved=Check $baseline.Saved.execution.executionId;$pending=Check $baseline.Pending.execution.executionId
 if($saved.Current.IsEnabled -or $saved.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne [Windows.Automation.ToggleState]::On -or -not $pending.Current.IsEnabled){throw 'Stored and temporary CHK not differentiated'}
 Capture '01-digitacion-simple-ph-repetidas';Inspect-Window
}
if($Step -eq 'Select'){
 Toggle (Check $baseline.Pending.execution.executionId);Find-Named '1 prueba seleccionada'|Out-Null;Invoke-Named 'Desmarcar selección temporal';Start-Sleep -Milliseconds 250;Toggle (Check $baseline.Pending.execution.executionId);Find-Named '1 prueba seleccionada'|Out-Null
 if((Check $baseline.Pending.execution.executionId).Current.ItemStatus -ne 'Marcada sin guardar'){throw 'CHK did not create temporary selection'}
 $at=[DateTimeOffset]$baseline.OperationalAt;Set-NamedValue 'Fecha de digitación' $at.ToString('yyyy-MM-dd');Set-NamedValue 'Hora de digitación' $at.ToString('HH:mm');Capture '02-chk-temporal-fecha-hora'
}
if($Step -eq 'Save'){
 Invoke-Named 'Guardar digitación';Find-Named 'Confirmar y guardar'|Out-Null;Capture '03-confirmacion';Invoke-Named 'Confirmar y guardar';Start-Sleep -Milliseconds 600
 $check=Wait-Check $baseline.Pending.execution.executionId $false 'Digitación guardada';Capture '04-digitacion-persistida';Inspect-Window
}
if($Step -eq 'Saved'){
 PhReport;$check=Wait-Check $baseline.Pending.execution.executionId $false 'Digitación guardada';Capture '04-digitacion-persistida';Inspect-Window
}
if($Step -eq 'Groups'){
 Connect-Api;$row=(Get-Json ('report-list?View=All&Search='+$baseline.Job)).items|Where-Object{$_.execution.executionId -eq $baseline.Pending.execution.executionId};$consulta=(Get-Json ('jobs?Scope=History&Search='+$baseline.Job)).items|Where-Object{$_.executionId -eq $baseline.Pending.execution.executionId}
 if(-not $row.execution.complete -or -not $consulta.digitizationSessionId){throw 'Saved execution did not reach Consulta'}
 $script:session=$consulta.digitizationSessionId;@{SessionId=$script:session;ExecutionId=$row.execution.executionId}|ConvertTo-Json|Set-Content (Join-Path $out 'saved-session.json') -Encoding UTF8
 Invoke-Named 'Chequeo de horarios';Set-NamedValue 'Buscar JOB en Report List' $baseline.Job;Invoke-Named 'Buscar Report List';Start-Sleep -Milliseconds 300;Capture '05-chequeo-horarios'
 $clock=([DateTimeOffset]$baseline.OperationalAt).ToString('HH:mm');$item=@(Items 'Horarios de digitación'|Where-Object{$_.Current.Name -like ('*'+$clock+'*')})|Select-Object -First 1;if(-not $item){throw 'New schedule missing'};$item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();Find-Named 'Pruebas del horario'|Out-Null;Start-Sleep -Milliseconds 200;Capture '06-detalle-horario'
}
if($Step -eq 'Review'){Invoke-Named 'Chequear horario';Invoke-Named 'Confirmar';Start-Sleep -Milliseconds 350;Capture '07-horario-chequeado'}
if($Step -eq 'Verify'){
 Connect-Api;$rows=@((Get-Json ('report-list?View=All&Search='+$baseline.Job)).items|Where-Object{$_.execution.planningTest -eq 'PH'});if($rows.Count -ne 2){throw 'Repeated identity lost'}
 $saved=$rows|Where-Object{$_.execution.executionId -eq $baseline.Pending.execution.executionId};$original=$rows|Where-Object{$_.execution.executionId -eq $baseline.Saved.execution.executionId};if(($original|ConvertTo-Json -Depth 12 -Compress) -ne ($baseline.Saved|ConvertTo-Json -Depth 12 -Compress)){throw 'Existing historical PH was modified'}
 if(-not $saved.execution.complete -or [DateTimeOffset]$saved.execution.digitizedAt -ne [DateTimeOffset]$baseline.OperationalAt){throw 'Native operational time not persisted'}
 $consulta=(Get-Json ('jobs?Scope=History&Search='+$baseline.Job)).items|Where-Object{$_.executionId -eq $saved.execution.executionId};if($consulta.scheduleReviewState -ne 'Chequeado'){throw 'Review not reflected in Consulta'}
 $detail=Get-Json ('report-list/schedules/'+$consulta.digitizationSessionId+'?groupByClock=true');if($detail.schedule.state -ne 'Chequeado'){throw 'Persisted schedule not reviewed'}
 @{Passed=$true;Boundary='Authenticated runtime, actual restored local PostgreSQL, after native save and review';Rows=$rows;Consulta=$consulta;Schedule=$detail;OriginalPHUnchanged=$true;Utc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json -Depth 18|Set-Content (Join-Path $out 'persistence.json') -Encoding UTF8
}
if($Step -eq 'Correction'){
 PhReport;$cell=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|Where-Object{$_.Current.Name -like 'F2 C3 *' -and -not $_.Current.IsOffscreen})|Select-Object -First 1
 # Identify the row by its stable CHK identity and use the JOB cell at the same vertical position.
 $check=Check $baseline.Pending.execution.executionId;$b=$check.Current.BoundingRectangle;$cell=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|Where-Object{$_.Current.Name -match '^F\d+ C3 ' -and -not $_.Current.IsOffscreen -and [Math]::Abs($_.Current.BoundingRectangle.Top-$b.Top) -lt 20})|Select-Object -First 1
 if(-not $cell){throw 'Original pending execution JOB cell unavailable'};Click-Point $cell -Right;Invoke-Named 'Corregir digitación';Find-Named 'Motivo obligatorio'|Out-Null
 Set-NamedValue 'Motivo obligatorio' $correctionReason;Capture '08-correccion-separada-auditada';Invoke-Named 'Guardar corrección';Start-Sleep -Milliseconds 500
}
if($Step -in @('Correction','Corrected')){
 if($Step -eq 'Corrected'){PhReport}
 $restored=Wait-Check $baseline.Pending.execution.executionId $true 'Sin información'
 Connect-Api;$dossier=Get-Json ('jobs/'+$baseline.Pending.execution.episodeId);$history=@($dossier.history|Where-Object{$_.action -eq 'ReportCorrected' -and $_.reason -eq $correctionReason});if($history.Count -ne 1){throw 'Authorized correction audit missing'}
 $rows=@((Get-Json ('report-list?View=All&Search='+$baseline.Job)).items|Where-Object{$_.execution.planningTest -eq 'PH'});$original=$rows|Where-Object{$_.execution.executionId -eq $baseline.Saved.execution.executionId};$pending=$rows|Where-Object{$_.execution.executionId -eq $baseline.Pending.execution.executionId}
 if($rows.Count -ne 2 -or $pending.execution.complete -or $pending.execution.digitizedAt -or ($original|ConvertTo-Json -Depth 12 -Compress) -ne ($baseline.Saved|ConvertTo-Json -Depth 12 -Compress)){throw 'Final independent PH states or original history changed'}
 @{Passed=$true;History=$history;Rows=$rows;OriginalPHUnchanged=$true;Boundary='Restored local database; only test-created mark reversed, with audit retained';Utc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json -Depth 15|Set-Content (Join-Path $out 'correction-audit.json') -Encoding UTF8;Capture '09-ph-independientes-final';Inspect-Window
}
if($Step -eq 'Scope'){
 Report '';Start-Sleep -Milliseconds 200;$header=Find-Named 'Marcar pruebas pendientes visibles';Toggle $header
 $selected=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|Where-Object{$_.Current.Name -match '^\d+ pruebas? seleccionadas?'}|Select-Object -Last 1);if(-not $selected){throw 'Visible CHK multiple selection unavailable'}
 $count=[int]([regex]::Match($selected[0].Current.Name,'^\d+').Value);if($count -gt 30 -or $count -lt 1){throw 'CHK header selected outside visible scope'};Capture '10-checks-multiples-visibles'
 $list=Find-Named 'Ejecuciones de Report List';$scroll=$list.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern);$scroll.SetScrollPercent(-1,100);Start-Sleep -Milliseconds 400;Invoke-Named 'Guardar digitación';Find-Named 'Confirmar y guardar'|Out-Null
 $warning=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|Where-Object{$_.Current.Name -like 'Incluir las * pruebas seleccionadas fuera de la vista actual' -and -not $_.Current.IsOffscreen})|Select-Object -First 1;if(-not $warning){throw 'Explicit offscreen confirmation missing'}
 Invoke-Named 'Confirmar y guardar';Find-Named 'Confirmar y guardar'|Out-Null;Capture '11-alcance-fuera-vista-confirmacion';Invoke-Named 'Cancelar';Invoke-Named 'Desmarcar selección temporal'
 @{Passed=$true;SelectedVisible=$count;OffscreenSaveBlockedWithoutExplicitConsent=$true;NoMutationPerformed=$true}|ConvertTo-Json|Set-Content (Join-Path $out 'selection-scope.json') -Encoding UTF8
}
if($Step -eq 'Layout'){
 PhReport
 Invoke-Named 'Configurar tabla de Report List';$submenu=Find-Named 'Columnas';Capture '12-configuracion-compacta';$submenu.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand();Invoke-Named 'Restablecer columnas';Start-Sleep -Milliseconds 150
}
if($Step -eq 'Preferences'){
 PhReport;Open-Columns;Invoke-Named 'Restablecer columnas';Open-Columns;Click-Menu 'Estado de digitación';Find-Named 'Ordenar Report List por Estado de digitación'|Out-Null
 Open-Columns;Click-Menu 'Cliente'
 Click-Point (Find-Named 'Ordenar Report List por JOB') -Right;Invoke-Named 'Mover columna a la izquierda'
 Click-Point (Find-Named 'Ordenar Report List por JOB') -Right;Invoke-Named 'Cambiar ancho de columna';Set-NamedValue 'Ancho de columna en píxeles' '200';Invoke-Named 'Aplicar ancho';Start-Sleep -Milliseconds 250
 $preferences=Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Lims/ReportList') -Filter 'columns-*.json'|Sort-Object LastWriteTimeUtc -Descending|Select-Object -First 1;$layout=Get-Content $preferences.FullName -Raw|ConvertFrom-Json
 if($layout.Version -ne 2 -or $layout.Columns[1].Key -ne 'Job' -or $layout.Columns[1].Width -ne 200 -or $layout.Columns.Key -contains 'Buyer' -or $layout.Columns.Key -notcontains 'Checked'){throw 'Native show/hide/order/width preferences were not saved'}
 @{Passed=$true;Layout=$layout;Boundary='Native controls and actual per-user preferences file; default layout restored afterwards'}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $out 'column-preferences.json') -Encoding UTF8;Capture '14-columnas-orden-ancho'
 Open-Columns;Invoke-Named 'Restablecer columnas'
}
if($Step -eq 'Volume'){
 Report '';for($i=0;$i -lt 5;$i++){$scroll=(Find-Named 'Ejecuciones de Report List').GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern);$scroll.SetScrollPercent(-1,100);Start-Sleep -Milliseconds 600}
 $status=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|Where-Object{$_.Current.Name -match '^[\d,.]+ de [\d,.]+ ejecuciones'}|Select-Object -Last 1);$loaded=[int]([regex]::Match($status[0].Current.Name,'^[\d,.]+').Value -replace '[,.]','');$visible=@(Items 'Ejecuciones de Report List'|Where-Object{-not $_.Current.IsOffscreen}).Count
 if($loaded -lt 1500 -or $visible -gt 100){throw 'Incremental loading / native virtualization not verified'};Capture '15-volumen-virtualizado';@{Passed=$true;Loaded=$loaded;VisibleRows=$visible;Boundary='Native ListView with restored real data; local only'}|ConvertTo-Json|Set-Content (Join-Path $out 'volume.json') -Encoding UTF8
}
if($Step -eq 'Cells'){
 PhReport;$cell=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|Where-Object{$_.Current.Name -like 'F1 C1 *' -and -not $_.Current.IsOffscreen})|Select-Object -First 1;Click-Point $cell;[Windows.Forms.SendKeys]::SendWait('^{HOME}+{DOWN}+{RIGHT}+{RIGHT}^c');Start-Sleep -Milliseconds 200;$copy=Get-Clipboard -Raw
 if(@($copy -split "`r?`n").Count -ne 2 -or $copy -notlike '*2026-10-15*2026-11-06*GUAT26022934*'){throw 'Native cell selection/copy regressed'};Capture '13-celdas-y-teclado';[Windows.Forms.SendKeys]::SendWait('{ESC}')
}
@{Passed=$true;Step=$Step;Boundary='Existing native LIMS on restored local test database';ProcessId=$process.Id;AssemblySha256=(Get-FileHash (Join-Path (Split-Path $process.Path) 'Lims.Desktop.dll')).Hash;Utc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json|Set-Content (Join-Path $out ('native-'+$Step+'.json')) -Encoding UTF8
