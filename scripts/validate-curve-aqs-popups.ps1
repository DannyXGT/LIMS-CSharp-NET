param([string]$EvidenceFolder='curve-aqs',[string[]]$Kinds=@('Curva','AQS'))
$ErrorActionPreference='Stop'
$driver=Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$evidence=Join-Path $PSScriptRoot ('..\artifacts\validation\'+$EvidenceFolder)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
function Send($data) { & $driver -Action Command -Command ($data | ConvertTo-Json -Compress) -EvidenceFolder $EvidenceFolder }
function Find-Control($name) {
    $nameCondition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$name)
    return $script:root.FindAll([System.Windows.Automation.TreeScope]::Descendants,$nameCondition) | Where-Object { -not $_.Current.IsOffscreen } | Select-Object -First 1
}
$window=Get-Content -LiteralPath (Join-Path $evidence 'window.json') | ConvertFrom-Json
$root=[System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$window.hwnd)
$close=Find-Control 'Cerrar'
if($null -ne $close) { $close.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 250 }
$cancel=Find-Control 'Cancelar'
if($null -ne $cancel) { $cancel.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 250 }
$windowPattern=$null
if($root.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern,[ref]$windowPattern)) { $windowPattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal) }
Send @{action='size';width=1366;height=768}
$reports=@()
foreach($kind in $Kinds) {
    if($null -ne $windowPattern) { $windowPattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal) }
    Send @{action='permissionActivate'}
    Send @{action='size';width=1366;height=768}
    Send @{action='group';kind=$kind}; Send @{action='groupEditor';kind=$kind}; Start-Sleep -Milliseconds 350
    Send @{action='groupScenario';kind=$kind;code=($kind+'-POPUP');volumes=@(2)}
    $date=Find-Control 'Fecha de preparación'
    if($null -eq $date) { throw 'No se encuentra la fecha de preparación.' }
    $pattern=$date.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $pattern.Expand(); Start-Sleep -Milliseconds 250
    $condition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Calendar)
    $calendar=[System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants,$condition) | Where-Object { $_.Current.ProcessId -eq $root.Current.ProcessId -and -not $_.Current.IsOffscreen } | Select-Object -First 1
    if($null -eq $calendar) { throw 'No se abrio el calendario nativo.' }
    $bounds=$calendar.Current.BoundingRectangle; $save=Find-Control ('Guardar '+$kind); $windowBounds=$root.Current.BoundingRectangle
    if($bounds.Left -lt $windowBounds.Left -or $bounds.Right -gt $windowBounds.Right+2 -or $bounds.Top -lt $windowBounds.Top -or $bounds.Bottom -gt $save.Current.BoundingRectangle.Top+2) { throw 'El calendario sale de la ventana o invade Guardar.' }
    & $driver -Action Capture -Name ($kind+'-calendar') -EvidenceFolder $EvidenceFolder
    $reports+=@{kind=$kind;calendar=$bounds.ToString();save=$save.Current.BoundingRectangle.ToString();passed=$true}
    $pattern.Collapse(); Start-Sleep -Milliseconds 100
    $source=Find-Control 'Intermedia de origen'
    if($null -eq $source) { throw 'No se encuentra el selector de Intermedia.' }
    $sourcePattern=$source.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $sourcePattern.Expand(); Start-Sleep -Milliseconds 250
    if($sourcePattern.Current.ExpandCollapseState -ne [System.Windows.Automation.ExpandCollapseState]::Expanded) { throw 'No se abrio el selector de Intermedia.' }
    & $driver -Action Capture -Name ($kind+'-source-dropdown') -EvidenceFolder $EvidenceFolder
    $sourcePattern.Collapse(); Send @{action='groupClose';kind=$kind}
}
@{fixtureOnly=$true;passed=$true;reports=$reports} | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 (Join-Path $evidence 'popup-verification.json')
