param([string]$EvidenceFolder = 'preparations-final', [switch]$SkipCreation)
$ErrorActionPreference = 'Stop'
$driver = Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$evidence = Join-Path $PSScriptRoot ('..\artifacts\validation\' + $EvidenceFolder)
function Send-Fixture($data) { & $driver -Action Command -Command ($data | ConvertTo-Json -Compress) -EvidenceFolder $EvidenceFolder }
function Capture-Fixture($name) { Start-Sleep -Milliseconds 700; & $driver -Action Capture -Name $name -EvidenceFolder $EvidenceFolder }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$handle = [IntPtr](Get-Content -Raw -LiteralPath (Join-Path $evidence 'window.json') | ConvertFrom-Json).hwnd
$window = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
function Invoke-WhenReady($name) {
    $condition = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button))
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        $button = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($button -and $button.Current.IsEnabled -and -not $button.Current.IsOffscreen) {
            ([System.Windows.Automation.InvokePattern]$button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
            return
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Visible enabled button not found: $name"
}
if (-not $SkipCreation) {
    & (Join-Path $PSScriptRoot 'validate-intermediate.ps1') -Action Scenarios -EvidenceFolder $EvidenceFolder
    Invoke-WhenReady 'Crear Intermedia'
    Capture-Fixture 'intermedia-confirmation'
    Invoke-WhenReady 'Confirmar'
    Start-Sleep -Milliseconds 500
}
Send-Fixture @{ action = 'intReport' }
$report = Get-Content -Raw -LiteralPath (Join-Path $evidence 'intermediate-report.json') | ConvertFrom-Json
if ($null -eq $report.Created -or $report.Created.Consumptions.Count -ne 3) { throw 'In-memory preparation creation did not complete.' }
if (-not $SkipCreation) { Capture-Fixture 'intermedia-success'; Invoke-WhenReady 'Cerrar' }
Send-Fixture @{ action = 'size'; width = 1366; height = 768 }
Capture-Fixture 'intermedia-list-1366'
$rowCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
$rowName = ($window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $rowCondition) | Where-Object { $_.Current.Name.StartsWith($report.Created.Preparation.Code, [StringComparison]::Ordinal) } | Select-Object -First 1).Current.Name
& $driver -Action Click -Name $rowName -EvidenceFolder $EvidenceFolder
Invoke-WhenReady ('Ver preparaci' + [char]0xF3 + 'n')
Capture-Fixture 'intermedia-detail-button-1366'
Invoke-WhenReady 'Cerrar'
& $driver -Action DoubleClick -Name $rowName -EvidenceFolder $EvidenceFolder
Capture-Fixture 'intermedia-detail-double-click-1366'
Invoke-WhenReady 'Cerrar'
& $driver -Action Click -Name $rowName -EvidenceFolder $EvidenceFolder
& $driver -Action Key -Keys '{ENTER}' -EvidenceFolder $EvidenceFolder
Capture-Fixture 'intermedia-detail-enter-1366'
Invoke-WhenReady 'Cerrar'
@{ fixtureOnly = $true; createdId = $report.Created.Preparation.Id; consumptionCount = $report.Created.Consumptions.Count; verified = @('button','double-click','enter'); productionDatabaseModified = $false } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'intermedia-verification.json') -Encoding utf8
