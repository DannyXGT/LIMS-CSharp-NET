$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$layout=Join-Path $workspace 'Frontend/Lims.Desktop/bin/Debug/net10.0-windows10.0.26100.0/win-x64'
$expectedExe=Join-Path $layout 'AppX/Lims.Desktop.exe'
$process=Get-Process -Name Lims.Desktop -ErrorAction SilentlyContinue|Where-Object{$_.Path -eq $expectedExe}|Sort-Object StartTime -Descending|Select-Object -First 1
if(-not $process){throw 'The official client from the expected project-mode AppX directory is not running.'}
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing
$condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id)
$window=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,$condition)|Where-Object{$_.Current.Name -eq 'LIMS'}|Select-Object -First 1
if(-not $window){throw 'LIMS window not available.'}
function Find([string]$name,[int]$seconds=10){
    $deadline=[DateTime]::UtcNow.AddSeconds($seconds)
    do{
        $elements=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$name))
        foreach($element in $elements){if(-not $element.Current.IsOffscreen -and $element.Current.IsEnabled){return $element}}
        Start-Sleep -Milliseconds 150
    }while([DateTime]::UtcNow -lt $deadline)
    return $null
}
function Open-ReadView([string]$name){
    if($name -notin @('Planificación','Consulta de Jobs','Report List / Digitación')){throw 'Only approved read views.'}
    $element=Find $name 20;if(-not $element){throw ('Read view not available: '+$name)}
    $pattern=$null
    if($element.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern,[ref]$pattern)){$pattern.Invoke();return}
    if($element.TryGetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern,[ref]$pattern)){$pattern.Select();return}
    throw ('Read view cannot be selected: '+$name)
}
if(-not(Find 'Consulta de Jobs' 1)){Open-ReadView 'Planificación'}
Open-ReadView 'Consulta de Jobs'
Open-ReadView 'Report List / Digitación'
$notice='Report List todavía no está disponible en el servidor configurado. Se requiere actualizar el servidor para usar este módulo.'
$available=Find $notice 15
$out=Join-Path $workspace 'artifacts/report-list-server';New-Item -ItemType Directory -Path $out -Force|Out-Null
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class CommandClientCapture {
 [StructLayout(LayoutKind.Sequential)] public struct Rect {public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
}
'@
$rect=[CommandClientCapture+Rect]::new();$handle=[IntPtr]$window.Current.NativeWindowHandle
[CommandClientCapture]::GetWindowRect($handle,[ref]$rect)|Out-Null
$bitmap=[Drawing.Bitmap]::new($rect.Right-$rect.Left,$rect.Bottom-$rect.Top);$graphics=[Drawing.Graphics]::FromImage($bitmap)
try{$dc=$graphics.GetHdc();try{if(-not[CommandClientCapture]::PrintWindow($handle,$dc,2)){throw 'Cannot capture client.'}}finally{$graphics.ReleaseHdc($dc)};$bitmap.Save((Join-Path $out 'production-client-report-list.png'),[Drawing.Imaging.ImageFormat]::Png)}finally{$graphics.Dispose();$bitmap.Dispose()}
$compiledHash=(Get-FileHash (Join-Path $layout 'Lims.Desktop.dll')).Hash
$packagedHash=(Get-FileHash (Join-Path $layout 'AppX/Lims.Desktop.dll')).Hash
if($compiledHash -ne $packagedHash){throw 'Packaged assembly differs from the current project-mode build.'}
@{Boundary='Official project-mode client with existing Production session; read-only navigation';ProcessId=$process.Id;Executable=$expectedExe;ReportListMenuVisible=$true;ServerUpdateNoticeVisible=[bool]$available;CompiledAndPackagedHash=$compiledHash;Utc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json|Set-Content (Join-Path $out 'production-client.json') -Encoding UTF8
if(-not $available){throw 'Expected server update notice was not visible; inspect the captured image.'}
Write-Output 'PASS: the command now opens the current client, Report List is visible, and the missing server module is reported clearly.'
