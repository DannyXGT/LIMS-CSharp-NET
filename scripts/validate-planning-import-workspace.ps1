param([ValidateSet('Inspect','Review','Import','Manual','Resolve','Reimport','Final','Interactions')][string]$Step='Inspect')
$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'validate-planning-desktop.ps1'));$reviewScriptsRoot=$PSScriptRoot
$begin=$source.IndexOf('$ErrorActionPreference=');$finish=$source.IndexOf("if(`$Action -eq 'Inspect')");Invoke-Expression $source.Substring($begin,$finish-$begin).Replace('$PSScriptRoot','$reviewScriptsRoot')
$process=Get-Process -Id $window.Current.ProcessId
if(-not $process.Path.StartsWith((Join-Path $workspace 'artifacts/planificacion-dev/desktop-validation-'),[StringComparison]::OrdinalIgnoreCase)){throw 'Solo cliente aislado'}
$evidence=Join-Path $local 'centro-operativo/ui';New-Item -ItemType Directory -Path $evidence -Force|Out-Null
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class ReviewGeometry {
 [DllImport("user32.dll")]public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
 [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int w,int v,uint f);
 [DllImport("user32.dll")]public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")]public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e);
}
'@
[ReviewGeometry]::SetThreadDpiAwarenessContext([IntPtr](-4))|Out-Null
$runtime=Get-Content (Join-Path $local 'runtime.json') -Raw|ConvertFrom-Json
if($runtime.Host -ne '127.0.0.1' -or $runtime.Port -ne 55441 -or $runtime.Database -ne 'lims_planning_dev'){throw 'Base no autorizada'}
$account=Get-Content (Join-Path $local 'acceptance-account.json') -Raw|ConvertFrom-Json
$auth=Invoke-RestMethod https://localhost:7441/api/auth/login -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes((@{identifier=$account.email;password=$account.password;clientName='Centro operativo native acceptance'}|ConvertTo-Json)))
$headers=@{Authorization='Bearer '+$auth.accessToken};$caseJob='GUAT25002016-REV4-REV5-RE'
function Api-Case(){Invoke-RestMethod ('https://localhost:7441/api/planning/reviews/workspace?Category=SummaryWithoutReport&Situation=All&Search='+[uri]::EscapeDataString($caseJob)) -Headers $headers}
function Accept([string]$scenario,$details){@{Scenario=$scenario;Passed=$true;AtUtc=[DateTime]::UtcNow.ToString('O');Boundary='Actual native WinUI, original XLSX and authorized manual decisions against isolated lims_planning_dev';Details=$details}|ConvertTo-Json -Depth 8|Set-Content (Join-Path (Split-Path $evidence) ('scenario-'+$scenario+'.json')) -Encoding UTF8;Write-Output ('PASS '+$scenario)}
function Capture([string]$name){
 $handle=[IntPtr]$window.Current.NativeWindowHandle;$rect=[PlanningCapture+Rect]::new();[PlanningCapture]::GetWindowRect($handle,[ref]$rect)|Out-Null
 [PlanningCapture]::SetForegroundWindow($handle)|Out-Null;$width=$rect.Right-$rect.Left;$height=$rect.Bottom-$rect.Top
 [ReviewGeometry]::SetWindowPos($handle,[IntPtr]::Zero,$rect.Left,$rect.Top,$width-1,$height,0x14)|Out-Null;Start-Sleep -Milliseconds 300
 [ReviewGeometry]::SetWindowPos($handle,[IntPtr]::Zero,$rect.Left,$rect.Top,$width,$height,0x14)|Out-Null;Start-Sleep -Milliseconds 700;Capture-Window $window $name
}
function Wait-Like([string]$pattern,[int]$seconds=40){$deadline=[DateTime]::UtcNow.AddSeconds($seconds);do{foreach($e in $window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)){if(-not $e.Current.IsOffscreen -and $e.Current.Name -like $pattern){return $e}};Start-Sleep -Milliseconds 150}while([DateTime]::UtcNow -lt $deadline);Inspect-Window;throw ('No apareció '+$pattern)}
function Set-ReadyValue([string]$name,[string]$value){$deadline=[DateTime]::UtcNow.AddSeconds(15);do{try{Set-NamedValue $name $value;return}catch{Start-Sleep -Milliseconds 150}}while([DateTime]::UtcNow -lt $deadline);throw ('Campo editable no disponible: '+$name)}
function Combo([string]$name,[int]$index){$combo=Find-Named $name;$expand=$combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern);$expand.Expand();Start-Sleep -Milliseconds 150;$items=@($combo.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem)));if($items.Count -eq 0){$items=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ClassNameProperty,'ComboBoxItem'))|Where-Object{-not $_.Current.IsOffscreen})};$items[$index].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();$expand.Collapse();Start-Sleep -Milliseconds 250}
function Search-Review([string]$job){Set-NamedValue 'Buscar JOB en revisión' $job;Invoke-Named 'Buscar';Wait-Like '1–1 de 1'|Out-Null}
function Select-Review(){ $deadline=[DateTime]::UtcNow.AddSeconds(30);do{$list=Find-Named 'Tabla de revisión de importaciones';if($list.Current.IsEnabled){$items=@($list.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem)));if($items.Count -eq 1 -and $items[0].Current.IsEnabled){$items[0].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();Start-Sleep -Milliseconds 200;return}};Start-Sleep -Milliseconds 150}while([DateTime]::UtcNow -lt $deadline);throw 'No terminó de cargar una fila seleccionable' }
$window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState([Windows.Automation.WindowVisualState]::Normal)
[ReviewGeometry]::SetWindowPos([IntPtr]$window.Current.NativeWindowHandle,[IntPtr]::Zero,0,0,1920,1080,0x40)|Out-Null
if($Step -eq 'Inspect'){Inspect-Window;exit}
Invoke-Named 'Importaciones';Invoke-Named 'Revisión';Find-Named 'Solo Summary'|Out-Null;Wait-Like 'Histórico local*'|Out-Null
if($Step -eq 'Review'){
 Capture '01-centro-confirmado';Search-Review $caseJob;Select-Review;Capture '02-bandeja-filtrada';Invoke-Named 'Expediente';Start-Sleep -Milliseconds 700
 $main=$window;$window=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id))|Where-Object{$_.Current.Name -eq ('Estado del JOB · '+$caseJob)}|Select-Object -First 1
 if(-not $window){throw 'No abrió el expediente'};Wait-Like 'Estado TIPS*'|Out-Null;Capture '02b-expediente-summary';$window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close();$window=$main
 $case=(Api-Case).items[0];Accept 'B' @{Job=$caseJob;EpisodeId=$case.episodeId;Buyer=$case.buyer;Vendor=$case.vendor;State=$case.officialState};exit
}
if($Step -in @('Import','Reimport')){
 Invoke-Named 'Nueva importación'
 $fs=$source.IndexOf('    function Select-SourceFile(');$fe=$source.IndexOf("    Select-SourceFile 'Summary XLSX'",$fs);Invoke-Expression $source.Substring($fs,$fe-$fs)
 Select-SourceFile 'Summary XLSX' (Join-Path $env:USERPROFILE 'Downloads/a0_-_pro-invoice_summary_17Nov2026045852237.xlsx')
 Select-SourceFile 'Report List XLSX' (Join-Path $env:USERPROFILE 'Downloads/a0_-_delay_report_list_based_on_due_date_17Nov2026045945888.xlsx')
 Combo 'Corte operativo' 1;Capture '03-archivos-reales';Invoke-Named 'Validar archivos'
 if($Step -eq 'Reimport'){
  Wait-Like 'Este lote ya está confirmado*'|Out-Null;Capture '10-reimportacion-sin-duplicados'
  $case=(Api-Case).items[0];if($case.state -ne 'Resolved'){throw 'El caso resuelto reapareció'};$d=Invoke-RestMethod ('https://localhost:7441/api/planning/jobs/'+$case.episodeId) -Headers $headers
  $previous=Get-Content (Join-Path (Split-Path $evidence) 'scenario-C.json') -Raw|ConvertFrom-Json
  if($d.tests.Count -ne 1 -or $d.tests[0].executionId -ne $previous.Details.ExecutionId){throw 'Prueba manual duplicada o perdida'}
  Accept 'E' @{Job=$caseJob;State=$case.state;Tests=$d.tests.Count;ExecutionId=$d.tests[0].executionId;AlreadyApplied=$true};exit
 }
 Wait-Like 'Archivos validados*' 90|Out-Null;Capture '04-conciliacion';Invoke-Named 'Ir a confirmación';Capture '05-confirmacion';Invoke-Named 'Confirmar lote revisado';Invoke-Named 'Confirmar';Wait-Like 'Lote confirmado. Consulta de Jobs*' 90|Out-Null;Capture '06-lote-confirmado'
 $batches=Invoke-RestMethod https://localhost:7441/api/planning/imports -Headers $headers;$batch=$batches|Where-Object{$_.status -eq 'Applied'}|Select-Object -First 1
 Accept 'A' @{BatchId=$batch.id;Day=$batch.day;Cut=$batch.window;NewJobs=$batch.newJobs;NewTests=$batch.newTests;SummarySha256=(Get-FileHash (Join-Path $env:USERPROFILE 'Downloads/a0_-_pro-invoice_summary_17Nov2026045852237.xlsx')).Hash;ReportSha256=(Get-FileHash (Join-Path $env:USERPROFILE 'Downloads/a0_-_delay_report_list_based_on_due_date_17Nov2026045945888.xlsx')).Hash};exit
}
if($Step -eq 'Manual'){
 $main=$window;$manual=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id))|Where-Object{$_.Current.Name -like 'Agregar pruebas*'}|Select-Object -First 1
 if(-not $manual){Search-Review $caseJob;Select-Review;Invoke-Named 'Agregar pruebas';Start-Sleep -Milliseconds 600;$manual=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id))|Where-Object{$_.Current.Name -like 'Agregar pruebas*'}|Select-Object -First 1}
 $window=$manual;if(-not $window){throw 'No abrió la ventana de pruebas'};Wait-Like 'Selecciona la prueba y documenta*'|Out-Null
 $catalogue=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ComboBox))|Where-Object{$_.Current.Name -eq 'Prueba del catálogo'}|Select-Object -First 1
 if(-not $catalogue){throw 'Selector del catálogo no disponible'}
 $catalogueRows=Invoke-RestMethod https://localhost:7441/api/planning/equivalences -Headers $headers;$mapping=$catalogueRows|Where-Object{-not $_.noProgram -and $_.originalTest -like 'Appearance After Laundering /*' -and $_.equivalence -and $_.laboratory}|Select-Object -First 1
 if(-not $mapping){throw 'No existe una clasificación real para seleccionar'};$catalogue.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand();Set-NamedValue 'Buscar nombre original…' $mapping.originalTest
 $item=Wait-Like $mapping.originalTest;$rect=$item.Current.BoundingRectangle;[PlanningCapture]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle)|Out-Null
 if([PlanningCapture]::GetForegroundWindow() -ne [IntPtr]$window.Current.NativeWindowHandle){throw 'El foco no pertenece a la ventana de pruebas; no se hace clic'}
 [ReviewGeometry]::SetCursorPos([int]($rect.Left+$rect.Width/2),[int]($rect.Top+$rect.Height/2))|Out-Null;[ReviewGeometry]::mouse_event(2,0,0,0,[UIntPtr]::Zero);[ReviewGeometry]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
 Wait-Like 'Equivalencia:*'|Out-Null;Set-NamedValue 'Motivo y fuente de asignación' 'Validación funcional local autorizada: selección de una prueba real del catálogo para comprobar asignación y resolución; no representa una orden oficial de TIPS.';Capture '07-agregar-pruebas'
 Invoke-Named 'Guardar prueba';Wait-Like 'Prueba guardada en PostgreSQL*'|Out-Null;Capture '08-prueba-manual-guardada';$window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close();$window=$main
 $case=(Api-Case).items[0];$d=Invoke-RestMethod ('https://localhost:7441/api/planning/jobs/'+$case.episodeId) -Headers $headers;if($d.tests.Count -ne 1 -or $d.tests[0].origin -ne 'Manual'){throw 'Prueba no persistida'}
 Accept 'C' @{Job=$caseJob;ExecutionId=$d.tests[0].executionId;OriginalTest=$d.tests[0].originalTest;Equivalence=$d.tests[0].equivalence;Laboratory=$d.tests[0].laboratory;Source='Existing real catalogue; authorized local validation assignment'};exit
}
if($Step -eq 'Resolve'){
 Search-Review $caseJob;Select-Review;Invoke-Named 'Resolver';Set-ReadyValue 'Motivo de la decisión' 'Validación local autorizada: asignación desde catálogo guardada y verificada; no modifica estado oficial de TIPS.';Invoke-Named 'Guardar decisión';Wait-Like 'No hay casos que coincidan*'|Out-Null;Combo 'Situación de revisión' 3;Wait-Like '1–1 de 1'|Out-Null;Capture '09-resuelto-en-historico'
 $case=(Api-Case).items[0];if($case.state -ne 'Resolved' -or -not $case.decidedBy -or -not $case.decidedAt){throw 'Decisión incompleta'};Accept 'D' @{Job=$caseJob;State=$case.state;Actor=$case.decidedBy;At=$case.decidedAt};exit
}
if($Step -eq 'Final'){
 Invoke-Named 'Consulta de Jobs';Invoke-Named 'Filtros';Combo 'Alcance de datos' 1;Invoke-Named 'Aplicar filtros';Set-NamedValue 'Buscar JOB' $caseJob;Invoke-Named 'Buscar';Wait-Like '1 ejecución · 1 JOB'|Out-Null;Capture '11-consulta-coherente'
 $rows=Invoke-RestMethod ('https://localhost:7441/api/planning/jobs?Scope=History&SearchMode=Exact&Search='+[uri]::EscapeDataString($caseJob)) -Headers $headers;$case=(Api-Case).items[0]
 if($rows.total -ne 1 -or $rows.items[0].jobState -ne $case.officialState -or $rows.items[0].origin -ne 'Manual'){throw 'Consulta incoherente'};Accept 'F' @{Job=$caseJob;Records=$rows.total;State=$rows.items[0].jobState;ExecutionId=$rows.items[0].executionId;DigitizationState=$rows.items[0].digitizationState}
 Invoke-Named 'Importaciones';Invoke-Named 'Revisión';Combo 'Situación de revisión' 0;Invoke-Named 'Limpiar';Wait-Like '*de 14,722'|Out-Null;Capture '12-revision-final';exit
}
if($Step -eq 'Interactions'){
 function Ready(){Start-Sleep -Milliseconds 200;$deadline=[DateTime]::UtcNow.AddSeconds(20);do{$list=Find-Named 'Tabla de revisión de importaciones';if($list.Current.IsEnabled){return $list};Start-Sleep -Milliseconds 100}while([DateTime]::UtcNow -lt $deadline);throw 'La tabla no terminó de cargar'}
 Invoke-Named 'Solo Summary';Combo 'Situación de revisión' 0;Invoke-Named 'Limpiar';Ready|Out-Null;Wait-Like '*de 14,722'|Out-Null
 Invoke-Named 'Siguiente';Wait-Like '101–200 de *'|Out-Null;Invoke-Named 'Anterior';Wait-Like '1–100 de *'|Out-Null
 Invoke-Named 'Filtrar revisión Estado';Set-ReadyValue 'Valor del filtro de revisión Estado' 'Report Closed';Invoke-Named 'Aplicar columna';Ready|Out-Null
 Invoke-Named 'Ordenar revisión por Fecha ingreso';$list=Ready
 $items=@($list.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem)))
 $items[0].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();$items[1].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).AddToSelection();Invoke-Named 'Copiar';Wait-Like '2 filas copiadas*'|Out-Null
 Add-Type -AssemblyName System.Windows.Forms;$copied=[Windows.Forms.Clipboard]::GetText();if(@($copied -split "`r?`n").Count -ne 2){throw 'La copia no conserva las dos filas'}
 $filter=[uri]::EscapeDataString('{"OfficialState":"Report Closed"}');$expected=Invoke-RestMethod ('https://localhost:7441/api/planning/reviews/workspace?Category=SummaryWithoutReport&Sort=InDate&PageSize=2&ColumnFilters='+$filter) -Headers $headers
 $jobs=@($copied -split "`r?`n"|ForEach-Object{($_ -split "`t")[0]});if($jobs[0] -ne $expected.items[0].job -or $jobs[1] -ne $expected.items[1].job){throw 'Ordenamiento o copia incoherente con la API'}
 Capture '13-seleccion-filtro-orden'
 foreach($name in @('Solo Report List','Sin Vendor','Equivalencias pendientes','Conciliación ambigua','Errores de importación','Solo Summary')){Invoke-Named $name;Ready|Out-Null}
 Invoke-Named 'Limpiar';Ready|Out-Null;Invoke-Named 'Ver historial';Find-Named 'Historial de importaciones'|Out-Null;Capture '14-historial-usuarios-resultados';Invoke-Named 'Revisión';Ready|Out-Null
 [ReviewGeometry]::SetWindowPos([IntPtr]$window.Current.NativeWindowHandle,[IntPtr]::Zero,0,0,1280,800,0x40)|Out-Null;Capture '15-ventana-reducida'
 [ReviewGeometry]::SetWindowPos([IntPtr]$window.Current.NativeWindowHandle,[IntPtr]::Zero,0,0,1920,1080,0x40)|Out-Null;Capture '12-revision-final'
 Accept 'Interactions' @{SelectionRows=2;Copy='TSV exact server order';ColumnFilter='OfficialState';Sort='InDate';Pagination=$true;SixQueues=$true;History=$true;ReducedWindow='1280x800'};exit
}
