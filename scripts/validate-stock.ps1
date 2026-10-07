param(
    [ValidateSet('Start','Command','Capture','Inspect','Invoke','Click','DoubleClick','Key')]
    [string]$Action = 'Inspect',
    [string]$StockAction,
    [string]$Name = 'stock',
    [string]$EvidenceFolder = 'stock-corrected',
    [string]$Field,
    [string]$Text,
    [int]$Index = 0,
    [int]$Width = 1920,
    [int]$Height = 1080,
    [double]$Offset = 0,
    [string]$Keys
)
$ErrorActionPreference = 'Stop'
# Reuse the existing isolated HWND/UI Automation capture driver, with Stock commands and fixtures.
$driver = Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
if ($Action -in @('Capture','Inspect','Invoke','Click','DoubleClick','Key')) {
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
    $statePath = Join-Path $PSScriptRoot ('..\artifacts\validation\' + $EvidenceFolder + '\window.json')
    $fixtureState = Get-Content -LiteralPath $statePath | ConvertFrom-Json
    $fixtureWindow = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$fixtureState.hwnd)
    $fixturePattern = $fixtureWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
    if ($fixturePattern.Current.WindowVisualState -ne [System.Windows.Automation.WindowVisualState]::Normal) {
        $fixturePattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal)
    }
    if ($PSBoundParameters.ContainsKey('Width') -and $PSBoundParameters.ContainsKey('Height')) {
        $sizeCommand = @{ action = 'size'; width = $Width; height = $Height } | ConvertTo-Json -Compress
        & $driver -Action Command -Command $sizeCommand -EvidenceFolder $EvidenceFolder
    }
}
$arguments = @{ Action = $Action; Name = $Name; EvidenceFolder = $EvidenceFolder }
if ($Action -eq 'Start') { $arguments.SkipBuild = $true; $arguments.IsolatedIdentity = $true }
if ($Action -eq 'Command') {
    $command = @{ action = $StockAction }
    if ($StockAction -eq 'size') { $command.width = $Width; $command.height = $Height }
    if ($StockAction -in @('stockText','stockDate')) { $command.field = $Field; $command.text = $Text }
    if ($StockAction -eq 'stockSelect') { $command.index = $Index }
    if ($StockAction -eq 'stockScroll') { $command.offset = $Offset }
    $arguments.Command = $command | ConvertTo-Json -Compress
}
if ($Action -eq 'Key') { $arguments.Keys = $Keys }
& $driver @arguments
