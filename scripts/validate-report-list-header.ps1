param([ValidateSet('Overview','Selection','Settings','Columns','Navigation','Viewport','Inspect')][string]$Step='Overview',[int]$Width=1366,[int]$Height=768)
$ErrorActionPreference='Stop'
$headerScriptsRoot=$PSScriptRoot
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'validate-report-list-usability.ps1'))
$start=$source.IndexOf('$ErrorActionPreference=');$end=$source.IndexOf('$correctionReason=')
Invoke-Expression $source.Substring($start,$end-$start).Replace('$usabilityRoot=$PSScriptRoot','$usabilityRoot=$headerScriptsRoot').Replace("Join-Path `$PSScriptRoot 'validate-report-list-correction.ps1'","Join-Path `$headerScriptsRoot 'validate-report-list-correction.ps1'")
$out=Join-Path $workspace 'artifacts/report-list-header';$evidence=Join-Path $out 'ui';New-Item -ItemType Directory -Path $evidence -Force|Out-Null
function Capture([string]$name,[int]$captureHeight=0){$handle=[IntPtr]$window.Current.NativeWindowHandle;[PlanningCapture]::SetForegroundWindow($handle)|Out-Null;$rect=[PlanningCapture+Rect]::new();[PlanningCapture]::GetWindowRect($handle,[ref]$rect)|Out-Null;[PlanningCapture]::SetCursorPos($rect.Left+90,$rect.Top+30)|Out-Null;Start-Sleep -Milliseconds 650;$bitmapHeight=if($captureHeight -gt 0){[Math]::Min($captureHeight,$rect.Bottom-$rect.Top)}else{$rect.Bottom-$rect.Top};$bitmap=[Drawing.Bitmap]::new($rect.Right-$rect.Left,$bitmapHeight);$graphics=[Drawing.Graphics]::FromImage($bitmap);try{$graphics.CopyFromScreen($rect.Left,$rect.Top,0,0,$bitmap.Size);$bitmap.Save((Join-Path $evidence ($name+'.png')),[Drawing.Imaging.ImageFormat]::Png)}finally{$graphics.Dispose();$bitmap.Dispose()}}
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class ReportHeaderWindow {
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int command);
}
'@
function Open-Columns{
 Invoke-Named 'Configurar tabla de Report List';$submenu=Find-Named 'Columnas';$pattern=$submenu.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern);$pattern.Expand();Find-Named 'Estado de digitación'|Out-Null
}
function Assert-Header([bool]$sameLine){
 $title=Find-Named 'Report List';$tab=Find-Named 'Digitación';$search=Find-Named 'Buscar JOB en Report List';$head=Find-Named 'Ordenar Report List por Fecha de ingreso';$config=Find-Named 'Configurar tabla de Report List'
 $t=$title.Current.BoundingRectangle;$n=$tab.Current.BoundingRectangle;$s=$search.Current.BoundingRectangle;$h=$head.Current.BoundingRectangle;$c=$config.Current.BoundingRectangle;$w=$window.Current.BoundingRectangle
 if($sameLine -and [Math]::Abs(($t.Top+$t.Height/2)-($n.Top+$n.Height/2)) -gt 18){throw 'Title and tabs did not share a compact header'}
 if($sameLine -and $h.Top-$t.Top -gt 145){throw 'Header still consumes excessive vertical space'}
 if($s.Width -lt 220 -or $c.Right -gt $w.Right-10 -or $c.Bottom -gt $h.Top -or $h.Top -gt $w.Bottom-180){throw 'Clipped or overflowing header controls'}
 return @{Title=$t.ToString();Tabs=$n.ToString();Search=$s.ToString();TableTop=$h.Top;HeaderToTablePixels=$h.Top-$t.Top;Window=$w.ToString();SingleHeaderRow=$sameLine}
}
if($Step -eq 'Inspect'){Inspect-Window;Capture 'current';exit}
if($Step -eq 'Overview'){
 [ReportHeaderWindow]::ShowWindow([IntPtr]$window.Current.NativeWindowHandle,3)|Out-Null;Start-Sleep -Milliseconds 400;PhReport;foreach($name in @('Due Date','JOB','Prueba','Laboratorio','Hora','Fecha de prueba','Digitado por')){Find-Named ('Ordenar Report List por '+$name)|Out-Null}
 $saved=Wait-Check $baseline.Saved.execution.executionId $false 'Digitación guardada';$pending=Wait-Check $baseline.Pending.execution.executionId $true 'Sin información'
 $geometry=Assert-Header $true;(Find-Named 'Buscar JOB en Report List').SetFocus();Capture '01-cabecera-compacta';Capture '09-cabecera-detalle' 400;$geometry|ConvertTo-Json|Set-Content (Join-Path $out 'geometry-wide.json') -Encoding UTF8
}
if($Step -eq 'Selection'){
 PhReport;Toggle (Check $baseline.Pending.execution.executionId);Invoke-Named 'Desmarcar selección temporal';Start-Sleep -Milliseconds 200;Toggle (Check $baseline.Pending.execution.executionId);Find-Named '1 prueba seleccionada'|Out-Null;Find-Named 'Guardar digitación'|Out-Null
 Capture '02-barra-contextual';Invoke-Named 'Guardar digitación';Find-Named 'Confirmar y guardar'|Out-Null;Capture '03-confirmacion-cancelable';Invoke-Named 'Cancelar';Invoke-Named 'Desmarcar selección temporal';Wait-Check $baseline.Pending.execution.executionId $true 'Sin información'|Out-Null
}
if($Step -eq 'Settings'){
 PhReport;Invoke-Named 'Configurar tabla de Report List';Find-Named 'Columnas'|Out-Null;Capture '04-configuracion-compacta';[Windows.Forms.SendKeys]::SendWait('{ESC}')
 Open-Columns;Capture '05-submenu-columnas';Invoke-Named 'Restablecer columnas'
}
if($Step -eq 'Columns'){
 PhReport;Open-Columns;Invoke-Named 'Restablecer columnas';Start-Sleep -Milliseconds 250;Open-Columns;Click-Menu 'Estado de digitación';Find-Named 'Ordenar Report List por Estado de digitación'|Out-Null;Open-Columns;Click-Menu 'Cliente'
 Click-Point (Find-Named 'Ordenar Report List por JOB') -Right;Invoke-Named 'Mover columna a la izquierda';Click-Point (Find-Named 'Ordenar Report List por JOB') -Right;Invoke-Named 'Cambiar ancho de columna';Set-NamedValue 'Ancho de columna en píxeles' '200';Invoke-Named 'Aplicar ancho';Start-Sleep -Milliseconds 200
 $timer=[Diagnostics.Stopwatch]::StartNew();do{$p=Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Lims/ReportList') -Filter 'columns-*.json'|Sort-Object LastWriteTimeUtc -Descending|Select-Object -First 1;$layout=Get-Content $p.FullName -Raw|ConvertFrom-Json;$persisted=$layout.Columns[1].Key -eq 'Job' -and $layout.Columns[1].Width -eq 200 -and $layout.Columns.Key -notcontains 'Buyer' -and $layout.Columns.Key -contains 'Checked';if(-not $persisted){Start-Sleep -Milliseconds 150}}while(-not $persisted -and $timer.Elapsed.TotalSeconds -lt 10)
 if(-not $persisted){throw 'Column controls lost actual preferences'}
 $layout|ConvertTo-Json -Depth 6|Set-Content (Join-Path $out 'column-preferences.json') -Encoding UTF8;Capture '06-columnas-ancho-orden';Open-Columns;Invoke-Named 'Restablecer columnas'
}
if($Step -eq 'Navigation'){
 PhReport;Click-Point (Find-Named 'Digitación');(Find-Named 'Digitación').SetFocus();Start-Sleep -Milliseconds 150;[Windows.Forms.SendKeys]::SendWait('{RIGHT}');Find-Named 'Horarios de digitación'|Out-Null;Capture '08-chequeo-en-su-pestana'
 [Windows.Forms.SendKeys]::SendWait('{END}');Find-Named 'JOBs completamente digitados'|Out-Null;[Windows.Forms.SendKeys]::SendWait('{HOME}');Find-Named 'Ejecuciones de Report List'|Out-Null
 PhReport;$cell=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|Where-Object{$_.Current.Name -like 'F1 C1 *' -and -not $_.Current.IsOffscreen})|Select-Object -First 1;Click-Point $cell;[Windows.Forms.SendKeys]::SendWait('^{HOME}+{DOWN}+{RIGHT}+{RIGHT}^c');Start-Sleep -Milliseconds 250;$copy=Get-Clipboard -Raw;if(@($copy -split "`r?`n").Count -ne 2 -or $copy -notlike '*2026-10-15*2026-11-06*GUAT26022934*'){throw 'Native cell keyboard/copy regressed'};[Windows.Forms.SendKeys]::SendWait('{ESC}')
}
if($Step -eq 'Viewport'){
 $handle=[IntPtr]$window.Current.NativeWindowHandle;[ReportHeaderWindow]::ShowWindow($handle,9)|Out-Null;[ReportCorrectionDpi]::SetWindowPos($handle,[IntPtr]::Zero,30,30,$Width,$Height,0x40)|Out-Null;Start-Sleep -Milliseconds 600;PhReport
 $geometry=Assert-Header ($Width -ge 1366);Capture ('07-vista-'+$Width+'x'+$Height);$geometry|ConvertTo-Json|Set-Content (Join-Path $out ('geometry-'+$Width+'.json')) -Encoding UTF8
}
Connect-Api;$rows=@((Get-Json ('report-list?View=All&Search='+$baseline.Job)).items|Where-Object{$_.execution.planningTest -eq 'PH'})
foreach($before in @((Get-Content (Join-Path $workspace 'artifacts/report-list-usability/correction-audit.json') -Raw|ConvertFrom-Json).Rows)){$after=$rows|Where-Object{$_.execution.executionId -eq $before.execution.executionId};if(($after|ConvertTo-Json -Depth 12 -Compress) -ne ($before|ConvertTo-Json -Depth 12 -Compress)){throw 'Read-only visual acceptance changed real PH data'}}
@{Passed=$true;Step=$Step;Boundary='Existing authenticated native LIMS; restored local database; no saved-data changes';OriginalExecutionsUnchanged=$true;AssemblySha256=(Get-FileHash (Join-Path (Split-Path $process.Path) 'Lims.Desktop.dll')).Hash;ProcessId=$process.Id;Utc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json|Set-Content (Join-Path $out ('native-'+$Step+'.json')) -Encoding UTF8
