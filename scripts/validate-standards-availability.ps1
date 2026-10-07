param([string]$EvidenceFolder = 'standards-availability')
$ErrorActionPreference = 'Stop'
$evidence = Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))) ('artifacts\validation\' + $EvidenceFolder)
$driver = Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$checks = [Collections.Generic.List[string]]::new()
function Command($data) { & $driver -Action Command -Command ($data | ConvertTo-Json -Compress) -EvidenceFolder $EvidenceFolder }
function State { Command @{action='report'}; Get-Content -LiteralPath (Join-Path $evidence 'layout.json') | ConvertFrom-Json }
function Capture($name, [switch]$Full) {
    State | Out-Null
    if ($Full) { & $driver -Action Capture -Name $name -EvidenceFolder $EvidenceFolder | Out-Null }
    else { & $driver -Action Capture -Section AvailabilityCard -Name $name -EvidenceFolder $EvidenceFolder | Out-Null }
}
function Fixture($quantity, $status='Active') {
    Command @{action='availability'; quantity=$quantity; status=$status}
    Start-Sleep -Milliseconds 350
    State
}
Command @{action='standards'}
Command @{action='size'; width=1920; height=1080}
$deadline = [DateTime]::UtcNow.AddSeconds(8)
do { $loaded = State; Start-Sleep -Milliseconds 100 } while ($loaded.items -lt 2 -and [DateTime]::UtcNow -lt $deadline)
if ($loaded.items -lt 2) { throw 'Fixture list did not load.' }
Command @{action='select'; index=1}
Command @{action='detail'}
$deadline = [DateTime]::UtcNow.AddSeconds(8)
do { $selected = State; Start-Sleep -Milliseconds 100 } while ($selected.selectedName -ne '4-Aminobiphenyl' -and [DateTime]::UtcNow -lt $deadline)
if ($selected.selectedName -ne '4-Aminobiphenyl') { throw 'Fixture detail did not load.' }
foreach ($quantity in @(100,74,25,0,74.5,0.025)) {
    $status = if ($quantity -eq 0) { 'Depleted' } else { 'Active' }
    $layout = Fixture $quantity $status
    $a = $layout.availability
    $culture = [Globalization.CultureInfo]::GetCultureInfo('es-GT')
    $expectedQuantity = $quantity.ToString('0.############################', $culture) + ' mg'
    $expectedPercent = $quantity.ToString('0.##', $culture) + ' %'
    if ($a.quantity -ne $expectedQuantity -or $a.percent -ne $expectedPercent) { throw 'Existing decimal formatter was not preserved.' }
    if (-not $layout.detailOpen -or -not $a.meterVisible -or [Math]::Abs($a.value - $quantity) -gt 0.001) { throw 'Incorrect progress value.' }
    if ($a.height -ne 8 -or $a.trackHeight -ne 8) { throw 'Progress bar or track is not 8 DIP.' }
    if ([Math]::Abs($a.indicatorWidth - $a.trackWidth * $quantity / 100) -gt 1.5) { throw 'Native fill does not match availability.' }
    if ($a.qualifier -ne 'disponibles' -or $a.totals -notlike '* disponibles de 100 mg' -or $a.presentation -ne 'Presentación: 100 mg × 1 unidad') { throw 'Incorrect content hierarchy.' }
    if ($quantity -gt 0 -and $a.color -ne '#FF4CC38A') { throw 'Positive availability is not DesignSystem Success.' }
    if ($quantity -eq 0 -and $a.color -ne '#FFF2B84B') { throw 'Depleted fixture does not use its existing Warning tone.' }
    $card = $layout.detailSections | Where-Object name -eq AvailabilityCard
    if ($card.height -gt 199) { throw 'Availability card grew taller.' }
    $name = 'availability-' + $quantity.ToString([Globalization.CultureInfo]::InvariantCulture).Replace('.', '-')
    Copy-Item -LiteralPath (Join-Path $evidence 'layout.json') -Destination (Join-Path $evidence ($name + '.json'))
    Capture $name
    $checks.Add("Rendered quantity, percentage, native fill, color and compact height: $quantity")
}
$unknown = Fixture $null
if ($unknown.availability.meterVisible -or $unknown.availability.quantity -ne 'No informada') { throw 'Unknown availability invented a percentage.' }
$checks.Add('Unknown availability hides the meter')
$invalid = Fixture ([decimal]-1)
if ($invalid.availability.meterVisible) { throw 'Invalid negative availability exposes a percentage.' }
$checks.Add('Existing guard hides invalid negative percentage')
$blocked = Fixture 25 'Blocked'
if ($blocked.availability.color -ne '#FFFF7A84') { throw 'Blocked fixture does not use existing Danger tone.' }
Capture 'availability-blocked'
$checks.Add('Existing Blocked status uses Danger')
Fixture 100 | Out-Null
Capture 'availability-full-1920x1080' -Full
Command @{action='size'; width=1366; height=768}
Start-Sleep -Milliseconds 250
$narrow = State
if ($narrow.availability.iconVisible) { throw 'Narrow card did not hide the vial.' }
if ($narrow.availability.trackHeight -ne 8 -or $narrow.availability.trackWidth -lt 100) { throw 'Narrow meter lost readability.' }
Capture 'availability-1366x768' -Full
$checks.Add('Narrow card hides vial and retains readable 8 DIP meter')
Command @{action='closeDetail'}
Command @{action='size'; width=1920; height=1080}
$checks | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'availability-checks.json') -Encoding UTF8
$checks
