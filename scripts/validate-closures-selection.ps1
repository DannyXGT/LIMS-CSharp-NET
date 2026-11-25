$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$evidence=Join-Path $workspace 'artifacts/validation/closures-desktop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Windows.Forms
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class ClosureSelectionInput {
 [DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]public static extern bool SetPhysicalCursorPos(int x,int y);
 [DllImport("user32.dll")]public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e);
 [DllImport("user32.dll")]public static extern void keybd_event(byte k,byte s,uint f,UIntPtr e);
 [DllImport("user32.dll")]public static extern uint MapVirtualKey(uint k,uint t);
 [DllImport("user32.dll")]public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
}
'@
$handle=[IntPtr](Get-Content (Join-Path $evidence 'window.json') -Raw|ConvertFrom-Json).hwnd
$window=[Windows.Automation.AutomationElement]::FromHandle($handle)
$app=Get-Process -Id $window.Current.ProcessId
if(-not $app.Path.StartsWith($evidence+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'La prueba solo permite el cliente aislado de Cierres.'}
$dpiContext=[ClosureSelectionInput]::SetThreadDpiAwarenessContext([IntPtr](-4))
function Assert-Focus {if([ClosureSelectionInput]::GetForegroundWindow() -ne $handle){throw 'El cliente aislado no tiene el foco; no se enviara entrada.'}}
function Key([byte]$code,[bool]$up=$false){Assert-Focus;$flags=$(if($up){2}else{0});if($code -ge 0x21 -and $code -le 0x28){$flags=$flags -bor 1};[ClosureSelectionInput]::keybd_event($code,[byte][ClosureSelectionInput]::MapVirtualKey($code,0),$flags,[UIntPtr]::Zero);Start-Sleep -Milliseconds 40}
function Copy-Range {Key 0x11;try{Key 0x43;Key 0x43 $true}finally{Key 0x11 $true};Start-Sleep -Milliseconds 150;return [Windows.Forms.Clipboard]::GetText()}
function Cell([string]$prefix){
 $result=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|Where-Object{-not $_.Current.IsOffscreen -and $_.Current.Name.StartsWith($prefix,[StringComparison]::Ordinal)}|Select-Object -First 1
 if($null -eq $result){throw ('No se encontro la celda: '+$prefix)};return $result.Current.BoundingRectangle
}
function Drag($first,$last){
 Assert-Focus;[ClosureSelectionInput]::SetPhysicalCursorPos([int]($first.Left+4),[int]($first.Top+$first.Height/2))|Out-Null
 [ClosureSelectionInput]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
 $screen=[Windows.Forms.SystemInformation]::VirtualScreen
 try{for($i=1;$i -le 12;$i++){
  Assert-Focus;$x=$first.Left+4+($last.Left-$first.Left)*$i/12;$y=$first.Top+$first.Height/2+($last.Top+$last.Height/2-$first.Top-$first.Height/2)*$i/12
  [ClosureSelectionInput]::mouse_event(0xE001,[uint32](($x-$screen.Left)*65535/($screen.Width-1)),[uint32](($y-$screen.Top)*65535/($screen.Height-1)),0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 65
 }}finally{[ClosureSelectionInput]::mouse_event(4,0,0,0,[UIntPtr]::Zero)}
 Start-Sleep -Milliseconds 180
}
function Assert-Matrix([string]$text,[int]$rows,[int]$columns){$lines=@($text -split "`r?`n");if($lines.Count -ne $rows -or @($lines|Where-Object{@($_ -split "`t").Count -ne $columns}).Count -gt 0){throw "La copia no contiene una matriz de $rows x $columns."}}
function Assert-HtmlTable([int]$rows,[int]$columns){
 if(-not [Windows.Forms.Clipboard]::ContainsData([Windows.Forms.DataFormats]::Html)){throw 'El portapapeles no contiene una tabla con formato.'}
 $html=[Windows.Forms.Clipboard]::GetText([Windows.Forms.TextDataFormat]::Html)
 $start=[regex]::Match($html,'StartFragment:(\d+)');$end=[regex]::Match($html,'EndFragment:(\d+)')
 if(-not $start.Success -or -not $end.Success){throw 'Faltan los offsets nativos del fragmento HTML.'}
 $bytes=[Text.Encoding]::UTF8.GetBytes($html);$offset=[int]$start.Groups[1].Value;$length=[int]$end.Groups[1].Value-$offset
 if($offset -lt 0 -or $length -le 0 -or $offset+$length -gt $bytes.Length){throw 'El fragmento HTML no tiene offsets UTF-8 validos.'}
 [xml]$table=[Text.Encoding]::UTF8.GetString($bytes,$offset,$length)
 if($table.DocumentElement.Name -ne 'table' -or $table.SelectNodes('//tr').Count -ne $rows -or $table.SelectNodes('//td').Count -ne $rows*$columns){throw 'La tabla del portapapeles no conserva el rango seleccionado.'}
}
try{
 $window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState([Windows.Automation.WindowVisualState]::Normal)
 $focusTarget=$window.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'Bandeja de cierres'))
 $focusTarget.SetFocus()
 [ClosureSelectionInput]::SetForegroundWindow($handle)|Out-Null;Start-Sleep -Milliseconds 200
 Key 0x1B;Key 0x1B $true;Start-Sleep -Milliseconds 150
 $first=Cell 'Fila 1, JOB:';$last=Cell 'Fila 3, PRUEBA:'
 Drag $first $last;$rectangle=Copy-Range;Assert-Matrix $rectangle 3 3
 Assert-HtmlTable 3 3
 if(-not $rectangle.StartsWith('GUA20260001') -or -not $rectangle.Contains('DEMO20260003')){throw 'La copia perdio el orden de las filas entre grupos.'}
 Key 0xA0;try{Key 0x27;Key 0x27 $true;Key 0x28;Key 0x28 $true}finally{Key 0xA0 $true};Assert-Matrix (Copy-Range) 4 4
 Drag $first $last;Key 0xA1;try{Key 0x27;Key 0x27 $true;Key 0x28;Key 0x28 $true}finally{Key 0xA1 $true};Assert-Matrix (Copy-Range) 4 4
 Drag $last $first;if((Copy-Range) -ne $rectangle){throw 'El arrastre inverso cambio el rango copiado.'}
 Key 0x11;try{Key 0x41;Key 0x41 $true}finally{Key 0x11 $true};$all=Copy-Range;Assert-Matrix $all 13 5
 Key 0x11;try{Key 0x56;Key 0x56 $true}finally{Key 0x11 $true};Key 0x71;Key 0x71 $true;Key 0x2E;Key 0x2E $true
 if((Copy-Range) -ne $all){throw 'Una tecla de edicion cambio la tabla de solo lectura.'}
 [ClosureSelectionInput]::SetPhysicalCursorPos([int]($first.Left+4),[int]($first.Top+$first.Height/2))|Out-Null
 for($tap=0;$tap -lt 2;$tap++){Assert-Focus;[ClosureSelectionInput]::mouse_event(2,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 30;[ClosureSelectionInput]::mouse_event(4,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 50}
 Start-Sleep -Milliseconds 250
 $detail=$window.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'Pruebas actuales del JOB en cierre'))
 if($null -eq $detail -or $detail.Current.IsOffscreen){throw 'El doble clic no abrio el detalle del JOB.'}
 $close=$window.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'Cerrar detalle'));$close.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke();Start-Sleep -Milliseconds 150
 $first=Cell 'Fila 1, JOB:';$last=Cell 'Fila 3, PRUEBA:'
 Drag $first $last
 $report=[ordered]@{Passed=$true;Boundary='Native Windows pointer, keyboard and clipboard against isolated WinUI fixture only.';MouseDrag3x3=$true;ReverseDrag=$true;LeftAndRightShift4x4=$true;CtrlCWindowsClipboard=$true;HtmlTableOnWindowsClipboard=$true;Utf8HtmlOffsets=$true;CtrlA13x5=$true;GroupBandsExcluded=$true;PasteF2DeleteDoNotEdit=$true;DoubleClickOpensDetail=$true}
 $report|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $evidence 'selection-native.json') -Encoding UTF8
 $report
}finally{[ClosureSelectionInput]::SetThreadDpiAwarenessContext($dpiContext)|Out-Null}
