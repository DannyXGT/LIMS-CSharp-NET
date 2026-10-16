param([string]$EvidenceFolder = 'preparations-workspace')
$ErrorActionPreference = 'Stop'
$driver = Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$evidence = Join-Path $PSScriptRoot ('..\artifacts\validation\' + $EvidenceFolder)
function Send-Fixture($data) { & $driver -Action Command -Command ($data | ConvertTo-Json -Compress) -EvidenceFolder $EvidenceFolder }
function Capture-Fixture($name) { & $driver -Action Capture -Name $name -EvidenceFolder $EvidenceFolder }
& (Join-Path $PSScriptRoot 'validate-preparation-list.ps1') -EvidenceFolder $EvidenceFolder
$checks = @()
foreach ($size in @(@(1920,1080), @(1600,900), @(1366,768))) {
    Send-Fixture @{ action = 'size'; width = $size[0]; height = $size[1] }
    foreach ($scene in @('Default','Low','Depleted','Expired','Unknown','Empty','Error','Loading')) {
        Send-Fixture @{ action = 'preparationScenario'; scene = $scene }
        Capture-Fixture ('stock-' + $scene.ToLowerInvariant() + '-' + $size[0])
        Send-Fixture @{ action = 'preparationListReport' }
        $report = Get-Content -Raw -LiteralPath (Join-Path $evidence 'preparation-list.json') | ConvertFrom-Json
        if ($scene -eq 'Error' -and (-not $report.HasListMessage -or $report.EmptyVisible -eq 'Visible')) { throw 'Error presented as empty.' }
        if ($scene -eq 'Loading' -and -not $report.IsLoadingList) { throw 'Loading indicator missing.' }
        if ($scene -eq 'Empty' -and ($report.EmptyVisible -ne 'Visible' -or $report.EmptyHeight -gt 240)) { throw 'Empty state is not compact.' }
        $checks += @{ width = $size[0]; height = $size[1]; scene = $scene; report = $report }
        if ($scene -eq 'Loading') { Start-Sleep -Milliseconds 1900 }
    }
}
Send-Fixture @{ action = 'preparationScenario'; scene = 'Default' }
Send-Fixture @{ action = 'size'; width = 1366; height = 768 }
Send-Fixture @{ action = 'tabSelect'; index = 1 }
Capture-Fixture 'intermedia-empty-1366'
@{ fixtureOnly = $true; productionDatabaseModified = $false; checks = $checks } | ConvertTo-Json -Depth 20 |
    Set-Content -LiteralPath (Join-Path $evidence 'workspace-validation.json') -Encoding utf8
Write-Output 'WinUI workspace scenarios verified using isolated fixture APIs.'
