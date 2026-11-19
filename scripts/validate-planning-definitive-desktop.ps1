param([ValidateSet('Inspect','Ready','Preview','Sections','Confirm','Reviews','Dossier','Cells','History','Reimport','ImportedRows','Interactions','Export')][string]$Step='Inspect',[string]$SavedExportPath)
$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'validate-planning-desktop.ps1'));$reviewScriptsRoot=$PSScriptRoot
$begin=$source.IndexOf('$ErrorActionPreference=');$finish=$source.IndexOf("if(`$Action -eq 'Inspect')");Invoke-Expression $source.Substring($begin,$finish-$begin).Replace('$PSScriptRoot','$reviewScriptsRoot')
$process=Get-Process -Id $window.Current.ProcessId
if(-not $process.Path.StartsWith((Join-Path $workspace 'artifacts/planificacion-dev/desktop-validation-'),[StringComparison]::OrdinalIgnoreCase)){throw 'Solo cliente aislado'}
$evidence=Join-Path $local 'centro-definitivo/ui';New-Item -ItemType Directory -Path $evidence -Force|Out-Null
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class DefinitiveGeometry {
 [DllImport("user32.dll")]public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
 [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int w,int v,uint f);
 [DllImport("user32.dll")]public static extern void keybd_event(byte k,byte s,uint f,UIntPtr e);
}
'@
[DefinitiveGeometry]::SetThreadDpiAwarenessContext([IntPtr](-4))|Out-Null
function Capture([string]$name){
 $handle=[IntPtr]$window.Current.NativeWindowHandle;$rect=[PlanningCapture+Rect]::new();[PlanningCapture]::GetWindowRect($handle,[ref]$rect)|Out-Null
 [PlanningCapture]::SetForegroundWindow($handle)|Out-Null;$width=$rect.Right-$rect.Left;$height=$rect.Bottom-$rect.Top
 [DefinitiveGeometry]::SetWindowPos($handle,[IntPtr]::Zero,$rect.Left,$rect.Top,$width-1,$height,0x14)|Out-Null;Start-Sleep -Milliseconds 200
 [DefinitiveGeometry]::SetWindowPos($handle,[IntPtr]::Zero,$rect.Left,$rect.Top,$width,$height,0x14)|Out-Null;Start-Sleep -Milliseconds 700;Capture-Window $window $name
}
function Use-Window([string]$title){
 $deadline=[DateTime]::UtcNow.AddSeconds(50);do{
 $candidate=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id))|Where-Object{$_.Current.Name -eq $title}|Select-Object -First 1
 if($candidate){$script:window=$candidate;return};Start-Sleep -Milliseconds 200
 }while([DateTime]::UtcNow -lt $deadline);throw ('Ventana no disponible: '+$title)
}
function Combo([string]$name,[int]$index){
 $combo=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$name))|Where-Object{$_.Current.ControlType -eq [Windows.Automation.ControlType]::ComboBox -and -not $_.Current.IsOffscreen}|Select-Object -First 1
 if(-not $combo){throw ('Combo no disponible: '+$name)}
 $expand=$combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern);$expand.Expand();Start-Sleep -Milliseconds 100
 $items=@($combo.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem)))
 if($items.Count -eq 0){$items=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ClassNameProperty,'ComboBoxItem'))|Where-Object{-not $_.Current.IsOffscreen})}
 $items[$index].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();$expand.Collapse();Start-Sleep -Milliseconds 300
}
function Ready-Review(){
 Start-Sleep -Milliseconds 200
 $deadline=[DateTime]::UtcNow.AddSeconds(45);do{$list=Find-Named 'Tabla de revisión de importaciones';$new=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'Nueva importación'))|Where-Object{$_.Current.ControlType -eq [Windows.Automation.ControlType]::Button}|Select-Object -First 1;if($list.Current.IsEnabled -and (-not $new -or $new.Current.IsEnabled)){return};Start-Sleep -Milliseconds 150}while([DateTime]::UtcNow -lt $deadline);throw 'No terminó de cargar la bandeja'
}
function Select-FirstReview(){Ready-Review;$list=Find-Named 'Tabla de revisión de importaciones';$items=@($list.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem)));if($items.Count -eq 0){throw 'No hay fila'};$items[0].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()}
function Set-ReadyValue([string]$name,[string]$value){$deadline=[DateTime]::UtcNow.AddSeconds(15);do{try{Set-NamedValue $name $value;return}catch{Start-Sleep -Milliseconds 150}}while([DateTime]::UtcNow -lt $deadline);throw ('Campo no disponible: '+$name)}
function Begin-Import(){
 $deadline=[DateTime]::UtcNow.AddSeconds(45);do{
 $button=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'Nueva importación'))|Where-Object{$_.Current.ControlType -eq [Windows.Automation.ControlType]::Button -and $_.Current.IsEnabled -and -not $_.Current.IsOffscreen}|Select-Object -First 1
 if($button){$button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke();Find-Named 'Summary XLSX'|Out-Null;return};Start-Sleep -Milliseconds 150
 }while([DateTime]::UtcNow -lt $deadline);throw 'No terminó la carga inicial'
}
function Pass([string]$name){@{Scenario=$name;Passed=$true;Utc=[DateTime]::UtcNow.ToString('O');Boundary='Actual native client with actual XLSX and persisted lims_planning_dev'}|ConvertTo-Json|Set-Content (Join-Path (Split-Path $evidence) ('native-'+$name+'.json')) -Encoding UTF8;'PASS '+$name}
if($Step -eq 'Inspect'){Inspect-Window;exit}
if($Step -in @('Ready','Preview','Reimport')){
 Invoke-Named 'Importaciones';Begin-Import;Capture '01-seleccionar-archivos'
 if($Step -eq 'Ready'){Pass 'Ready';exit}
 $fs=$source.IndexOf('    function Select-SourceFile(');$fe=$source.IndexOf("    Select-SourceFile 'Summary XLSX'",$fs);Invoke-Expression $source.Substring($fs,$fe-$fs)
 Select-SourceFile 'Summary XLSX' (Join-Path $env:USERPROFILE 'Downloads/a0_-_pro-invoice_summary_17Nov2026045852237.xlsx')
 Select-SourceFile 'Report List XLSX' (Join-Path $env:USERPROFILE 'Downloads/a0_-_delay_report_list_based_on_due_date_17Nov2026045945888.xlsx')
 Invoke-Named 'Validar archivos'
 if($Step -eq 'Reimport'){Use-Window 'Resumen de importación';Capture '12-reimportacion-sin-duplicados';Capture '07-resumen-importacion';Pass 'Reimport';exit}
 Use-Window 'Revisa antes de agregar';Capture '02-revision-emergencias';Pass 'Preview';exit
}
if($Step -eq 'Sections'){
 Use-Window 'Revisa antes de agregar'
 foreach($section in @(@(1,'03-revision-solo-summary'),@(2,'04-revision-solo-report'),@(3,'05-revision-buyer-vendor'),@(4,'06-revision-equivalencias'))){Combo 'Sección de revisión previa' $section[0];Capture $section[1]}
 Pass 'Sections';exit
}
if($Step -eq 'Confirm'){
 Use-Window 'Revisa antes de agregar'
 $check=Find-Named 'Importar registros válidos y dejar explícitamente las equivalencias pendientes'
 if($check.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne [Windows.Automation.ToggleState]::On){$check.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()}
 Invoke-Named 'Confirmar importación';Use-Window 'Resumen de importación';Capture '07-resumen-importacion';Pass 'Confirm';exit
}
if($Step -eq 'Reviews'){
 $summary=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id))|Where-Object{$_.Current.Name -eq 'Resumen de importación'}|Select-Object -First 1
 if($summary){$window=$summary;Invoke-Named 'Revisar pendientes';Use-Window 'LIMS'}else{Invoke-Named 'Bandejas de revisión'}
 Ready-Review;Capture '08-bandejas-lote-actual';Pass 'Reviews';exit
}
if($Step -eq 'Dossier'){
 Invoke-Named 'Bandejas de revisión';Ready-Review;Combo 'Situación de revisión' 4;Set-NamedValue 'Buscar JOB en revisión' 'GUAT25002016-REV4-REV5-RE';Invoke-Named 'Buscar';Start-Sleep -Milliseconds 200;Select-FirstReview;Invoke-Named 'Expediente'
 Use-Window 'Estado del JOB · GUAT25002016-REV4-REV5-RE';Capture '09-expediente-desde-bandeja';Pass 'Dossier';exit
}
if($Step -eq 'Cells'){
 Invoke-Named 'Bandejas de revisión';Ready-Review;Invoke-Named 'Limpiar';Ready-Review;Invoke-Named 'Selección de celdas';Capture '10-seleccion-celdas';Pass 'Cells';exit
}
if($Step -eq 'ImportedRows'){
 Use-Window 'Resumen de importación';Invoke-Named 'Ver registros importados';Use-Window 'LIMS';Start-Sleep -Milliseconds 700;Find-Named 'Restablecer filtros'|Out-Null;Capture '13-consulta-del-lote';Pass 'ImportedRows';exit
}
if($Step -eq 'Interactions'){
 foreach($child in [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id))){if($child.Current.Name -like 'Estado del JOB *'){$child.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()}}
 [PlanningCapture]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle)|Out-Null;Start-Sleep -Milliseconds 300
 function Wait-Text([string]$pattern){$deadline=[DateTime]::UtcNow.AddSeconds(35);do{foreach($e in $window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)){if(-not $e.Current.IsOffscreen -and $e.Current.Name -like $pattern){return}};Start-Sleep -Milliseconds 100}while([DateTime]::UtcNow -lt $deadline);throw ('Texto no disponible: '+$pattern)}
 function Send-Key([byte]$key,[bool]$up=$false){if([PlanningCapture]::GetForegroundWindow() -ne [IntPtr]$window.Current.NativeWindowHandle){throw 'Foco fuera del cliente aislado'};[DefinitiveGeometry]::keybd_event($key,0,$(if($up){2}else{0}),[UIntPtr]::Zero);Start-Sleep -Milliseconds 50}
 Invoke-Named 'Bandejas de revisión';Ready-Review;Invoke-Named 'Solo Summary';Combo 'Situación de revisión' 1;Invoke-Named 'Limpiar';Ready-Review;Wait-Text '1–100 de *'
 Invoke-Named 'Siguiente';Wait-Text '101–200 de *';Invoke-Named 'Anterior';Wait-Text '1–100 de *'
 [PlanningCapture]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle)|Out-Null;Start-Sleep -Milliseconds 200
 Invoke-Named 'Filtrar revisión Estado';Set-ReadyValue 'Valor del filtro de revisión Estado' 'Report Closed';Invoke-Named 'Aplicar columna';Ready-Review
 Invoke-Named 'Ordenar revisión por Fecha ingreso';Ready-Review;Select-FirstReview
 $list=Find-Named 'Tabla de revisión de importaciones';$items=@($list.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem)))
 $items[1].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).AddToSelection();Invoke-Named 'Copiar';Add-Type -AssemblyName System.Windows.Forms
 $copied=[Windows.Forms.Clipboard]::GetText();if(@($copied -split "`r?`n").Count -ne 2){throw 'Copia de filas incorrecta'};Capture '14-filtro-orden-copia-filas'
 Invoke-Named 'Selección de celdas';$cells=Find-Named 'Celdas de bandeja de revisión';[PlanningCapture]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle)|Out-Null;$cells.SetFocus()
 # Inject actual mouse movement: SetCursorPos alone defers pointer updates until release in this native control.
 $first=Find-Named (($cells.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|Where-Object{$_.Current.Name -like 'F1 C1 *'}|Select-Object -First 1).Current.Name)
 $last=Find-Named (($cells.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|Where-Object{$_.Current.Name -like 'F3 C3 *'}|Select-Object -First 1).Current.Name)
 $a=$first.Current.BoundingRectangle;$b=$last.Current.BoundingRectangle
 if([PlanningCapture]::GetForegroundWindow() -ne [IntPtr]$window.Current.NativeWindowHandle){throw 'Foco fuera del cliente aislado'}
 [PlanningCapture]::SetCursorPos([int]($a.Left+5),[int]($a.Top+$a.Height/2))|Out-Null
 [PlanningCapture]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
 $screen=[Windows.Forms.SystemInformation]::VirtualScreen
 try{for($i=1;$i -le 12;$i++){
  $x=$a.Left+5+($b.Left-$a.Left)*$i/12;$y=$a.Top+$a.Height/2+($b.Top+$b.Height/2-$a.Top-$a.Height/2)*$i/12
  [PlanningCapture]::mouse_event(0xE001,[uint32](($x-$screen.Left)*65535/($screen.Width-1)),[uint32](($y-$screen.Top)*65535/($screen.Height-1)),0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 80
 };Start-Sleep -Milliseconds 250}finally{[PlanningCapture]::mouse_event(4,0,0,0,[UIntPtr]::Zero)}
 Send-Key 0x11;Send-Key 0x43;Send-Key 0x43 $true;Send-Key 0x11 $true;Start-Sleep -Milliseconds 200
 $range=[Windows.Forms.Clipboard]::GetText();$lines=@($range -split "`r?`n");if($lines.Count -ne 3 -or @($lines[0] -split "`t").Count -ne 3){throw 'El rango 3 x 3 no se copió correctamente'};Capture '10-seleccion-celdas'
 Invoke-Named 'Selección de celdas'
 foreach($name in @('Solo Report List','Sin Buyer/Vendor','Sin equivalencia','Pruebas por agregar','Datos incompletos','Retenidos y resueltos','Solo Summary')){Invoke-Named $name;Ready-Review}
 Combo 'Situación de revisión' 1;Ready-Review;Invoke-Named 'Limpiar';Ready-Review
 [DefinitiveGeometry]::SetWindowPos([IntPtr]$window.Current.NativeWindowHandle,[IntPtr]::Zero,0,0,1280,800,0x40)|Out-Null;Capture '15-ventana-reducida'
 [DefinitiveGeometry]::SetWindowPos([IntPtr]$window.Current.NativeWindowHandle,[IntPtr]::Zero,0,0,1920,1080,0x40)|Out-Null;Capture '08-bandejas-lote-actual'
 Pass 'Interactions';exit
}
if($Step -eq 'Export'){
 if($SavedExportPath){
  # The native save picker may be completed separately; validate the actual saved file and the client success state.
  Find-Named 'Listado completo exportado; incluye todas las páginas del alcance seleccionado.'|Out-Null
  $interaction=Get-Content (Join-Path (Split-Path $evidence) 'native-Interactions.json') -Raw|ConvertFrom-Json
  $file=Get-Item -LiteralPath $SavedExportPath
  if($file.LastWriteTimeUtc -lt [DateTime]::Parse($interaction.Utc).ToUniversalTime()){throw 'El CSV es anterior al recorrido nativo actual'}
  $hash=(Get-FileHash -LiteralPath $SavedExportPath).Hash;$expected=(Get-FileHash (Join-Path (Split-Path $evidence) 'solo-summary-lote.csv')).Hash
  $rows=@(Import-Csv -LiteralPath $SavedExportPath -Encoding UTF8).Count
  if($rows -ne 14722 -or $hash -ne $expected){throw 'El CSV nativo no coincide con el resultado completo de la API'}
  Capture '16-exportacion-completa';Pass 'Export'
  $resultPath=Join-Path (Split-Path $evidence) 'native-Export.json';$proof=Get-Content $resultPath -Raw|ConvertFrom-Json
  $proof|Add-Member Rows $rows;$proof|Add-Member Sha256 $hash;$proof|Add-Member SavePicker 'Completed separately in the native client; saved file and client success message verified automatically'
  $proof|ConvertTo-Json|Set-Content $resultPath -Encoding UTF8
  Write-Output ('Filas CSV nativas verificadas: '+$rows);exit
 }
 Invoke-Named 'Exportar';Invoke-Named 'Registros filtrados'
 $deadline=[DateTime]::UtcNow.AddSeconds(45);$dialog=$null
 do{
  foreach($candidate in $window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ClassNameProperty,'#32770'))){
   $edit=$candidate.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,'1148'));if($edit){$dialog=$candidate;break}
  };if(-not $dialog){Start-Sleep -Milliseconds 150}
 }while(-not $dialog -and [DateTime]::UtcNow -lt $deadline)
 if(-not $dialog){throw 'No se abrió Guardar CSV'}
 $input=$edit.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Edit));if(-not $input){$input=$edit}
 $path=Join-Path (Split-Path $evidence) ('native-export-'+[Guid]::NewGuid().ToString('N')+'.csv')
 $input.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($path)
 $save=$dialog.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ClassNameProperty,'Button'))|Where-Object{$_.Current.AutomationId -eq '1' -and $_.Current.NativeWindowHandle -ne 0}|Select-Object -First 1
 if(-not $save){throw 'No se encontró Guardar'}
 [PlanningCapture]::SendMessage([IntPtr]$save.Current.NativeWindowHandle,0xF5,[IntPtr]::Zero,[IntPtr]::Zero)|Out-Null
 $deadline=[DateTime]::UtcNow.AddSeconds(30);do{if((Test-Path -LiteralPath $path) -and (Get-Item -LiteralPath $path).Length -gt 0){break};Start-Sleep -Milliseconds 150}while([DateTime]::UtcNow -lt $deadline)
 $rows=@(Import-Csv -LiteralPath $path -Encoding UTF8).Count;if($rows -ne 14722){throw ('Exportación nativa incompleta: '+$rows)}
 Capture '16-exportacion-completa';Pass 'Export';Write-Output ('Filas CSV guardadas por el cliente: '+$rows);exit
}
if($Step -eq 'History'){Invoke-Named 'Importaciones';Invoke-Named 'Historial';Capture '11-historial';Pass 'History';exit}
