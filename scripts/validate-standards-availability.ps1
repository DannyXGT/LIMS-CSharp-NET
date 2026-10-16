param(
    [string]$EvidenceFolder = 'standards-availability',
    [switch]$MixedAvailability,
    [switch]$AllowFailures
)
$ErrorActionPreference = 'Stop'
$driver = Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$evidence = Join-Path (Split-Path $PSScriptRoot) ('artifacts\validation\' + $EvidenceFolder)
$reports = [Collections.Generic.List[object]]::new()

function Send-Command($data) {
    & $driver -Action Command -EvidenceFolder $EvidenceFolder -Command ($data | ConvertTo-Json -Compress)
    Start-Sleep -Milliseconds 350
}
function Check-Rows([string]$name) {
    Send-Command @{ action = 'availabilityRows' }
    $path = Join-Path $evidence 'availability-rows.json'
    $report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    Copy-Item -LiteralPath $path -Destination (Join-Path $evidence ($name + '.json'))
    $reports.Add([pscustomobject]@{ name = $name; passed = $report.passed; visibleRows = $report.visibleRows })
}

Send-Command @{ action = 'size'; width = 1920; height = 1080 }
Send-Command @{ action = 'standards' }
Send-Command @{ action = 'pane'; open = $false }
if ($MixedAvailability) {
    Send-Command @{ action = 'dashboard'; scenario = 'Administrator' }
    Send-Command @{ action = 'standards' }
    Send-Command @{ action = 'reload' }
}
Check-Rows 'initial'
for ($cycle = 1; $cycle -le 3; $cycle++) {
    foreach ($offset in @(360, 900, 0)) {
        Send-Command @{ action = 'availabilityRows'; offset = $offset }
        Check-Rows "cycle-$cycle-scroll-$offset"
    }
    Send-Command @{ action = 'home' }
    Send-Command @{ action = 'standards' }
    Check-Rows "cycle-$cycle-reenter"
    Send-Command @{ action = 'reload' }
    Check-Rows "cycle-$cycle-reload"
}
Send-Command @{ action = 'availabilityRows'; pageDelta = 1 }
Check-Rows 'next-page'
Send-Command @{ action = 'availabilityRows'; pageDelta = -1 }
Check-Rows 'previous-page'
Send-Command @{ action = 'size'; width = 1100; height = 900 }
Check-Rows 'compact'
Send-Command @{ action = 'size'; width = 1920; height = 1080 }
Check-Rows 'wide-again'

$summary = [pscustomobject]@{ fixtureOnly = $true; mixedAvailability = [bool]$MixedAvailability; passed = @($reports | Where-Object { -not $_.passed }).Count -eq 0; checks = $reports }
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'availability-summary.json') -Encoding UTF8
$summary | ConvertTo-Json -Depth 6
if (-not $summary.passed -and -not $AllowFailures) { throw 'Availability row regression failed. Inspect availability-summary.json.' }
