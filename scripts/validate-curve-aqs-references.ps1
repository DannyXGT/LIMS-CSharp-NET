param([string]$EvidenceFolder='curve-aqs-final')
$ErrorActionPreference='Stop'
$driver=Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$evidence=Join-Path $PSScriptRoot ('..\artifacts\validation\'+$EvidenceFolder)
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
function Send($data) { & $driver -Action Command -Command ($data | ConvertTo-Json -Compress) -EvidenceFolder $EvidenceFolder }
function Expand($name) {
 $condition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$name)
 foreach($element in $script:root.FindAll([System.Windows.Automation.TreeScope]::Descendants,$condition)) {
  $pattern=$null
  if(-not $element.Current.IsOffscreen -and $element.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern,[ref]$pattern)) { $pattern.Expand(); return }
 }
 throw ('No se encuentra el expansor: '+$name)
}
Send @{action='size';width=1920;height=1080}; Send @{action='group';kind='Curva'}; Send @{action='groupDetail';kind='Curva'}; Start-Sleep -Milliseconds 350
$window=Get-Content (Join-Path $evidence 'window.json') | ConvertFrom-Json
$root=[System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$window.hwnd)
Expand 'Referencias del origen · CAS y lotes'
Send @{action='groupDetailScroll';kind='Curva';offset=9999}; Start-Sleep -Milliseconds 250
& $driver -Action Capture -Name 'Curva-trace-expanded' -EvidenceFolder $EvidenceFolder
Expand 'Registro y referencias de movimientos'
Send @{action='groupDetailScroll';kind='Curva';offset=9999}; Start-Sleep -Milliseconds 250
& $driver -Action Capture -Name 'Curva-audit-expanded' -EvidenceFolder $EvidenceFolder
& $driver -Action Invoke -Name 'Cerrar' -EvidenceFolder $EvidenceFolder
