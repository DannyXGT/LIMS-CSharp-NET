param([switch]$SkipStart)
$ErrorActionPreference = 'Stop'
$driver = Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$folder = 'preparation-tabs'
$evidence = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ('..\artifacts\validation\' + $folder)))
function Send-Command($data) { & $driver -Action Command -Command ($data | ConvertTo-Json -Compress) -EvidenceFolder $folder }
function Capture($name) { Start-Sleep -Milliseconds 300; & $driver -Action Capture -Name $name -EvidenceFolder $folder }
function Luminance($color) {
    $hex = $color.TrimStart('#')
    if ($hex.Length -eq 8) { $hex = $hex.Substring(2) }
    $channels = @(0,2,4) | ForEach-Object {
        $channel = [Convert]::ToInt32($hex.Substring($_,2),16) / 255.0
        if ($channel -le 0.04045) { $channel / 12.92 } else { [Math]::Pow(($channel + 0.055) / 1.055, 2.4) }
    }
    return 0.2126 * $channels[0] + 0.7152 * $channels[1] + 0.0722 * $channels[2]
}
function Contrast($first, $second) {
    $a = Luminance $first; $b = Luminance $second
    return ([Math]::Max($a,$b) + 0.05) / ([Math]::Min($a,$b) + 0.05)
}
function Assert-Tabs($index, $name) {
    Send-Command @{ action = 'tabReport' }
    $report = Get-Content -LiteralPath (Join-Path $evidence 'tabs.json') -Raw | ConvertFrom-Json
    if ($report.Type -ne 'Tab' -or $report.SelectionCount -ne 1 -or -not $report.IsSelectionRequired -or $report.CanSelectMultiple) { throw 'Invalid tab strip selection provider.' }
    if ($report.SelectedIndex -ne $index -or -not $report.ContentMatchesSelection) { throw 'Tab and content selection disagree.' }
    for ($i = 0; $i -lt $report.Tabs.Count; $i++) {
        $tab = $report.Tabs[$i]
        if ($tab.Type -ne 'TabItem' -or $tab.IsSelected -ne ($i -eq $index) -or $tab.IsTabStop -ne ($i -eq $index)) { throw 'Invalid tab accessibility or keyboard stop.' }
        if ($tab.ActualHeight -lt 40 -or $tab.ActualWidth -le 40) { throw 'Unreadable tab bounds.' }
        if ((Contrast $tab.Foreground $report.Background) -lt 4.5) { throw 'Tab text contrast is below 4.5:1.' }
        if ($tab.Weight -ne $(if ($i -eq $index) { 600 } else { 400 })) { throw 'Incorrect active/inactive font weight.' }
    }
    $expected = if ($index -eq 0) { 'Nuevo Stock' } else { 'Nueva Intermedia' }
    if ($report.Action -ne $expected) { throw 'Incorrect header action.' }
    if (($report.Tabs | Measure-Object ActualWidth -Sum).Sum -gt $report.StripWidth) { throw 'Tabs overflow their strip.' }
    if ($report.FourTabWidth -le 0 -or $report.FourTabWidth -gt $report.StripWidth) { throw 'Four tabs do not fit the desktop strip.' }
    # WinUI rounds the nominal 3 DIP line to device pixels (3.2 DIP at 125% DPI).
    if ([Math]::Abs($report.IndicatorHeight - 3) -gt 0.5) { throw 'The accent underline must be approximately 3 DIP high.' }
    if ((Contrast $report.IndicatorColor $report.Background) -lt 3) { throw 'Underline contrast is below 3:1.' }
    Copy-Item -LiteralPath (Join-Path $evidence 'tabs.json') -Destination (Join-Path $evidence ($name + '.json'))
}
if (-not $SkipStart) { & $driver -Action Start -SkipBuild -IsolatedIdentity -EvidenceFolder $folder }
$deadline = [DateTime]::UtcNow.AddSeconds(45)
while (-not (Test-Path -LiteralPath (Join-Path $evidence 'window.json'))) {
    if ([DateTime]::UtcNow -ge $deadline) { throw 'Visual harness did not launch.' }
    Start-Sleep -Milliseconds 200
}
Send-Command @{ action = 'preparations' }
Send-Command @{ action = 'tabSelect'; index = 0 }
Send-Command @{ action = 'dark' }

# Use real UI Automation and physical pointer input, restricted to this fixture window.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class PreparationTabInput {
 [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
 [DllImport("user32.dll")] public static extern bool GetPhysicalCursorPos(out Point point);
 [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
'@
$handle = [IntPtr](Get-Content -LiteralPath (Join-Path $evidence 'window.json') | ConvertFrom-Json).hwnd
[PreparationTabInput]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null
$window = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
[PreparationTabInput]::ShowWindow($handle,9) | Out-Null
Send-Command @{ action = 'size'; width = 1920; height = 1080 }
Send-Command @{ action = 'tabCapacity' }
Capture 'stock-dark-1920'
Assert-Tabs 0 'stock-dark-1920'
function Find-Tab($label) {
    $condition = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $label),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem))
    $tab = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $tab) { throw "Tab not exposed to UI Automation: $label" }
    return $tab
}
function Move-Pointer($label) {
    [PreparationTabInput]::ShowWindow($handle,9) | Out-Null
    [PreparationTabInput]::SetForegroundWindow($handle) | Out-Null
    Start-Sleep -Milliseconds 150
    $inputOwner = [uint32]0
    [PreparationTabInput]::GetWindowThreadProcessId([PreparationTabInput]::GetForegroundWindow(), [ref]$inputOwner) | Out-Null
    if ($inputOwner -ne $window.Current.ProcessId) { throw 'Fixture does not own input.' }
    $bounds = (Find-Tab $label).Current.BoundingRectangle
    [PreparationTabInput]::SetCursorPos(30,30) | Out-Null
    Start-Sleep -Milliseconds 100
    $moved = [PreparationTabInput]::SetCursorPos([int]($bounds.X + $bounds.Width / 2), [int]($bounds.Y + $bounds.Height / 2))
    $cursor = [PreparationTabInput+Point]::new()
    [PreparationTabInput]::GetPhysicalCursorPos([ref]$cursor) | Out-Null
    $pointOwner = [uint32]0
    [PreparationTabInput]::GetWindowThreadProcessId([PreparationTabInput]::WindowFromPoint($cursor), [ref]$pointOwner) | Out-Null
    if (-not $moved -or $pointOwner -ne $window.Current.ProcessId) { throw 'Pointer did not reach the fixture tab.' }
    [PreparationTabInput]::mouse_event(1, 1, 0, 0, [UIntPtr]::Zero)
}
(Find-Tab 'Stock').SetFocus()
# Activate the fixture's pointer input through the already-selected tab before hover.
Move-Pointer 'Stock'
[PreparationTabInput]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 40
[PreparationTabInput]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
Move-Pointer 'Intermedia'
Capture 'hover-intermedia-inactive'
Assert-Tabs 0 'hover-intermedia-inactive'
$hoverReport = Get-Content -LiteralPath (Join-Path $evidence 'tabs.json') -Raw | ConvertFrom-Json
if ($hoverReport.Tabs[1].Foreground -ne $hoverReport.Tabs[0].Foreground) { throw 'Inactive hover did not use the primary text brush.' }
[PreparationTabInput]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 40
[PreparationTabInput]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
Capture 'intermedia-dark-1920'
Assert-Tabs 1 'intermedia-dark-1920'

