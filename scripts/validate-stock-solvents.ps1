param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$driver = Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$folder = 'stock-solvents'
$evidence = Join-Path $PSScriptRoot '..\artifacts\validation\stock-solvents'
if (-not $SkipBuild) {
    dotnet build (Join-Path $PSScriptRoot '..\Tests\Frontend\Lims.Desktop.VisualHarness\Lims.Desktop.VisualHarness.csproj') --no-restore -c Debug -p:Platform=x64 -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Visual harness build failed.' }
}
& $driver -Action Start -SkipBuild -IsolatedIdentity -EvidenceFolder $folder
$deadline = [DateTime]::UtcNow.AddSeconds(20)
while (-not (Test-Path (Join-Path $evidence 'window.json'))) {
    if ([DateTime]::UtcNow -gt $deadline) { throw 'The WinUI window did not start.' }
    Start-Sleep -Milliseconds 150
}
function Send-StockCommand($data) {
    & $driver -Action Command -Command ($data | ConvertTo-Json -Compress -Depth 10) -EvidenceFolder $folder
}
Send-StockCommand @{ action = 'preparations' }
Send-StockCommand @{ action = 'size'; width = 1920; height = 1080 }
Send-StockCommand @{ action = 'stockEditor' }
Start-Sleep -Milliseconds 600
Send-StockCommand @{ action = 'stockSelect'; index = 0 }
foreach ($field in @(
    @{ field = 'CompoundBox'; text = '4-Aminobiphenyl' },
    @{ field = 'ConcentrationBox'; text = '100' },
    @{ field = 'FinalQuantityBox'; text = '100' },
    @{ field = 'ActualQuantityBox'; text = '10.1' }
)) { $field.action = 'stockText'; Send-StockCommand $field }
Send-StockCommand @{ action = 'stockDate'; field = 'ExpirationDatePicker'; text = '2027-06-24T00:00:00-06:00' }
foreach ($mixture in @(
    @{ name = '50-50'; rows = @(@{id=4;percent='50'}, @{id=8;percent='50'}) },
    @{ name = '30-20-50'; rows = @(@{id=4;percent='30'}, @{id=3;percent='20'}, @{id=8;percent='50'}) }
)) {
    Send-StockCommand @{ action = 'stockMixture'; rows = $mixture.rows }
    foreach ($size in @(@{w=1920;h=1080}, @{w=1366;h=768})) {
        Send-StockCommand @{ action = 'size'; width = $size.w; height = $size.h }
        Send-StockCommand @{ action = 'stockScroll'; offset = 10000 }
        & $driver -Action Capture -Name ("mixture-" + $mixture.name + '-' + $size.w + 'x' + $size.h) -EvidenceFolder $folder
        Send-StockCommand @{ action = 'themeReport' }
        $layout = Get-Content -LiteralPath (Join-Path $evidence 'theme-layout.json') | ConvertFrom-Json
        foreach ($button in @($layout.dialog.cancel, $layout.dialog.save)) {
            if ($button.y -lt 0 -or ($button.y + $button.height) -gt ($layout.rootSize.Height + 1)) {
                throw 'The footer is outside the WinUI viewport.'
            }
        }
        Copy-Item -LiteralPath (Join-Path $evidence 'theme-layout.json') -Destination (Join-Path $evidence ("layout-" + $mixture.name + '-' + $size.w + '.json'))
    }
}
Send-StockCommand @{ action = 'stockReport' }
$report = Get-Content -LiteralPath (Join-Path $evidence 'interaction.json') | ConvertFrom-Json
if (-not $report.CanSave) { throw 'The complete mixture did not enable creation.' }
Send-StockCommand @{ action = 'stockSave' }
Send-StockCommand @{ action = 'stockReport' }
$saved = Get-Content -LiteralPath (Join-Path $evidence 'interaction.json') | ConvertFrom-Json
if ($null -eq $saved.Created -or $saved.Created.Compound -ne '4-Aminobiphenyl' -or
    ($saved.Created.Solvents.Percent -join ':') -ne '30:20:50') { throw 'The UI save did not retain compound and composition.' }
Copy-Item -LiteralPath (Join-Path $evidence 'interaction.json') -Destination (Join-Path $evidence 'saved-mixture.json')
Send-StockCommand @{ action = 'stockClose' }
Send-StockCommand @{ action = 'size'; width = 1920; height = 1080 }
Send-StockCommand @{ action = 'stockDetail' }
& $driver -Action Capture -Name 'detail-30-20-50-1920x1080' -EvidenceFolder $folder
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$state = Get-Content -LiteralPath (Join-Path $evidence 'window.json') | ConvertFrom-Json
$fixtureWindow = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$state.hwnd)
$fixtureWindow.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
Write-Output 'Real WinUI presentation with isolated UI fixtures only. PostgreSQL persistence is tested separately.'
