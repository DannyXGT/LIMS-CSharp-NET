param([ValidateSet('LoginAndNavigate','ReviewDetails','ImportFiles','Inspect','Capture')][string]$Action='LoginAndNavigate',[string]$Name='current',[string]$SummaryPath,[string]$ReportPath)
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$local=Join-Path $workspace 'artifacts\planificacion-dev'
$evidence=Join-Path $local 'screenshots'
New-Item -ItemType Directory -Path $evidence -Force|Out-Null
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
public static class PlanningCapture {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h,uint command);
 [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h,uint message,IntPtr w,IntPtr l);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
 [StructLayout(LayoutKind.Sequential)] public struct KeyboardInput { public ushort Key,Scan;public uint Flags,Time;public UIntPtr Extra; }
 [StructLayout(LayoutKind.Sequential)] public struct MouseInput { public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra; }
 [StructLayout(LayoutKind.Explicit)] public struct InputUnion { [FieldOffset(0)] public KeyboardInput Keyboard;[FieldOffset(0)] public MouseInput Mouse; }
 [StructLayout(LayoutKind.Sequential)] public struct Input { public uint Type;public InputUnion Data; }
 [DllImport("user32.dll",SetLastError=true)] public static extern uint SendInput(uint count,Input[] inputs,int size);
 public static void ReplaceText(string text) {
   var keys=new List<Input>();
   Action<ushort,uint> key=(code,flags)=>keys.Add(new Input{Type=1,Data=new InputUnion{Keyboard=new KeyboardInput{Key=code,Flags=flags}}});
   key(0x11,0);key(0x41,0);key(0x41,2);key(0x11,2);key(0x08,0);key(0x08,2);
   foreach(char ch in text){keys.Add(new Input{Type=1,Data=new InputUnion{Keyboard=new KeyboardInput{Scan=ch,Flags=4}}});keys.Add(new Input{Type=1,Data=new InputUnion{Keyboard=new KeyboardInput{Scan=ch,Flags=6}}});}
   var values=keys.ToArray();if(SendInput((uint)values.Length,values,Marshal.SizeOf(typeof(Input)))!=values.Length)throw new InvalidOperationException("No se pudo introducir el texto en el cliente aislado.");
 }
}
'@
function Find-Window {
    $deadline=[DateTime]::UtcNow.AddSeconds(45)
    do{
        foreach($process in Get-Process -Name 'Lims.Planning.Validation' -ErrorAction SilentlyContinue){
            $condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,[int]$process.Id)
            $elements=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,$condition)
            $element=$elements|Where-Object{$_.Current.Name -eq 'LIMS' -and $_.Current.ClassName -eq 'WinUIDesktopWin32WindowClass'}|Select-Object -First 1
            if($element){return $element}
        }
        Start-Sleep -Milliseconds 150
    }while([DateTime]::UtcNow -lt $deadline)
    throw 'No se abrió el cliente con identidad de validación. Revise desktop.log.'
}
$window=Find-Window
function Find-Named([string]$controlName,[int]$seconds=30){
    $deadline=[DateTime]::UtcNow.AddSeconds($seconds)
    $condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$controlName)
    do{
        $elements=$window.FindAll([Windows.Automation.TreeScope]::Descendants,$condition)
        foreach($element in $elements){if(-not $element.Current.IsOffscreen){return $element}}
        Start-Sleep -Milliseconds 150
    }while([DateTime]::UtcNow -lt $deadline)
    throw "Control no disponible: $controlName"
}
function Invoke-Named([string]$controlName){
    $deadline=[DateTime]::UtcNow.AddSeconds(20)
    do{
        $condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$controlName)
        $elements=$window.FindAll([Windows.Automation.TreeScope]::Descendants,$condition)
        foreach($element in $elements){
            if($element.Current.IsOffscreen -or -not $element.Current.IsEnabled){continue}
            $pattern=$null
            if($element.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern,[ref]$pattern)){$pattern.Invoke();return}
            if($element.TryGetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern,[ref]$pattern)){$pattern.Select();return}
        }
        Start-Sleep -Milliseconds 150
    }while([DateTime]::UtcNow -lt $deadline)
    throw "Control no invocable: $controlName"
}
function Set-NamedValue([string]$controlName,[string]$value){
    $condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$controlName)
    foreach($element in $window.FindAll([Windows.Automation.TreeScope]::Descendants,$condition)){
        $pattern=$null
        if($element.TryGetCurrentPattern([Windows.Automation.ValuePattern]::Pattern,[ref]$pattern)){$pattern.SetValue('');Start-Sleep -Milliseconds 120;$pattern.SetValue($value);Start-Sleep -Milliseconds 150;return}
    }
    throw "Campo editable no disponible: $controlName"
}
function Capture-Window($element,[string]$captureName){
    $handle=[IntPtr]$element.Current.NativeWindowHandle
    $rect=[PlanningCapture+Rect]::new()
    [PlanningCapture]::GetWindowRect($handle,[ref]$rect)|Out-Null
    $bitmap=[Drawing.Bitmap]::new($rect.Right-$rect.Left,$rect.Bottom-$rect.Top)
    $graphics=[Drawing.Graphics]::FromImage($bitmap)
    try{
        $dc=$graphics.GetHdc()
        try{if(-not [PlanningCapture]::PrintWindow($handle,$dc,2)){throw 'No se pudo capturar la ventana.'}}finally{$graphics.ReleaseHdc($dc)}
        $bitmap.Save((Join-Path $evidence ($captureName+'.png')),[Drawing.Imaging.ImageFormat]::Png)
    }finally{$graphics.Dispose();$bitmap.Dispose()}
}
function Inspect-Window {
    $window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|ForEach-Object{
        if(-not $_.Current.IsPassword){[pscustomobject]@{Name=$_.Current.Name;Type=$_.Current.ControlType.ProgrammaticName;Offscreen=$_.Current.IsOffscreen;Bounds=$_.Current.BoundingRectangle.ToString()}}
    }|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $local 'desktop-uia.json') -Encoding UTF8
}
if($Action -eq 'Inspect'){Inspect-Window;Write-Output 'UI Automation guardada en desktop-uia.json';exit 0}
if($Action -eq 'Capture'){Capture-Window $window $Name;exit 0}
if($Action -eq 'ImportFiles'){
    foreach($path in @($SummaryPath,$ReportPath)){if(-not $path -or -not (Test-Path -LiteralPath $path -PathType Leaf)){throw 'Indique dos archivos XLSX locales existentes.'}}
    $validation=Get-Content -LiteralPath (Join-Path $local 'desktop-validation.json') -Raw|ConvertFrom-Json
    if($validation.ApiUrl -ne 'https://localhost:7441'){throw 'Esta validacion solo admite la API local aislada.'}
    function Select-SourceFile([string]$button,[string]$path){
        $existing=$window.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ClassNameProperty,'#32770'))
        if(-not $existing){Invoke-Named $button}
        $deadline=[DateTime]::UtcNow.AddSeconds(20)
        $dialog=$null
        do{
            $dialogs=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ClassNameProperty,'#32770'))
            foreach($candidate in $dialogs){
                $edit=$candidate.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,'1148'))
                if($edit){$dialog=$candidate;break}
            }
            if(-not $dialog){Start-Sleep -Milliseconds 150}
        }while(-not $dialog -and [DateTime]::UtcNow -lt $deadline)
        if(-not $dialog){throw 'El selector de archivos no estuvo disponible.'}
        $input=$edit.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Edit))
        if(-not $input){$input=$edit}
        $input.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue([IO.Path]::GetFullPath($path))
        $buttons=$dialog.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ClassNameProperty,'Button'))
        $open=$buttons|Where-Object{$_.Current.AutomationId -eq '1' -and $_.Current.NativeWindowHandle -ne 0}|Select-Object -First 1
        if(-not $open){throw 'No se encontro el boton nativo Abrir del selector.'}
        [PlanningCapture]::SendMessage([IntPtr]$open.Current.NativeWindowHandle,0xF5,[IntPtr]::Zero,[IntPtr]::Zero)|Out-Null
        Start-Sleep -Milliseconds 500
    }
    Select-SourceFile 'Summary XLSX' $SummaryPath
    Select-SourceFile 'Report List XLSX' $ReportPath
    Invoke-Named 'Validar y conciliar'
    $deadline=[DateTime]::UtcNow.AddSeconds(55)
    do{$confirm=Find-Named 'Confirmar lote revisado';if($confirm.Current.IsEnabled){break};Start-Sleep -Milliseconds 250}while([DateTime]::UtcNow -lt $deadline)
    if(-not $confirm.Current.IsEnabled){Inspect-Window;throw 'La previsualizacion no habilito la confirmacion. Revise desktop-uia.json.'}
    Capture-Window $window '07-importacion-revision'
    Invoke-Named 'Cambios y errores'
    Start-Sleep -Milliseconds 300
    Capture-Window $window '07b-importacion-cobertura'
    Invoke-Named 'Confirmar lote revisado'
    Invoke-Named 'Confirmar'
    $deadline=[DateTime]::UtcNow.AddSeconds(55)
    do{
        $done=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|Where-Object{$_.Current.Name -like 'Lote confirmado*'}|Select-Object -First 1
        if(-not $done){Start-Sleep -Milliseconds 250}
    }while(-not $done -and [DateTime]::UtcNow -lt $deadline)
    if(-not $done){Inspect-Window;throw 'No se recibio el resultado de confirmacion.'}
    Capture-Window $window '08-importacion-confirmada'
    Inspect-Window
    @{Passed=$true;Boundary='Actual WinUI file pickers, preview and explicit confirmation against isolated local PostgreSQL';SummarySha256=(Get-FileHash -LiteralPath $SummaryPath).Hash;ReportSha256=(Get-FileHash -LiteralPath $ReportPath).Hash;Result=$done.Current.Name}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $local 'acceptance-desktop-import.json') -Encoding UTF8
    Write-Output $done.Current.Name
    exit 0
}
if($Action -eq 'ReviewDetails'){
    $source=Get-Content -LiteralPath (Join-Path $local 'acceptance-api.json') -Raw|ConvertFrom-Json
    $condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem)
    $selected=$false
    foreach($element in $window.FindAll([Windows.Automation.TreeScope]::Descendants,$condition)){
        if($element.Current.IsOffscreen -or $element.Current.Name -notlike ('*'+$source.sourceJob+'*')){continue}
        $pattern=$null
        if($element.TryGetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern,[ref]$pattern)){$pattern.Select();$selected=$true;break}
    }
    if(-not $selected){throw 'No se encontró la fila real consultada.'}
    Invoke-Named 'Abrir expediente seleccionado'
    $deadline=[DateTime]::UtcNow.AddSeconds(20)
    $dossier=$null
    do{
        $windows=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$window.Current.ProcessId))
        $dossier=$windows|Where-Object{$_.Current.Name -like ('*'+$source.sourceJob+'*')}|Select-Object -First 1
        if(-not $dossier){Start-Sleep -Milliseconds 150}
    }while(-not $dossier -and [DateTime]::UtcNow -lt $deadline)
    if(-not $dossier){throw 'El expediente no abrió como ventana independiente.'}
    Start-Sleep -Milliseconds 600
    Capture-Window $dossier '05-expediente-real'
    $dossier.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()
    Invoke-Named 'Importaciones'
    Find-Named 'Validar y conciliar'|Out-Null
    Start-Sleep -Milliseconds 600
    Capture-Window $window '06-importaciones-bandejas'
    Inspect-Window
    Write-Output 'Expediente independiente e importaciones verificados en el cliente real local.'
    exit 0
}
$account=Get-Content -LiteralPath (Join-Path $local 'acceptance-account.json') -Raw|ConvertFrom-Json
$restored=$false
try{Find-Named 'Elegir espacio de trabajo' 3|Out-Null;$restored=$true}catch{ }
if(-not $restored){
    $back=$window.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'← Espacios'))
    if($back){Invoke-Named '← Espacios';Find-Named 'Elegir espacio de trabajo'|Out-Null;$restored=$true}
}
if(-not $restored){
    Find-Named 'Usuario'|Out-Null
    Add-Type -AssemblyName System.Windows.Forms
    function Type-LoginField([string]$id,[string]$value){
        $control=$window.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$id))
        if(-not $control){throw 'Campo original de login no encontrado.'}
        [PlanningCapture]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle)|Out-Null
        $bounds=$control.Current.BoundingRectangle
        [PlanningCapture]::SetCursorPos([int]($bounds.Left+$bounds.Width/2),[int]($bounds.Top+$bounds.Height/2))|Out-Null
        [PlanningCapture]::mouse_event(2,0,0,0,[UIntPtr]::Zero);[PlanningCapture]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
        $control.SetFocus();Start-Sleep -Milliseconds 150
        if([PlanningCapture]::GetForegroundWindow() -ne [IntPtr]$window.Current.NativeWindowHandle){throw 'El cliente aislado debe tener el foco antes de introducir credenciales.'}
        [PlanningCapture]::ReplaceText($value)
        Start-Sleep -Milliseconds 500
        if($id -eq 'IdentifierBox'){
            $actual=$control.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
            if($actual -ne $value){throw 'El identificador no se introdujo completo; se cancela el intento.'}
        }
    }
    Type-LoginField 'IdentifierBox' ($account.email.Split('@')[0])
    Type-LoginField 'PasswordInput' $account.password
    Capture-Window $window '00-login-original'
    Start-Sleep -Milliseconds 500
    Invoke-Named 'Iniciar sesión'
}
Find-Named 'Elegir espacio de trabajo'|Out-Null
Find-Named 'Planificación'|Out-Null
Start-Sleep -Milliseconds 300
Capture-Window $window '01-selector'
Invoke-Named 'Laboratorio'
Find-Named 'Cambiar espacio'|Out-Null
Invoke-Named 'Estándares'
Start-Sleep -Milliseconds 800
Capture-Window $window '02-estandares-preservados'
Invoke-Named 'Preparaciones'
Start-Sleep -Milliseconds 600
Capture-Window $window '03-preparaciones-preservadas'
Invoke-Named 'Cambiar espacio'
Find-Named 'Planificación'|Out-Null
Invoke-Named 'Planificación'
Find-Named 'Consulta de Jobs'|Out-Null
Start-Sleep -Milliseconds 1400
Capture-Window $window '04-consulta-real'
Inspect-Window
@{Passed=$true;Boundary='Actual WinUI executable; isolated package identity, existing login and local restored PostgreSQL; historical TIPS source';ProcessId=$window.Current.ProcessId;Screenshots=@('00-login-original','01-selector','02-estandares-preservados','03-preparaciones-preservadas','04-consulta-real');ProductionCredentialsReused=$false}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $local 'acceptance-desktop.json') -Encoding UTF8
Write-Output 'Login, selector, laboratorio y Planificación verificados en el cliente real aislado.'
