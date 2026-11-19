param([ValidateSet('Import','Results','History','Tray','Inspect')][string]$Action='Results')
$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'validate-planning-desktop.ps1'))
$realScriptsRoot=$PSScriptRoot;$start=$source.IndexOf('$ErrorActionPreference=');$end=$source.IndexOf("if(`$Action -eq 'Inspect')")
Invoke-Expression $source.Substring($start,$end-$start).Replace('$PSScriptRoot','$realScriptsRoot')
$process=Get-Process -Id $window.Current.ProcessId
if(-not $process.Path.StartsWith((Join-Path $workspace 'artifacts/planificacion-dev/desktop-validation-'),[StringComparison]::OrdinalIgnoreCase)){throw 'Cliente no autorizado'}
$evidence=Join-Path $local 'importacion-real/ui';New-Item -ItemType Directory -Path $evidence -Force|Out-Null
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class RealImportGeometry {
 [DllImport("user32.dll")]public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
 [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int w,int v,uint f);
}
'@
[RealImportGeometry]::SetThreadDpiAwarenessContext([IntPtr](-4))|Out-Null
$window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState([Windows.Automation.WindowVisualState]::Normal)
[RealImportGeometry]::SetWindowPos([IntPtr]$window.Current.NativeWindowHandle,[IntPtr]::Zero,0,0,1920,1080,0x40)|Out-Null
function Capture([string]$name){
    # An inactive Windows desktop can retain an old compositor surface. Resize the isolated window to request a new real frame.
    $handle=[IntPtr]$window.Current.NativeWindowHandle;$rect=[PlanningCapture+Rect]::new();[PlanningCapture]::GetWindowRect($handle,[ref]$rect)|Out-Null
    [PlanningCapture]::SetForegroundWindow($handle)|Out-Null
    $width=$rect.Right-$rect.Left;$height=$rect.Bottom-$rect.Top
    [RealImportGeometry]::SetWindowPos($handle,[IntPtr]::Zero,$rect.Left,$rect.Top,$width-1,$height,0x14)|Out-Null;Start-Sleep -Milliseconds 400
    [RealImportGeometry]::SetWindowPos($handle,[IntPtr]::Zero,$rect.Left,$rect.Top,$width,$height,0x14)|Out-Null;Start-Sleep -Milliseconds 900
    Capture-Window $window $name
}
function Combo([string]$name,[int]$index){
    $combo=Find-Named $name;$expand=$combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern);$expand.Expand();Start-Sleep -Milliseconds 150
    $items=@($combo.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem)))
    if($items.Count -eq 0){$items=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ClassNameProperty,'ComboBoxItem'))|Where-Object{-not $_.Current.IsOffscreen})}
    if($items.Count -le $index){throw ('Opciones no disponibles: '+$name)}
    $items[$index].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();$expand.Collapse();Start-Sleep -Milliseconds 150
}
function Wait-Like([string]$pattern,[int]$seconds=60){
    $deadline=[DateTime]::UtcNow.AddSeconds($seconds)
    do{foreach($element in $window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)){if(-not $element.Current.IsOffscreen -and $element.Current.Name -like $pattern){return $element}};Start-Sleep -Milliseconds 200}while([DateTime]::UtcNow -lt $deadline)
    Inspect-Window;throw ('No apareció: '+$pattern)
}
if($Action -eq 'Inspect'){Inspect-Window;exit}
if($Action -eq 'Import'){
    Invoke-Named 'Importaciones';Find-Named 'Validar y conciliar'|Out-Null
    $functionStart=$source.IndexOf('    function Select-SourceFile(');$functionEnd=$source.IndexOf("    Select-SourceFile 'Summary XLSX'",$functionStart)
    Invoke-Expression $source.Substring($functionStart,$functionEnd-$functionStart)
    Select-SourceFile 'Summary XLSX' (Join-Path $env:USERPROFILE 'Downloads/a0_-_pro-invoice_summary_17Nov2026045852237.xlsx')
    Select-SourceFile 'Report List XLSX' (Join-Path $env:USERPROFILE 'Downloads/a0_-_delay_report_list_based_on_due_date_17Nov2026045945888.xlsx')
    Combo 'Corte' 1;Set-NamedValue 'Día operativo · yyyy-MM-dd' '2026-11-17';Capture '01-archivos-reales'
    Invoke-Named 'Validar y conciliar';$deadline=[DateTime]::UtcNow.AddSeconds(90)
    do{$confirm=Find-Named 'Confirmar lote revisado';if($confirm.Current.IsEnabled){break};Start-Sleep -Milliseconds 250}while([DateTime]::UtcNow -lt $deadline)
    if(-not $confirm.Current.IsEnabled){Inspect-Window;throw 'No se habilitó la confirmación del lote nuevo'}
    Capture '02-preview-corte-mediodia';Invoke-Named 'Archivos y validación';Capture '03-validacion-cobertura'
    Invoke-Named 'Confirmar lote revisado';Invoke-Named 'Confirmar';Wait-Like 'Lote confirmado*' 90|Out-Null;Capture '04-confirmacion-nativa'
    @{Passed=$true;Boundary='Real WinUI file pickers, targeted UI Automation, preview and native confirmation against isolated PostgreSQL';Cut='Noon';Day='2026-11-17';Expected='Same actual files across another cut do not create duplicate records'}|ConvertTo-Json|Set-Content (Join-Path $evidence 'acceptance-import.json') -Encoding UTF8
    Write-Output 'PASS: selección de los XLSX reales, validación, conciliación y confirmación desde WinUI';exit
}
if($Action -eq 'Tray'){
    Invoke-Named 'Importaciones';Invoke-Named 'Actualizar bandejas e historial';Combo 'Bandeja' 1;Invoke-Named 'Bandejas de revisión';Wait-Like '*incidencias'|Out-Null;Capture '11-bandeja-solo-summary';exit
}
if($Action -eq 'History'){
    Invoke-Named 'Importaciones';Invoke-Named 'Historial';Start-Sleep -Milliseconds 500
    $items=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem))
    $item=$items|Where-Object{$_.Current.Name -like '*b0f34516-d0e2-4e1a-ba0f-ee43f8cab370*'}|Select-Object -First 1
    if(-not $item){Inspect-Window;throw 'Seleccione el lote First del 17 en el historial'}
    $item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();Capture '05-historial-lotes';Invoke-Named 'Consultar lote seleccionado';Invoke-Named 'Vista previa de cambios';Start-Sleep -Milliseconds 500
    Set-NamedValue 'Filtrar por JOB o campo' 'SampleStatus';Capture '06-cambios-estado-reales';exit
}
Invoke-Named 'Consulta de Jobs';Invoke-Named 'Filtros';Combo 'Alcance de datos' 1;Invoke-Named 'Aplicar filtros';Start-Sleep -Milliseconds 500
Set-NamedValue 'Buscar JOB' 'GUAT25038091-REV3';Invoke-Named 'Buscar';Wait-Like '24 ejecuciones*'|Out-Null;Capture '07-consulta-estado-24-pruebas'
Invoke-Named 'Estado del JOB';$main=$window;$deadline=[DateTime]::UtcNow.AddSeconds(20)
do{$dossier=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id))|Where-Object{$_.Current.Name -eq 'Estado del JOB · GUAT25038091-REV3'}|Select-Object -First 1;if(-not $dossier){Start-Sleep -Milliseconds 150}}while(-not $dossier -and [DateTime]::UtcNow -lt $deadline)
if(-not $dossier){throw 'No abrió el expediente'}
$window=$dossier;Wait-Like 'Estado TIPS*'|Out-Null;Capture '08-expediente-estado-fuente';Invoke-Named 'WIP';Capture '09-expediente-wip';$window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close();$window=$main
Set-NamedValue 'Buscar JOB' 'GUAT26016315';Invoke-Named 'Buscar';Wait-Like '*Invoice Rejected*'|Out-Null;Capture '10-estado-actualizado-real'
Invoke-Named 'Importaciones';Combo 'Bandeja' 1;Invoke-Named 'Bandejas de revisión';Wait-Like '*incidencias'|Out-Null;Capture '11-bandeja-solo-summary'
@{Passed=$true;Boundary='Actual native table, real 24-test JOB and official Summary state, WIP and persisted Summary tray; isolated local database'}|ConvertTo-Json|Set-Content (Join-Path $evidence 'acceptance-results.json') -Encoding UTF8
Write-Output 'PASS: Consulta, Estado, expediente WIP y bandeja Solo Summary en el cliente nativo'
