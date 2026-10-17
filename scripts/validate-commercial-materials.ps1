param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$driver = Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$folder = 'commercial-materials'
$evidence = Join-Path $PSScriptRoot '..\artifacts\validation\commercial-materials'
foreach ($name in @('window.json', 'error.txt', 'crash.txt', 'ack.json', 'command.json')) {
    $path = Join-Path $evidence $name
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
}
if (-not $SkipBuild) {
    dotnet build (Join-Path $PSScriptRoot '..\Tests\Frontend\Lims.Desktop.VisualHarness\Lims.Desktop.VisualHarness.csproj') --no-restore -c Debug -p:Platform=x64 -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Visual harness build failed.' }
}
& $driver -Action Start -SkipBuild -IsolatedIdentity -EvidenceFolder $folder
$deadline = [DateTime]::UtcNow.AddSeconds(20)
while (-not (Test-Path (Join-Path $evidence 'window.json'))) {
    if ([DateTime]::UtcNow -gt $deadline) { throw 'The isolated WinUI window did not start.' }
    Start-Sleep -Milliseconds 100
}
function Send-Command($value) {
    & $driver -Action Command -Command ($value | ConvertTo-Json -Compress -Depth 12) -EvidenceFolder $folder
}
Send-Command @{ action = 'standards' }
Send-Command @{ action = 'reload' }
$today = [DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(-6)).ToString('yyyy-MM-dd')
foreach ($kind in @('CommercialSolution', 'CommercialMix')) {
    Send-Command @{ action = 'editor' }
    Send-Command @{ action = 'fill' }
    $components = @(@{ Id = [Guid]::NewGuid().ToString(); Name = 'Aluminio (prueba aislada)'; CasNumber = '7429-90-5'; CertifiedConcentration = 999; ConcentrationUnit = 'µg/mL'; ExpandedUncertainty = 5 })
    if ($kind -eq 'CommercialMix') {
        $components = @(0..2 | ForEach-Object { @{ Id = [Guid]::NewGuid().ToString(); Name = @('Analito A','Analito B','Analito C')[$_]; CertifiedConcentration = @(50,49.9,50.1)[$_]; ConcentrationUnit = 'µg/mL'; ExpandedUncertainty = 0.2; Lot = ('COMP-TEST-' + $_) } })
    }
    $certificate = @{ Reference = 'CERT-TEST-SOLO-VALIDACION'; Matrix = 'Acetonitrilo (dato de prueba)'; Traceability = 'Referencia de prueba aislada'; NominalConcentration = 1000; NominalUnit = 'µg/mL'; CoverageFactor = 2; OpenedDate = $today; UseAfterOpeningMonths = 15; Components = $components; OtherExpressions = @(@{Value=974;Unit='µg/g';ExpandedUncertainty=5}) }
    Send-Command @{ action = 'commercialData'; presentation = $kind; certificate = $certificate }
    Send-Command @{ action = 'text'; field = 'NameBox'; text = ($kind + ' - frasco de prueba aislada') }
    Send-Command @{ action = 'text'; field = 'PresentationBox'; text = '10' }
    Send-Command @{ action = 'commercialReport' }
    Copy-Item -LiteralPath (Join-Path $evidence 'commercial-report.json') -Destination (Join-Path $evidence ($kind + '-request.json'))
    foreach ($size in @(@{w=1920;h=1080}, @{w=1366;h=768})) {
        Send-Command @{ action = 'size'; width = $size.w; height = $size.h }
        Send-Command @{ action = 'editorScroll'; offset = 0 }
        & $driver -Action Capture -Name ($kind + '-form-' + $size.w + 'x' + $size.h) -EvidenceFolder $folder
        Send-Command @{ action = 'editorScroll'; offset = 680 }
        & $driver -Action Capture -Name ($kind + '-certificate-' + $size.w + 'x' + $size.h) -EvidenceFolder $folder
        Send-Command @{ action = 'report' }
        Copy-Item -LiteralPath (Join-Path $evidence 'layout.json') -Destination (Join-Path $evidence ($kind + '-form-layout-' + $size.w + '.json'))
    }
    & $driver -Action Invoke -Name 'Guardar estándar' -EvidenceFolder $folder
    Start-Sleep -Milliseconds 1400
    Send-Command @{ action = 'report' }
    $saved = Get-Content -LiteralPath (Join-Path $evidence 'layout.json') -Encoding UTF8 | ConvertFrom-Json
    if ($saved.selectedName -ne ($kind + ' - frasco de prueba aislada')) { throw 'The real editor did not save the expected isolated fixture.' }
    Send-Command @{ action = 'detail' }
    foreach ($size in @(@{w=1920;h=1080}, @{w=1366;h=768})) {
        Send-Command @{ action = 'size'; width = $size.w; height = $size.h }
        Send-Command @{ action = 'detailScroll'; offset = 0 }
        & $driver -Action Capture -Name ($kind + '-detail-' + $size.w + 'x' + $size.h) -EvidenceFolder $folder
        Send-Command @{ action = 'detailScroll'; offset = 580 }
        & $driver -Action Capture -Name ($kind + '-detail-components-' + $size.w + 'x' + $size.h) -EvidenceFolder $folder
    }
    Send-Command @{ action = 'closeDetail' }
}
Send-Command @{ action = 'intermediate' }
Send-Command @{ action = 'intEditor' }
Send-Command @{ action = 'commercialIntermediate' }
Send-Command @{ action = 'intReport' }
$intermediate = Get-Content -LiteralPath (Join-Path $evidence 'intermediate-report.json') -Encoding UTF8 | ConvertFrom-Json
if (($intermediate.Calculation.Results.Concentration -join ':') -ne '5:4.99:5.01') { throw 'The WinUI preview did not dilute every Mix analyte independently.' }
foreach ($size in @(@{w=1920;h=1080}, @{w=1366;h=768})) {
    Send-Command @{ action = 'size'; width = $size.w; height = $size.h }
    Send-Command @{ action = 'intScroll'; offset = 0 }
    & $driver -Action Capture -Name ('Mix-intermediate-' + $size.w + 'x' + $size.h) -EvidenceFolder $folder
    Send-Command @{ action = 'intScroll'; offset = 10000 }
    & $driver -Action Capture -Name ('Mix-intermediate-results-' + $size.w + 'x' + $size.h) -EvidenceFolder $folder
}
Send-Command @{ action = 'intClose' }
Send-Command @{ action = 'exit' }
Write-Output 'Real WinUI forms and details, isolated visual fixtures only. PostgreSQL is validated separately.'