Send-Command @{ action = 'tabFocus' }
& $driver -Action Key -Keys '{LEFT}' -EvidenceFolder $folder
Capture 'keyboard-focus-stock'
Assert-Tabs 0 'keyboard-focus-stock'
& $driver -Action Key -Keys '{RIGHT}' -EvidenceFolder $folder
Assert-Tabs 1 'keyboard-right'
& $driver -Action Key -Keys ' ' -EvidenceFolder $folder
Assert-Tabs 1 'keyboard-space'
& $driver -Action Key -Keys '{ENTER}' -EvidenceFolder $folder
Assert-Tabs 1 'keyboard-enter'
& $driver -Action Key -Keys '{TAB}' -EvidenceFolder $folder
Send-Command @{ action = 'tabReport' }
$exitReport = Get-Content -LiteralPath (Join-Path $evidence 'tabs.json') -Raw | ConvertFrom-Json
if (@($exitReport.Tabs | Where-Object Focus -ne 'Unfocused').Count -ne 0) { throw 'Tab key did not leave the tab strip.' }
& $driver -Action Key -Keys '+{TAB}' -EvidenceFolder $folder
Send-Command @{ action = 'tabReport' }
$entryReport = Get-Content -LiteralPath (Join-Path $evidence 'tabs.json') -Raw | ConvertFrom-Json
if ($entryReport.Tabs[1].Focus -ne 'Keyboard') { throw 'Shift+Tab did not return to selected tab.' }

# UIA clients can also select a tab through SelectionItemPattern.
$stockTab = Find-Tab 'Stock'
([System.Windows.Automation.SelectionItemPattern]$stockTab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
Assert-Tabs 0 'uia-selection'

foreach ($size in @(@(1600,900), @(1366,768))) {
    Send-Command @{ action = 'size'; width = $size[0]; height = $size[1] }
    Send-Command @{ action = 'tabCapacity' }
    foreach ($index in @(0,1)) {
        Send-Command @{ action = 'tabSelect'; index = $index }
        $name = $(if ($index -eq 0) { 'stock' } else { 'intermedia' }) + '-dark-' + $size[0]
        Capture $name
        Assert-Tabs $index $name
    }
}
Send-Command @{ action = 'light' }
foreach ($index in @(0,1)) {
    Send-Command @{ action = 'tabSelect'; index = $index }
    $name = $(if ($index -eq 0) { 'stock' } else { 'intermedia' }) + '-light-1366'
    Capture $name
    Assert-Tabs $index $name
}
& $driver -Action Inspect -Name 'automation-tree' -EvidenceFolder $folder
Send-Command @{ action = 'size'; width = 1920; height = 1080 }
foreach ($index in @(0,1)) {
    Send-Command @{ action = 'tabSelect'; index = $index }
    $actionLabel = if ($index -eq 0) { 'Nuevo Stock' } else { 'Nueva Intermedia' }
    $fieldLabel = if ($index -eq 0) { 'Nombre del Stock' } else { 'Nombre de la Intermedia' }
    & $driver -Action Invoke -Name $actionLabel -EvidenceFolder $folder
    Start-Sleep -Milliseconds 300
    $fieldCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $fieldLabel)
    $field = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $fieldCondition)
    if ($null -eq $field -or $field.Current.IsOffscreen) { throw "Header action did not open the correct editor: $actionLabel" }
    Send-Command @{ action = $(if ($index -eq 0) { 'stockClose' } else { 'intClose' }) }
    Start-Sleep -Milliseconds 300
}
Send-Command @{ action = 'size'; width = 1366; height = 768 }
@{ FixtureOnly = $true; StockActionOpensStockEditor = $true; IntermediateActionOpensIntermediateEditor = $true } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'header-actions.json') -Encoding UTF8
Write-Output 'Preparation tabs: pointer, keyboard, UIA selection, desktop sizes and both themes passed (fixture UI).'
