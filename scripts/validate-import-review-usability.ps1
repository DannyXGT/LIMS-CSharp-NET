param([ValidateSet('Baseline','Overview','Queues','Interactions','Detail','Viewport','Final','Inspect')][string]$Step='Overview',[int]$Width=1366,[int]$Height=768)
$ErrorActionPreference='Stop';$importScriptsRoot=$PSScriptRoot
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'validate-report-list-correction.ps1'));$start=$source.IndexOf('$ErrorActionPreference=');$finish=$source.IndexOf("if(`$Step -eq 'Inspect')")
Invoke-Expression $source.Substring($start,$finish-$start).Replace('Join-Path $PSScriptRoot ','Join-Path $importScriptsRoot ').Replace('$correctionRoot=$PSScriptRoot','$correctionRoot=$importScriptsRoot')
$out=Join-Path $workspace 'artifacts/import-review-usability';$evidence=Join-Path $out 'ui';New-Item -ItemType Directory -Path $evidence -Force|Out-Null
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class ImportReviewWindow {[DllImport("user32.dll")]public static extern bool ShowWindow(IntPtr h,int command);[DllImport("user32.dll")]public static extern bool IsIconic(IntPtr h);}
'@
function Capture([string]$name){$handle=[IntPtr]$window.Current.NativeWindowHandle;if([ImportReviewWindow]::IsIconic($handle)){[ImportReviewWindow]::ShowWindow($handle,9)|Out-Null;Start-Sleep -Milliseconds 300};[PlanningCapture]::SetForegroundWindow($handle)|Out-Null;$r=[PlanningCapture+Rect]::new();[PlanningCapture]::GetWindowRect($handle,[ref]$r)|Out-Null;[PlanningCapture]::SetCursorPos($r.Left+90,$r.Top+30)|Out-Null;Start-Sleep -Milliseconds 650;$bitmap=[Drawing.Bitmap]::new($r.Right-$r.Left,$r.Bottom-$r.Top);$graphics=[Drawing.Graphics]::FromImage($bitmap);try{try{$graphics.CopyFromScreen($r.Left,$r.Top,0,0,$bitmap.Size)}catch{$dc=$graphics.GetHdc();try{if(-not [PlanningCapture]::PrintWindow($handle,$dc,2)){throw 'Native window capture unavailable'}}finally{$graphics.ReleaseHdc($dc)};@{Mode='PrintWindow fallback';Left=$r.Left;Top=$r.Top;Width=$bitmap.Width;Height=$bitmap.Height}|ConvertTo-Json|Set-Content (Join-Path $out ('capture-'+$name+'.json')) -Encoding UTF8};$bitmap.Save((Join-Path $evidence ($name+'.png')),[Drawing.Imaging.ImageFormat]::Png)}finally{$graphics.Dispose();$bitmap.Dispose()}}
function Find-Type([string]$name,$type){$all=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$name));$item=@($all|Where-Object{$_.Current.ControlType -eq $type -and -not $_.Current.IsOffscreen})|Select-Object -First 1;if(-not $item){throw ('Typed control unavailable: '+$name)};return $item}
function Ready{
 $timer=[Diagnostics.Stopwatch]::StartNew();do{$list=Find-Named 'Celdas de bandeja de revisión';$loading=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ProgressBar))|Where-Object{-not $_.Current.IsOffscreen});if($list.Current.IsEnabled -and $loading.Count -eq 0){Start-Sleep -Milliseconds 300;return};Start-Sleep -Milliseconds 150}while($timer.Elapsed.TotalSeconds -lt 50);throw 'Review did not finish loading'
}
function Cell([int]$row,[int]$column){$all=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition);$cell=@($all|Where-Object{$_.Current.Name -like ('F'+$row+' C'+$column+' *') -and -not $_.Current.IsOffscreen})|Select-Object -First 1;if(-not $cell){throw ('Cell unavailable: '+$row+','+$column)};return $cell}
function Review{Invoke-Named 'Importaciones';$tab=Find-Type 'Bandejas de revisión' ([Windows.Automation.ControlType]::TabItem);$tab.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();Find-Named 'Buscar JOB en revisión'|Out-Null;Ready}
function Choose([string]$name,[string]$contains){$combo=Find-Type $name ([Windows.Automation.ControlType]::ComboBox);$pattern=$combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern);$pattern.Expand();Start-Sleep -Milliseconds 150;$items=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem));$item=@($items|Where-Object{$_.Current.Name -like ('*'+$contains+'*') -and -not $_.Current.IsOffscreen})|Select-Object -First 1;if(-not $item){throw ('Choice unavailable: '+$contains)};$item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();$pattern.Collapse();Start-Sleep -Milliseconds 250;Ready}
Connect-Api
if($Step -eq 'Baseline'){
 $batches=Get-Json 'imports';$batch=$batches|Where-Object{$_.status -eq 'Applied'}|Select-Object -First 1;if(-not $batch){throw 'Actual applied local import batch required'}
 $data=Get-Json ('reviews/workspace?Category=SummaryWithoutReport&Situation=Pending&Scope=Batch&BatchId='+$batch.id+'&PageSize=100&Sort=Job')
 @{Batch=$batch;Data=$data;Boundary='Actual restored local data before read-only visual acceptance';Utc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json -Depth 15|Set-Content (Join-Path $out 'baseline.json') -Encoding UTF8
 Review;Capture '00-datos-base-locales';Inspect-Window;exit
}
$baseline=Get-Content (Join-Path $out 'baseline.json') -Raw|ConvertFrom-Json
if($Step -eq 'Inspect'){Inspect-Window;Capture 'current';exit}
if($Step -eq 'Overview'){
 [ImportReviewWindow]::ShowWindow([IntPtr]$window.Current.NativeWindowHandle,3)|Out-Null;Start-Sleep -Milliseconds 400;Review
 foreach($name in @('Solo Summary','Solo Report List','Sin Buyer/Vendor','Sin equivalencia','Pruebas por agregar','Datos incompletos','Retenidos y resueltos')){Find-Type $name ([Windows.Automation.ControlType]::Button)|Out-Null}
 $first=Cell 1 1;if($first.Current.Name -notlike ('*'+$baseline.Data.items[0].job+'*')){throw 'Actual source row did not reach the native table'}
 $title=Find-Named 'Centro de Importaciones';$heading=Find-Named 'Ordenar por JOB';$delta=$heading.Current.BoundingRectangle.Top-$title.Current.BoundingRectangle.Top;if($delta -gt 210){throw 'Table still pushed down by redundant header bands'}
 @{HeaderToTablePixels=$delta;NativeFirstCell=$first.Current.Name;RowsInPage=$baseline.Data.items.Count;FilteredTotal=$baseline.Data.total;Counters=$baseline.Data.counters}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $out 'wide-geometry-data.json') -Encoding UTF8
 (Find-Named 'Buscar JOB en revisión').SetFocus();Capture '01-bandejas-con-datos';Inspect-Window
}
if($Step -eq 'Queues'){
 Review;foreach($name in @('Solo Report List','Sin Buyer/Vendor','Sin equivalencia','Pruebas por agregar','Datos incompletos','Retenidos y resueltos','Solo Summary')){Invoke-Named $name;Ready;Find-Named 'Situación de revisión'|Out-Null};Capture '02-bandejas-accesibles'
}
if($Step -eq 'Interactions'){
 Review;Set-NamedValue 'Buscar JOB en revisión' $baseline.Data.items[0].job;Start-Sleep -Milliseconds 650;Ready;$first=Cell 1 1;if($first.Current.Name -notlike ('*'+$baseline.Data.items[0].job+'*')){throw 'Job search changed source identity'};Capture '03-busqueda-job-real'
 Set-NamedValue 'Buscar JOB en revisión' '';Start-Sleep -Milliseconds 650;Ready;Click-Point (Cell 1 1);if([PlanningCapture]::GetForegroundWindow() -ne [IntPtr]$window.Current.NativeWindowHandle){throw 'Interactive keyboard focus unavailable for isolated client'};[Windows.Forms.SendKeys]::SendWait('^{HOME}+{DOWN}+{RIGHT}+{RIGHT}^c');Start-Sleep -Milliseconds 200;$copy=Get-Clipboard -Raw;if(@($copy -split "`r?`n").Count -ne 2 -or @((($copy -split "`r?`n")[0]) -split "`t").Count -ne 3){throw 'Actual native 2x3 cell copy failed'};Capture '04-celdas-teclado';[Windows.Forms.SendKeys]::SendWait('{ESC}')
 Invoke-Named 'Siguiente';Ready;$next=Cell 1 1;if($next.Current.Name -eq $first.Current.Name){throw 'Server paging did not change the visible row'};Capture '05-pagina-siguiente';Invoke-Named 'Anterior';Ready
 Invoke-Named 'Filtros';Find-Named 'Filtros adicionales'|Out-Null;Capture '06-filtros';[Windows.Forms.SendKeys]::SendWait('{ESC}')
 Invoke-Named 'Columnas';Find-Named 'Columnas visibles'|Out-Null;Capture '07-columnas';[Windows.Forms.SendKeys]::SendWait('{ESC}')
 Invoke-Named 'Exportar';Find-Named 'Registros filtrados'|Out-Null;Find-Named 'Filas seleccionadas'|Out-Null;Find-Named 'Categoría completa en este alcance'|Out-Null;Capture '08-alcances-exportacion';[Windows.Forms.SendKeys]::SendWait('{ESC}')
}
if($Step -eq 'Detail'){
 Review;Click-Point (Cell 1 1);Invoke-Named 'Ver detalle';Find-Named 'Detalle contextual de incidencia'|Out-Null;Start-Sleep -Milliseconds 600;Capture '09-detalle-con-procedencia';Invoke-Named 'Cerrar detalle'
 Invoke-Named 'Acciones';Find-Named 'Expediente'|Out-Null;Capture '10-acciones-con-permisos';[Windows.Forms.SendKeys]::SendWait('{ESC}')
}
if($Step -eq 'Viewport'){
 $handle=[IntPtr]$window.Current.NativeWindowHandle;[ImportReviewWindow]::ShowWindow($handle,9)|Out-Null;[ReportCorrectionDpi]::SetWindowPos($handle,[IntPtr]::Zero,30,30,$Width,$Height,0x40)|Out-Null;Start-Sleep -Milliseconds 500;Review
 $picker=Find-Named 'Bandeja de revisión';Choose 'Bandeja de revisión' 'Datos incompletos';Choose 'Bandeja de revisión' 'Solo Summary';$search=Find-Named 'Buscar JOB en revisión';$scope=Find-Named 'Alcance de bandejas';$bounds=$window.Current.BoundingRectangle
 foreach($control in @($search,$scope,$picker, (Find-Named 'Acciones'))){$b=$control.Current.BoundingRectangle;if($b.Right -gt $bounds.Right-10 -or $b.Left -lt $bounds.Left+60 -or $b.Bottom -gt $bounds.Bottom-30){throw 'Viewport clips an operational control'}}
 Capture ('11-ventana-'+$Width+'x'+$Height)
}
$after=Get-Json ('reviews/workspace?Category=SummaryWithoutReport&Situation=Pending&Scope=Batch&BatchId='+$baseline.Batch.id+'&PageSize=100&Sort=Job')
foreach($field in @('items','counters','total')){if(($after.$field|ConvertTo-Json -Depth 15 -Compress) -ne ($baseline.Data.$field|ConvertTo-Json -Depth 15 -Compress)){throw ('Visual acceptance changed persisted review data: '+$field)}}
@{Passed=$true;Step=$Step;SavedDataUnchanged=$true;Boundary='Authenticated existing WinUI; restored local PostgreSQL only';AssemblySha256=(Get-FileHash (Join-Path (Split-Path $process.Path) 'Lims.Desktop.dll')).Hash;ProcessId=$process.Id;Utc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json|Set-Content (Join-Path $out ('native-'+$Step+'.json')) -Encoding UTF8
