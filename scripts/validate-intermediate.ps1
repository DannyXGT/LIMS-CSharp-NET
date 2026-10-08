param(
    [ValidateSet('Start','Scenarios','Finish','Responsive','Exit')][string]$Action = 'Scenarios',
    [string]$EvidenceFolder = 'intermediate'
)
$ErrorActionPreference = 'Stop'
$driver = Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$evidence = Join-Path $PSScriptRoot ('..\artifacts\validation\' + $EvidenceFolder)
function Send-FixtureCommand($data) { & $driver -Action Command -Command ($data | ConvertTo-Json -Compress -Depth 8) -EvidenceFolder $EvidenceFolder }
function Capture-Fixture($name) { Start-Sleep -Milliseconds 300; & $driver -Action Capture -Name $name -EvidenceFolder $EvidenceFolder }
function Scenario($name, $volumes, $final = '100') {
    Send-FixtureCommand @{ action = 'intScenario'; volumes = @($volumes) }
    if ($final -ne '100') { Send-FixtureCommand @{ action = 'intText'; field = 'FinalVolumeBox'; text = $final } }
    Send-FixtureCommand @{ action = 'intScroll'; offset = 0 }
    Send-FixtureCommand @{ action = 'intReport' }
    $report = Get-Content -LiteralPath (Join-Path $evidence 'intermediate-report.json') | ConvertFrom-Json
    $expected = ($volumes | Measure-Object -Sum).Sum / [decimal]$final * 100
    if ($report.OccupiedPercent -ne $expected) { throw "Cylinder mismatch: $name" }
    if ($expected -gt 100 -and $report.CanSave) { throw "Excess must block create: $name" }
    [IO.File]::WriteAllText((Join-Path $evidence ($name + '.json')), ($report | ConvertTo-Json -Depth 30), [Text.UTF8Encoding]::new($false))
    Capture-Fixture ($name + '-1920')
}
switch ($Action) {
    'Start' { & $driver -Action Start -SkipBuild -IsolatedIdentity -EvidenceFolder $EvidenceFolder }
    'Scenarios' {
        Send-FixtureCommand @{ action = 'size'; width = 1920; height = 1080 }
        Send-FixtureCommand @{ action = 'intermediate' }
        Send-FixtureCommand @{ action = 'intEditor' }
        Capture-Fixture 'empty-1920'
        Scenario 'probe-0' @()
        Scenario 'one-component' @(10)
        Scenario 'three-components' @(10,10,5)
        Scenario 'probe-25' @(25)
        Scenario 'probe-75' @(25,25,25)
        Scenario 'probe-100' @(50,25,25)
        Scenario 'excess' @(110)
        Scenario 'insufficient' @(210) '300'
        Scenario 'ready' @(10,10,5)
    }
    'Finish' {
        & $driver -Action Invoke -Name 'Crear Intermedia' -EvidenceFolder $EvidenceFolder
        Capture-Fixture 'confirmation-1920'
        & $driver -Action Invoke -Name 'Confirmar' -EvidenceFolder $EvidenceFolder
        Capture-Fixture 'success-1920'
        Send-FixtureCommand @{ action = 'intReport' }
        $report = Get-Content -LiteralPath (Join-Path $evidence 'intermediate-report.json') | ConvertFrom-Json
        if ($report.CreateCalls -ne 1 -or $null -eq $report.Created -or $report.Created.Consumptions.Count -ne 3) { throw 'Fixture save was not verified.' }
        [IO.File]::WriteAllText((Join-Path $evidence 'verification.json'), ($report | ConvertTo-Json -Depth 30), [Text.UTF8Encoding]::new($false))
        & $driver -Action Invoke -Name 'Cerrar' -EvidenceFolder $EvidenceFolder
        Capture-Fixture 'list-1920'
        Send-FixtureCommand @{ action = 'intDetail' }
        Capture-Fixture 'detail-1920'
        & $driver -Action Click -Name 'Cerrar' -EvidenceFolder $EvidenceFolder
        Start-Sleep -Milliseconds 300
    }
    'Responsive' {
        Send-FixtureCommand @{ action = 'intEditor' }
        Send-FixtureCommand @{ action = 'intScenario'; volumes = @(10,10,5) }
        foreach ($size in @(@(1600,900), @(1366,768))) {
            Send-FixtureCommand @{ action = 'size'; width = $size[0]; height = $size[1] }
            Send-FixtureCommand @{ action = 'intScroll'; offset = 0 }
            Capture-Fixture ('responsive-' + $size[0])
            Send-FixtureCommand @{ action = 'intScroll'; offset = 10000 }
            Capture-Fixture ('result-' + $size[0])
            Send-FixtureCommand @{ action = 'intReport' }
            Copy-Item -LiteralPath (Join-Path $evidence 'intermediate-report.json') -Destination (Join-Path $evidence ('responsive-' + $size[0] + '.json'))
        }
    }
    'Exit' { Send-FixtureCommand @{ action = 'exit' } }
}
