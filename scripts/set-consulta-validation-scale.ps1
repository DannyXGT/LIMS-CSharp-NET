param([ValidateSet(100,125)][int]$Scale)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class ScaleValidationWindow {
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
}
'@
Start-Process 'ms-settings:display' -WindowStyle Hidden
$deadline=[DateTime]::UtcNow.AddSeconds(15);$settings=$null
do{
    $settings=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.Condition]::TrueCondition)|Where-Object{$_.Current.Name -in @('Configuración','Settings')}|Select-Object -First 1
    if(-not $settings){Start-Sleep -Milliseconds 200}
}while(-not $settings -and [DateTime]::UtcNow -lt $deadline)
if(-not $settings){throw 'No se abrió Configuración de pantalla.'}
try{
    $combo=$null
    do{
        $combo=$settings.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,'SystemSettings_Display_Scaling_ItemSizeOverride_ComboBox'))
        if(-not $combo){Start-Sleep -Milliseconds 200}
    }while(-not $combo -and [DateTime]::UtcNow -lt $deadline)
    if(-not $combo){throw 'No se encontró el selector de escala de Windows.'}
    $combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 300
    $choice=$settings.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem))|Where-Object{($_.Current.Name -replace '\s','') -like ($Scale.ToString()+'%*')}|Select-Object -First 1
    if(-not $choice){throw "No se encontró la escala $Scale %."}
    $choice.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 1000
}finally{$settings.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close()}
$process=Get-Process -Name 'Lims.Planning.Validation'
$window=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,[int]$process.Id))|Where-Object{$_.Current.Name -eq 'LIMS'}|Select-Object -First 1
$dpi=[ScaleValidationWindow]::GetDpiForWindow([IntPtr]$window.Current.NativeWindowHandle)
if($dpi -ne ($Scale*96/100)){throw "Windows reporta $dpi DPI después de elegir $Scale %."}
Write-Output "Escala efectiva de la ventana nativa: $Scale % ($dpi DPI)."
