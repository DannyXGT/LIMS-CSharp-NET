param([string]$EvidenceFolder='curve-aqs-final',[string]$Kind='AQS')
$ErrorActionPreference='Stop'
$driver=Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$evidence=Join-Path $PSScriptRoot ('..\artifacts\validation\'+$EvidenceFolder)
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
function Send($data) { & $driver -Action Command -Command ($data | ConvertTo-Json -Compress) -EvidenceFolder $EvidenceFolder }
Send @{action='size';width=1920;height=1080}; Send @{action='group';kind=$Kind}; Send @{action='groupEditor';kind=$Kind}; Start-Sleep -Milliseconds 350
Send @{action='groupScenario';kind=$Kind;code=($Kind+'-SALDO');volumes=@(2,3,5);selectedIndex=0}
Send @{action='groupScroll';kind=$Kind;offset=145}; Start-Sleep -Milliseconds 250
$state=Get-Content (Join-Path $evidence 'window.json') | ConvertFrom-Json
$root=[System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$state.hwnd)
$candidates=@()
foreach($element in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)) {
 $pattern=$null
 if($element.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern,[ref]$pattern) -and $pattern.Current.VerticallyScrollable) {
  $rect=$element.Current.BoundingRectangle
  if(-not $element.Current.IsOffscreen -and $rect.Width -gt 300 -and $rect.Width -lt 650 -and $rect.Height -gt 200) { $candidates+=@{element=$element;pattern=$pattern;height=$rect.Height} }
 }
}
$target=$candidates | Sort-Object height -Descending | Select-Object -First 1
if($null -ne $target) { $target.pattern.SetScrollPercent(-1,100) }; Start-Sleep -Milliseconds 250
& $driver -Action Capture -Name ($Kind+'-shared-consumption') -EvidenceFolder $EvidenceFolder
@{fixtureOnly=$true;nativeScroll=($null -ne $target);verticalPercent=$(if($target){$target.pattern.Current.VerticalScrollPercent}else{0})} | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $evidence 'shared-scroll.json')
Send @{action='groupClose';kind=$Kind}
