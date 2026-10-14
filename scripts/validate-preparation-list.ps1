param([string]$EvidenceFolder = 'preparation-final', [switch]$SkipStart)
$ErrorActionPreference = 'Stop'
$driver = Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$evidence = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ('..\artifacts\validation\' + $EvidenceFolder)))
function Send-Command($data) { & $driver -Action Command -Command ($data | ConvertTo-Json -Compress) -EvidenceFolder $EvidenceFolder }
function Capture($name) { Start-Sleep -Milliseconds 250; & $driver -Action Capture -Name $name -EvidenceFolder $EvidenceFolder }
function Report($name) {
    Send-Command @{ action = 'preparationListReport' }
    $report = Get-Content -LiteralPath (Join-Path $evidence 'preparation-list.json') -Raw | ConvertFrom-Json
    Copy-Item -LiteralPath (Join-Path $evidence 'preparation-list.json') -Destination (Join-Path $evidence ($name + '.json'))
    return $report
}
function Assert-Disabled($name, $count) {
    $report = Report $name
    if ($report.Count -ne $count -or $null -ne $report.SelectedId -or $report.CanOpen -or $report.ButtonEnabled) { throw "Detail enabled without selection: $name" }
}
if (-not $SkipStart) {
    if (Test-Path -LiteralPath (Join-Path $evidence 'window.json')) {
        Send-Command @{ action = 'exit' }
        Start-Sleep -Milliseconds 300
    }
    & $driver -Action Start -SkipBuild -IsolatedIdentity -EvidenceFolder $EvidenceFolder
}
$deadline = [DateTime]::UtcNow.AddSeconds(45)
while (-not (Test-Path -LiteralPath (Join-Path $evidence 'window.json'))) {
    if ([DateTime]::UtcNow -ge $deadline) { throw 'Visual harness did not launch.' }
    Start-Sleep -Milliseconds 200
}
Send-Command @{ action = 'preparations' }
Send-Command @{ action = 'tabSelect'; index = 0 }
foreach ($size in @(@(1920,1080), @(1600,900), @(1366,768))) {
    Send-Command @{ action = 'size'; width = $size[0]; height = $size[1] }
    Capture ('stock-empty-disabled-' + $size[0])
    Assert-Disabled ('empty-' + $size[0]) 0
}
Send-Command @{ action = 'size'; width = 1920; height = 1080 }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$handle = [IntPtr](Get-Content -LiteralPath (Join-Path $evidence 'window.json') | ConvertFrom-Json).hwnd
$window = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
$searchCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'Buscar preparaciones')
$searchBox = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $searchCondition)
([System.Windows.Automation.ValuePattern]$searchBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)).SetValue('sin-resultados')
$searchBox.SetFocus()
& $driver -Action Key -Keys '{ENTER}' -EvidenceFolder $EvidenceFolder
$filtered = Report 'search-empty'
if (-not $filtered.HasSearchCriteria -or $filtered.EmptyVisible -ne 'Visible') { throw 'Enter did not show the filtered empty state.' }
Capture 'stock-search-empty-1920'
& $driver -Action Invoke -Name 'Limpiar búsqueda' -EvidenceFolder $EvidenceFolder
Start-Sleep -Milliseconds 200
$cleared = Report 'search-cleared'
if ($cleared.HasSearchCriteria -or $cleared.EmptyVisible -ne 'Visible') { throw 'Clear search did not restore the initial empty state.' }

# Exercise existing creation UI in memory only. This harness has no production HTTP or database.
# No seeded rows or production records are created for these tests.
Send-Command @{ action = 'stockEditor' }
Start-Sleep -Milliseconds 300
Send-Command @{ action = 'stockSelect'; index = 0 }
foreach ($field in @(@('NameBox','Stock · prueba de selección'), @('ConcentrationBox','100'), @('FinalQuantityBox','100'), @('ActualQuantityBox','10.088'))) {
    Send-Command @{ action = 'stockText'; field = $field[0]; text = $field[1] }
}
Send-Command @{ action = 'stockDate'; field = 'ExpirationDatePicker'; text = '2027-01-01T00:00:00-06:00' }
& $driver -Action Invoke -Name 'Crear Stock' -EvidenceFolder $EvidenceFolder
Start-Sleep -Milliseconds 300
Send-Command @{ action = 'stockReport' }
$created = Get-Content -LiteralPath (Join-Path $evidence 'interaction.json') -Raw | ConvertFrom-Json
if ($null -eq $created.Created) { throw 'Existing fixture creation flow failed.' }
Send-Command @{ action = 'stockClose' }
Start-Sleep -Milliseconds 250
Capture 'stock-record-unselected-disabled-1920'
Assert-Disabled 'record-unselected' 1

$listCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'Preparaciones Stock')
$list = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $listCondition)
if ($null -eq $list) { throw 'Preparation list not found.' }
$rowCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
$row = $list.FindAll([System.Windows.Automation.TreeScope]::Descendants, $rowCondition) | Where-Object { -not $_.Current.IsOffscreen } | Select-Object -First 1
if ($null -eq $row) { throw 'No real list row found in fixture UI.' }
$rowName = $row.Current.Name
& $driver -Action Click -Name $rowName -EvidenceFolder $EvidenceFolder
Capture 'stock-selected-enabled-1920'
$selected = Report 'selected'
if (-not $selected.CanOpen -or -not $selected.ButtonEnabled -or $selected.SelectedId -ne $created.Created.Preparation.Id) { throw 'Pointer selection did not enable the correct preparation.' }

foreach ($route in @('button','double-click','enter')) {
    switch ($route) {
        'button' { & $driver -Action Invoke -Name 'Ver preparación' -EvidenceFolder $EvidenceFolder }
        'double-click' { & $driver -Action DoubleClick -Name $rowName -EvidenceFolder $EvidenceFolder }
        'enter' { $row.SetFocus(); & $driver -Action Key -Keys '{ENTER}' -EvidenceFolder $EvidenceFolder }
    }
    Start-Sleep -Milliseconds 300
    $opened = Report ('detail-' + $route)
    if ($opened.DetailId -ne $selected.SelectedId) { throw "Wrong sheet opened through $route" }
    Capture ('detail-' + $route)
    Send-Command @{ action = 'preparationCloseDetail' }
    Start-Sleep -Milliseconds 250
}
Send-Command @{ action = 'preparationReload' }
$refreshed = Report 'refresh'
if ($refreshed.SelectedId -ne $selected.SelectedId -or -not $refreshed.ButtonEnabled) { throw 'Refresh lost the existing selection.' }
Send-Command @{ action = 'tabSelect'; index = 1 }
Capture 'intermedia-active-1920'
Assert-Disabled 'tab-change' 1
$tabState = Report 'intermedia-selection'
if ($null -ne $tabState.IntermediateSelectedId -or $tabState.IntermediateCanOpen) { throw 'Intermediate tab inherited selection.' }
Send-Command @{ action = 'tabSelect'; index = 0 }
Assert-Disabled 'tab-return' 1
Send-Command @{ action = 'size'; width = 1366; height = 768 }
Capture 'stock-record-unselected-1366'
Write-Output 'Preparation list: real WinUI selection, button, double click, Enter, refresh and tab reset passed. Fixture UI only; no production persistence.'
