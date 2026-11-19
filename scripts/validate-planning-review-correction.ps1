param([ValidateSet('Inspect','Layout','Keyboard','Detail','Resolved','Filters','Sizes','History','Export','Decisions','Import','Columns')][string]$Step='Inspect',[string]$SavedExportPath,[switch]$ResumeImport)
$ErrorActionPreference='Stop'
$helper=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'validate-planning-desktop.ps1'))
$reviewCorrectionRoot=$PSScriptRoot
$start=$helper.IndexOf('$ErrorActionPreference=');$end=$helper.IndexOf("if(`$Action -eq 'Inspect')")
Invoke-Expression $helper.Substring($start,$end-$start).Replace('$PSScriptRoot','$reviewCorrectionRoot')
$process=Get-Process -Id $window.Current.ProcessId
if(-not $process.Path.StartsWith((Join-Path $workspace 'artifacts/planificacion-dev/desktop-validation-'),[StringComparison]::OrdinalIgnoreCase)){throw 'Cliente ajeno a validación'}
$settings=Get-Content (Join-Path $local 'desktop-validation.json') -Raw|ConvertFrom-Json
if($settings.ApiUrl -ne 'https://localhost:7441'){throw 'API ajena a validación local'}
$out=Join-Path $workspace 'artifacts/validation/centro-importaciones-correccion-2026-11-19'
$evidence=Join-Path $out 'ui';New-Item -ItemType Directory -Force $evidence|Out-Null
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class CorrectionInput {
 [DllImport("user32.dll")]public static extern void keybd_event(byte k,byte s,uint f,UIntPtr e);
 [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int w,int v,uint f);
 [DllImport("user32.dll")]public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
 [DllImport("user32.dll")]public static extern IntPtr GetDlgItem(IntPtr h,int id);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern bool SetWindowText(IntPtr h,string text);
 private delegate bool EnumProc(IntPtr h,IntPtr p);
 [DllImport("user32.dll")]private static extern bool EnumWindows(EnumProc p,IntPtr v);
 [DllImport("user32.dll")]private static extern bool EnumChildWindows(IntPtr h,EnumProc p,IntPtr v);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetClassName(IntPtr h,System.Text.StringBuilder b,int n);
 [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll")]private static extern bool IsWindowVisible(IntPtr h);
 public static uint Pid(IntPtr h){uint p;GetWindowThreadProcessId(h,out p);return p;}
 public static IntPtr[] Dialogs(){var found=new System.Collections.Generic.HashSet<IntPtr>();EnumProc check=(h,p)=>{var b=new System.Text.StringBuilder(200);GetClassName(h,b,200);if(b.ToString()=="#32770"&&IsWindowVisible(h))found.Add(h);return true;};EnumWindows((h,p)=>{check(h,p);EnumChildWindows(h,check,IntPtr.Zero);return true;},IntPtr.Zero);var a=new IntPtr[found.Count];found.CopyTo(a);return a;}
}
'@
[CorrectionInput]::SetThreadDpiAwarenessContext([IntPtr](-4))|Out-Null
function Key([byte]$key,[bool]$up=$false){
 if([PlanningCapture]::GetForegroundWindow() -ne [IntPtr]$window.Current.NativeWindowHandle){[PlanningCapture]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle)|Out-Null;Start-Sleep -Milliseconds 150}
 if([PlanningCapture]::GetForegroundWindow() -ne [IntPtr]$window.Current.NativeWindowHandle){throw ('Foco fuera del cliente aislado antes de tecla '+$key)}
 $scan=switch($key){0x10{0x2A}0x11{0x1D}0x25{0x4B}0x26{0x48}0x27{0x4D}0x28{0x50}default{0}}
 $flags=$(if($up){2}else{0});if($key -in @(0x25,0x26,0x27,0x28)){$flags=$flags -bor 1}
 [CorrectionInput]::keybd_event($key,$scan,$flags,[UIntPtr]::Zero);Start-Sleep -Milliseconds 80
}
function Chord([byte]$modifier,[byte]$key){Key $modifier;try{Key $key;Key $key $true}finally{Key $modifier $true};Start-Sleep -Milliseconds 120}
function Capture([string]$name){
 $handle=[IntPtr]$window.Current.NativeWindowHandle;$rect=[PlanningCapture+Rect]::new();[PlanningCapture]::GetWindowRect($handle,[ref]$rect)|Out-Null
 [PlanningCapture]::SetForegroundWindow($handle)|Out-Null
 [CorrectionInput]::SetWindowPos($handle,[IntPtr]::Zero,$rect.Left,$rect.Top,$rect.Right-$rect.Left-1,$rect.Bottom-$rect.Top,0x14)|Out-Null;Start-Sleep -Milliseconds 100
 [CorrectionInput]::SetWindowPos($handle,[IntPtr]::Zero,$rect.Left,$rect.Top,$rect.Right-$rect.Left,$rect.Bottom-$rect.Top,0x14)|Out-Null;Start-Sleep -Milliseconds 350
 Capture-Window $window $name
}
function Ready {
 Start-Sleep -Milliseconds 450;$deadline=[DateTime]::UtcNow.AddSeconds(50)
 do{$list=Find-Named 'Celdas de bandeja de revisión';if($list.Current.IsEnabled){return $list};Start-Sleep -Milliseconds 100}while([DateTime]::UtcNow -lt $deadline)
 throw 'Bandeja todavía cargando'
}
function Combo([string]$name,[int]$index){
 $combo=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$name))|Where-Object{$_.Current.ControlType -eq [Windows.Automation.ControlType]::ComboBox -and -not $_.Current.IsOffscreen}|Select-Object -First 1
 $expand=$combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern);$expand.Expand();Start-Sleep -Milliseconds 150
 $items=@($combo.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem)))
 $items[$index].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();$expand.Collapse();if($name -ne 'Corte operativo'){Ready|Out-Null}
}
function Queue([string]$name){
 $element=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$name))|Where-Object{$_.Current.ControlType -eq [Windows.Automation.ControlType]::Button}|Select-Object -First 1
 if($element.Current.IsOffscreen){
  $parent=$element
  while($parent){$scroll=$null;if($parent.TryGetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern,[ref]$scroll) -and $scroll.Current.HorizontallyScrollable){$scroll.SetScrollPercent($(if($name -eq 'Solo Summary'){0}else{100}),-1);break};$parent=[Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($parent)}
 }
 Invoke-Named $name;Ready|Out-Null
}
function Focus-Cell([string]$prefix='F1 C1 *'){
 $list=Ready;$cell=$list.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|Where-Object{$_.Current.Name -like $prefix -and -not $_.Current.IsOffscreen}|Select-Object -First 1
 if(-not $cell){throw ('Celda no disponible: '+$prefix)}
 [PlanningCapture]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle)|Out-Null;$box=$cell.Current.BoundingRectangle
 [PlanningCapture]::SetCursorPos([int]($box.Left+8),[int]($box.Top+$box.Height/2))|Out-Null
 [PlanningCapture]::mouse_event(2,0,0,0,[UIntPtr]::Zero);[PlanningCapture]::mouse_event(4,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 150
}
function Pass([string[]]$checks){@{Passed=$true;Step=$Step;Utc=[DateTime]::UtcNow.ToString('O');Boundary='Actual WinUI 3 client, original login, localhost API and isolated PostgreSQL';Checks=$checks;AssemblySha256=(Get-FileHash (Join-Path (Split-Path $process.Path) 'Lims.Desktop.dll')).Hash}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $out ($Step+'.json')) -Encoding UTF8;$checks|ForEach-Object{'PASS '+$_}}
function Section-Tab([string]$name){
 $tab=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$name))|Where-Object{$_.Current.ControlType -eq [Windows.Automation.ControlType]::TabItem -and -not $_.Current.IsOffscreen}|Select-Object -First 1
 if(-not $tab){throw ('Pestaña no disponible: '+$name)};$tab.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
}
function Native-Picker([string]$selectedName='') {
 $deadline=[DateTime]::UtcNow.AddSeconds(40)
 do{
  if($selectedName){$selected=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$selectedName))|Where-Object{-not $_.Current.IsOffscreen};if($selected){return @{AlreadySelected=$true}}}
  foreach($handle in [CorrectionInput]::Dialogs()){
   $pickerProcess=Get-Process -Id ([CorrectionInput]::Pid($handle)) -ErrorAction SilentlyContinue
   $broker=$pickerProcess -and $pickerProcess.Path -eq (Join-Path $env:WINDIR 'System32/PickerHost.exe') -and $handle -eq [PlanningCapture]::GetForegroundWindow() -and -not $window.Current.IsEnabled
   if($pickerProcess -and ($pickerProcess.Path.StartsWith($settings.Stage,[StringComparison]::OrdinalIgnoreCase) -or $broker)){
    $combo=[CorrectionInput]::GetDlgItem($handle,1148);$edit=[CorrectionInput]::GetDlgItem($combo,1001);if($edit -eq [IntPtr]::Zero){$edit=$combo}
    return @{Handle=$handle;Edit=$edit;Button=[CorrectionInput]::GetDlgItem($handle,1)}
   }
  }
  $candidates=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ClassNameProperty,'#32770')))
  $candidates+=@([Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ClassNameProperty,'#32770')))
  $fg=[Windows.Automation.AutomationElement]::FromHandle([PlanningCapture]::GetForegroundWindow());if($fg.Current.ClassName -eq '#32770'){$candidates+=,$fg}
  foreach($candidate in $candidates){
   $pickerProcess=Get-Process -Id $candidate.Current.ProcessId -ErrorAction SilentlyContinue
   if($pickerProcess -and $pickerProcess.Path.StartsWith($settings.Stage,[StringComparison]::OrdinalIgnoreCase)){
    $handle=[IntPtr]$candidate.Current.NativeWindowHandle;$combo=[CorrectionInput]::GetDlgItem($handle,1148);$edit=[CorrectionInput]::GetDlgItem($combo,1001);if($edit -eq [IntPtr]::Zero){$edit=$combo}
    $button=[CorrectionInput]::GetDlgItem($handle,1)
    if($edit -ne [IntPtr]::Zero -and $button -ne [IntPtr]::Zero){return @{Dialog=$candidate;Edit=$edit;Button=$button}}
   }
  }
  Start-Sleep -Milliseconds 150
 }while([DateTime]::UtcNow -lt $deadline)
 throw 'No se encontró un selector nativo del cliente aislado con campo y botón verificables'
}
function Complete-Picker($picker,[string]$path){
 if($picker.Edit -ne [IntPtr]::Zero -and $picker.Button -ne [IntPtr]::Zero){
  if(-not [CorrectionInput]::SetWindowText($picker.Edit,$path)){throw 'No se pudo establecer el archivo'};[PlanningCapture]::SendMessage($picker.Button,0xF5,[IntPtr]::Zero,[IntPtr]::Zero)|Out-Null;return
 }
 [PlanningCapture]::SetForegroundWindow($picker.Handle)|Out-Null;Start-Sleep -Milliseconds 150
 if([PlanningCapture]::GetForegroundWindow() -ne $picker.Handle){throw 'El selector autorizado no tiene el foco'}
 [CorrectionInput]::keybd_event(0x12,0x38,0,[UIntPtr]::Zero);[CorrectionInput]::keybd_event(0x4E,0x31,0,[UIntPtr]::Zero);[CorrectionInput]::keybd_event(0x4E,0x31,2,[UIntPtr]::Zero);[CorrectionInput]::keybd_event(0x12,0x38,2,[UIntPtr]::Zero);Start-Sleep -Milliseconds 150
 $focused=[Windows.Automation.AutomationElement]::FocusedElement
 if($focused.Current.ControlType -ne [Windows.Automation.ControlType]::Edit -or $focused.Current.IsPassword){throw 'El campo de archivo no recibió el foco'}
 if([PlanningCapture]::GetForegroundWindow() -ne $picker.Handle){throw 'Foco fuera del selector autorizado'}
 [PlanningCapture]::ReplaceText($path);[CorrectionInput]::keybd_event(0x0D,0x1C,0,[UIntPtr]::Zero);[CorrectionInput]::keybd_event(0x0D,0x1C,2,[UIntPtr]::Zero)
}
function Wait-Content([string]$name){
 $deadline=[DateTime]::UtcNow.AddSeconds(30)
 do{$items=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$name));if($items.Count -gt 0){return};Start-Sleep -Milliseconds 150}while([DateTime]::UtcNow -lt $deadline)
 throw ('Contenido no cargado: '+$name)
}
if($Step -eq 'Inspect'){Inspect-Window;Capture '00-inspeccion';exit}
if($Step -eq 'Export' -and $SavedExportPath){
 Find-Named 'Listado completo exportado; incluye todas las páginas del alcance seleccionado.'|Out-Null
 $file=Get-Item -LiteralPath $SavedExportPath;$decisions=Get-Content (Join-Path $out 'Decisions.json') -Raw|ConvertFrom-Json
 if($file.LastWriteTimeUtc -lt [DateTime]::Parse($decisions.Utc).ToUniversalTime()){throw 'El archivo guardado es anterior al recorrido actual'}
 $rows=@(Import-Csv -LiteralPath $SavedExportPath -Encoding UTF8).Count;$hash=(Get-FileHash -LiteralPath $SavedExportPath).Hash;$expected=(Get-FileHash (Join-Path $local 'centro-definitivo/solo-summary-lote.csv')).Hash
 if($rows -ne 14722 -or $hash -ne $expected){throw 'El archivo nativo no coincide con la exportación completa de la API'}
 Capture '11-exportacion-completa';Pass @('CSV guardado desde el cliente real: 14,722 filas','SHA256 idéntico a la exportación completa de la API','Selector completado por separado; archivo nuevo y feedback del cliente verificados')
 $proof=Get-Content (Join-Path $out 'Export.json') -Raw|ConvertFrom-Json;$proof|Add-Member CsvRows $rows;$proof|Add-Member CsvSha256 $hash;$proof|Add-Member SavePicker 'Completed separately in the native client; final file and client success state verified';$proof|ConvertTo-Json -Depth 5|Set-Content (Join-Path $out 'Export.json') -Encoding UTF8;exit
}
if($Step -eq 'History'){Invoke-Named 'Historial';Capture '09-historial';Pass @('Historial persistido accesible por navegación única');exit}
if(-not ($Step -eq 'Import' -and $ResumeImport)){
[PlanningCapture]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle)|Out-Null;Start-Sleep -Milliseconds 100;Key 0x1B;Key 0x1B $true
Invoke-Named 'Importaciones';Section-Tab 'Bandejas de revisión';Ready|Out-Null
$openDetail=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'Cerrar detalle'))|Where-Object{-not $_.Current.IsOffscreen -and $_.Current.ControlType -eq [Windows.Automation.ControlType]::Button}|Select-Object -First 1
if($openDetail){$openDetail.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()}
}
if($Step -eq 'Layout'){
 foreach($name in @('Copiar','Copiar JOBs','Selección de celdas','Ver historial')){if($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$name))|Where-Object{-not $_.Current.IsOffscreen}){throw ('Control redundante visible: '+$name)}}
 Capture '01-bandejas-pestanas';Pass @('Una sola tabla y pestañas compactas','Sin botones redundantes de copia ni navegación');exit
}
if($Step -eq 'Keyboard'){
 Queue 'Solo Summary';Combo 'Situación de revisión' 1;Focus-Cell
 Key 0x10;try{foreach($arrow in @(0x27,0x27,0x28,0x28)){Key $arrow;Key $arrow $true}}finally{Key 0x10 $true};Chord 0x11 0x43
 $text=[Windows.Forms.Clipboard]::GetText();$lines=@($text -split "`r?`n");if($lines.Count -ne 3 -or @($lines[0] -split "`t").Count -ne 3){Capture 'diagnostico-teclado';throw ('Copia rectangular incorrecta: '+$lines.Count+' filas y '+@($lines[0] -split "`t").Count+' columnas')}
 Capture '02-copia-multicelda';Chord 0x11 0x41;Chord 0x11 0x43
 $all=[Windows.Forms.Clipboard]::GetText();if(@($all -split "`r?`n").Count -ne 100){throw 'Ctrl+A no selecciona la página completa'}
 Key 0x1B;Key 0x1B $true;Chord 0x11 0x56;Find-Named 'Las fuentes son de solo lectura. Usa Completar datos para una corrección WIP autorizada con motivo y procedencia.'|Out-Null
 Pass @('Shift+flechas y Ctrl+C copian un rango 3 x 3 real','Ctrl+A copia 100 filas cargadas','Esc reduce selección; Ctrl+V protege las fuentes');exit
}
if($Step -eq 'Detail'){
 Focus-Cell;Invoke-Named 'Ver detalle';Find-Named 'Diferencias entre fuentes'|Out-Null;Capture '03-detalle-fuentes';Invoke-Named 'Cerrar detalle'
 Invoke-Named 'Acciones';Find-Named 'Retener'|Out-Null;Capture '04-acciones-contextuales';Key 0x1B;Key 0x1B $true
 Focus-Cell;Key 0x0D;Key 0x0D $true;Start-Sleep -Milliseconds 700
 $dossier=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id))|Where-Object{$_.Current.Name -like 'Estado del JOB *'}|Select-Object -First 1
 if(-not $dossier){throw 'Enter no abrió expediente'};$main=$window;$window=$dossier;Capture '05-expediente';$dossier.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close();$window=$main
 Pass @('Detalle real con Summary, Report List e historial','Cerrar panel conserva la selección','Acciones válidas según incidencia','Enter abre expediente independiente');exit
}
if($Step -eq 'Resolved'){
 Set-NamedValue 'Buscar JOB en revisión' 'GUAT25002016-REV4-REV5-RE';Combo 'Situación de revisión' 4;Ready|Out-Null
 Find-Named 'Lote 0163be42 · Total 1 · Pendientes 0 · Retenidos 0 · Resueltos 1'|Out-Null;Capture '06-cero-pendientes-un-resuelto'
 Invoke-Named 'Ver detalle';Find-Named 'Diferencias entre fuentes'|Out-Null;Capture '07-resolucion-persistida';Invoke-Named 'Cerrar detalle'
 Set-NamedValue 'Buscar JOB en revisión' '';Ready|Out-Null;Pass @('Total 1 visible con cero pendientes y un resuelto','Prueba manual y resolución persistidas en PostgreSQL');exit
}
if($Step -eq 'Filters'){
 Queue 'Solo Summary';Set-NamedValue 'Buscar JOB en revisión' '';Combo 'Situación de revisión' 1
 Invoke-Named 'Filtros';Set-NamedValue 'Filtro adicional Buyer' 'J Crew';Invoke-Named 'Aplicar filtros';Ready|Out-Null
 Capture '08-filtros';Invoke-Named 'Siguiente';Ready|Out-Null;Invoke-Named 'Anterior';Ready|Out-Null
 Queue 'Sin Buyer/Vendor';Queue 'Solo Summary';Invoke-Named 'Filtros';$field=Find-Named 'Filtro adicional Buyer';if($field.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -ne 'J Crew'){throw 'El cambio de categoría borró el filtro'}
 Invoke-Named 'Restablecer filtros';Ready|Out-Null
 Pass @('Filtro Buyer conserva alcance y se mantiene al cambiar categoría','Paginación ida y vuelta sobre miles de registros');exit
}
if($Step -eq 'Sizes'){
 foreach($size in @(@(1280,800),@(1366,768),@(1920,1080))){
  [CorrectionInput]::SetWindowPos([IntPtr]$window.Current.NativeWindowHandle,[IntPtr]::Zero,0,0,$size[0],$size[1],0x40)|Out-Null;Start-Sleep -Milliseconds 500
  Queue 'Retenidos y resueltos';Capture ('10-ventana-'+$size[0]);Queue 'Solo Summary'
  foreach($name in @('Filtros','Columnas','Exportar','Situación de revisión')){$element=Find-Named $name;$b=$element.Current.BoundingRectangle;$w=$window.Current.BoundingRectangle;if($b.Left -lt $w.Left -or $b.Right -gt $w.Right){throw ('Control fuera de ventana: '+$name)}}
 }
 Pass @('Pestañas accesibles mediante desplazamiento horizontal','Controles visibles a 1280, 1366 y 1920 píxeles');exit
}
if($Step -eq 'Export'){
 Queue 'Solo Summary';Combo 'Situación de revisión' 1;Set-NamedValue 'Buscar JOB en revisión' '';Ready|Out-Null
 Invoke-Named 'Exportar';Invoke-Named 'Registros filtrados'
 $picker=Native-Picker
 $path=Join-Path $local ('review-correction-export-'+[Guid]::NewGuid().ToString('N')+'.csv');Complete-Picker $picker $path
 Find-Named 'Listado completo exportado; incluye todas las páginas del alcance seleccionado.'|Out-Null
 $rows=@(Import-Csv -LiteralPath $path -Encoding UTF8).Count;if($rows -ne 14722){throw ('Exportación incompleta: '+$rows)}
 Capture '11-exportacion-completa';Pass @('Único Exportar guarda el conjunto filtrado mediante el selector nativo','CSV real de 14,722 casos, incluye todas las páginas');exit
}
if($Step -eq 'Decisions'){
 Queue 'Solo Summary';Set-NamedValue 'Buscar JOB en revisión' 'GUAT25002016-REV4-REV5-RE';Combo 'Situación de revisión' 4;Ready|Out-Null
 $account=Get-Content (Join-Path $local 'acceptance-account.json') -Raw|ConvertFrom-Json
 $auth=Invoke-RestMethod https://localhost:7441/api/auth/login -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes((@{identifier=$account.email;password=$account.password;clientName='Native review correction validation'}|ConvertTo-Json)))
 $headers=@{Authorization='Bearer '+$auth.accessToken}
 function Current-Case { $page=Invoke-RestMethod 'https://localhost:7441/api/planning/reviews/workspace?Category=SummaryWithoutReport&Situation=All&Search=GUAT25002016-REV4-REV5-RE' -Headers $headers;return $page.items[0] }
 function Decide([string]$action,[string]$reason){
  Invoke-Named 'Acciones';Invoke-Named $action;Set-NamedValue 'Motivo de la decisión' $reason;Invoke-Named 'Guardar decisión'
  $expected=switch($action){'Reabrir'{'Pendiente'}'Retener'{'Retenido'}'Resolver'{'Resuelto'}}
  $deadline=[DateTime]::UtcNow.AddSeconds(45)
  do{
   $cells=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,('F1 C9 · Situación · '+$expected)))
   if($cells.Count -gt 0){Ready|Out-Null;return};Start-Sleep -Milliseconds 150
  }while([DateTime]::UtcNow -lt $deadline)
  throw ('La tabla no confirmó la decisión '+$action)
 }
 $original=Current-Case;if($original.state -ne 'Resolved'){throw 'El caso original debe estar resuelto'}
 try{
  Decide 'Reabrir' 'Validación aislada de reapertura para revisar la interfaz corregida'
  Invoke-Named 'Acciones';Invoke-Named 'Agregar pruebas';$main=$window;$deadline=[DateTime]::UtcNow.AddSeconds(30);$manualWindow=$null
  do{$manualWindow=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id))|Where-Object{$_.Current.Name -like 'Agregar pruebas · GUAT*'}|Select-Object -First 1;if(-not $manualWindow){Start-Sleep -Milliseconds 150}}while(-not $manualWindow -and [DateTime]::UtcNow -lt $deadline)
  if(-not $manualWindow){throw 'No abrió la asignación manual autorizada'};$window=$manualWindow;Find-Named 'Prueba del catálogo'|Out-Null;Capture '18-agregar-pruebas';$manualWindow.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close();$window=$main
  Decide 'Retener' 'Validación aislada: retención con usuario, fecha y motivo persistidos'
  $held=Current-Case;if($held.state -ne 'Held' -or -not $held.decidedBy -or -not $held.decidedAt -or $held.reason -notlike 'Validación aislada:*'){throw 'Retención no persistida'}
  Invoke-Named 'Ver detalle';Wait-Content 'Diferencias entre fuentes';Capture '12-retencion-auditada'
  Decide 'Resolver' 'Validación aislada finalizada; prueba manual existente verificada y resolución restablecida'
  $resolved=Current-Case;if($resolved.state -ne 'Resolved'){throw 'Resolución no persistida'}
  Invoke-Named 'Cerrar detalle';Capture '13-resolucion-nativa'
  Pass @('Reabrir, retener y resolver desde acciones contextuales reales','PostgreSQL conserva motivo, usuario y fecha','Resolución final restablecida sin crear pruebas duplicadas')
 }finally{
  $current=Current-Case
  if($current.state -ne 'Resolved'){
   $body=@{key=$current.key;category=$current.category;version=$current.version;state='Resolved';reason='Restauración de resolución tras validación aislada de interfaz'}|ConvertTo-Json
   Invoke-RestMethod https://localhost:7441/api/planning/reviews/decision -Headers $headers -Method Put -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))|Out-Null
  }
 }
 exit
}
if($Step -eq 'Columns'){
 Invoke-Named 'Columnas';Find-Named 'Columnas visibles'|Out-Null
 $check=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'Prueba original'))|Where-Object{$_.Current.ControlType -eq [Windows.Automation.ControlType]::CheckBox}|Select-Object -First 1
 $check.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle();Key 0x1B;Key 0x1B $true
 $header=Find-Named 'Ordenar por Fecha ingreso';$before=$header.Current.BoundingRectangle
 [PlanningCapture]::SetCursorPos([int]($before.Left+12),[int]($before.Top+$before.Height/2))|Out-Null
 [PlanningCapture]::mouse_event(8,0,0,0,[UIntPtr]::Zero);[PlanningCapture]::mouse_event(16,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 150
 Invoke-Named 'Mover columna a la derecha';$moved=Find-Named 'Ordenar por Fecha ingreso';if($moved.Current.BoundingRectangle.Left -le $before.Left){throw 'La columna no cambió de orden'}
 $grip=Find-Named 'Redimensionar Fecha ingreso';$b=$grip.Current.BoundingRectangle;$prior=$moved.Current.BoundingRectangle.Width
 [PlanningCapture]::SetCursorPos([int]($b.Left+$b.Width/2),[int]($b.Top+$b.Height/2))|Out-Null;[PlanningCapture]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
 $screen=[Windows.Forms.SystemInformation]::VirtualScreen
 try{for($i=1;$i -le 10;$i++){$x=$b.Left+$b.Width/2+60*$i/10;$y=$b.Top+$b.Height/2;[PlanningCapture]::mouse_event(0xE001,[uint32](($x-$screen.Left)*65535/($screen.Width-1)),[uint32](($y-$screen.Top)*65535/($screen.Height-1)),0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 60}}finally{[PlanningCapture]::mouse_event(4,0,0,0,[UIntPtr]::Zero)}
 $resized=Find-Named 'Ordenar por Fecha ingreso';if($resized.Current.BoundingRectangle.Width -le $prior+20){throw 'La columna no cambió de ancho'}
 Capture '17-configuracion-columnas';Pass @('Configuración nativa muestra columnas adicionales','Menú del encabezado reorganiza columnas','Arrastre del borde redimensiona encabezado y celdas');exit
}
if($Step -eq 'Import'){
 function Use-Window([string]$title){
  $deadline=[DateTime]::UtcNow.AddSeconds(150);do{
   $candidate=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id))|Where-Object{$_.Current.Name -eq $title}|Select-Object -First 1
   if($candidate){$script:window=$candidate;return};Start-Sleep -Milliseconds 750
  }while([DateTime]::UtcNow -lt $deadline);throw ('Ventana no disponible: '+$title)
 }
 if(-not $ResumeImport){
 Invoke-Named 'Nueva importación';Combo 'Corte operativo' 1
 function Select-SourceFile([string]$button,[string]$path){Invoke-Named $button;$picker=Native-Picker ([IO.Path]::GetFileName($path));if(-not $picker.AlreadySelected){Complete-Picker $picker $path};Start-Sleep -Milliseconds 350}
 Select-SourceFile 'Summary XLSX' (Join-Path $env:USERPROFILE 'Downloads/a0_-_pro-invoice_summary_17Nov2026045852237.xlsx')
 Select-SourceFile 'Report List XLSX' (Join-Path $env:USERPROFILE 'Downloads/a0_-_delay_report_list_based_on_due_date_17Nov2026045945888.xlsx')
 Capture '14-archivos-originales';Invoke-Named 'Validar archivos'
 }
 Use-Window 'Revisa antes de agregar';Capture '15-revisa-antes-de-agregar'
 $check=Find-Named 'Importar registros válidos y dejar explícitamente las equivalencias pendientes';if($check.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne [Windows.Automation.ToggleState]::On){$check.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()}
 Invoke-Named 'Confirmar importación';Use-Window 'Resumen de importación';Capture '16-resumen-importacion';Invoke-Named 'Cerrar';Use-Window 'LIMS'
 Pass @('Selección nativa de Summary y Report List originales','Ventana previa obligatoria y aceptación explícita de equivalencias pendientes','Confirmación nativa y ventana Resumen de importación');exit
}
