param([string]$EvidenceFolder = 'standards-detail-workspace')
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$evidence = Join-Path $workspace ('artifacts\validation\' + $EvidenceFolder)
$driver = Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$checks = [Collections.Generic.List[string]]::new()
function Command($data) { & $driver -Action Command -Command ($data | ConvertTo-Json -Compress) -EvidenceFolder $EvidenceFolder }
function State {
    Command @{action='report'}
    Get-Content -LiteralPath (Join-Path $evidence 'layout.json') | ConvertFrom-Json
}
function Open($index) {
    Command @{action='select'; index=$index}
    Command @{action='detail'}
    Start-Sleep -Milliseconds 350
    $state = State
    if (-not $state.detailOpen -or $state.detailHeight -le 0) { throw 'No rendered sheet.' }
}
function Capture($name, $section) {
    State | Out-Null
    if ($section) { & $driver -Action Capture -Name $name -Section $section -EvidenceFolder $EvidenceFolder | Out-Null }
    else { & $driver -Action Capture -Name $name -EvidenceFolder $EvidenceFolder | Out-Null }
}
function AssertLayout($name) {
    Start-Sleep -Milliseconds 250
    $layout = State
    if ([Math]::Abs($layout.detailX * 2 + $layout.detailWidth - $layout.clientWidth) -gt 2 -or
        [Math]::Abs($layout.detailY * 2 + $layout.detailHeight - $layout.clientHeight) -gt 2) { throw 'Sheet is not centered in the client area.' }
    if ($layout.detailHeight -gt ($layout.clientHeight * 0.88) -or $layout.detailHeight -lt ($layout.clientHeight * 0.82) -or
        $layout.detailWidth -gt ($layout.clientWidth * 0.92) -or $layout.detailWidth -lt ($layout.clientWidth * 0.88)) { throw 'Sheet does not occupy the requested client proportions.' }
    if ($layout.detailViewport -le 0) { throw 'No internal viewport.' }
    Copy-Item -LiteralPath (Join-Path $evidence 'layout.json') -Destination (Join-Path $evidence ($name + '.json'))
    $checks.Add("Centered workspace: $name")
    return $layout
}

Command @{action='standards'}
Command @{action='size'; width=1920; height=1080}
Open 1
$layout = AssertLayout 'layout-4-aminobiphenyl-1920x1080'
if ($layout.detailExtent -gt $layout.detailViewport + 1) { throw 'Normal desktop content requires scrolling.' }
Capture '01-4-aminobiphenyl-1920x1080' ''
Capture '04-disponibilidad-vencimiento-estado' 'KpiGrid'
Capture '05-informacion-general' 'GeneralSection'
Capture '06-disponibilidad' 'AvailabilityCard'
Capture '07-auditoria' 'AuditSection'
Capture '08-header-acciones' 'HeaderGrid'
$checks.Add('Normal desktop content including audit fits without scrolling')
$checks.Add('Ten real screenshots include sections captured directly from the rendered fixture window')
Command @{action='closeDetail'}
Open 0
AssertLayout 'layout-naphthol-as-1920x1080' | Out-Null
Capture '02-naphthol-as-1920x1080' ''
Command @{action='closeDetail'}
Open 1
Command @{action='size'; width=1600; height=900}
AssertLayout 'layout-1600x900' | Out-Null
Capture 'detalle-1600x900' ''
Command @{action='size'; width=1366; height=768}
AssertLayout 'layout-4-aminobiphenyl-1366x768' | Out-Null
Capture '03-4-aminobiphenyl-1366x768' ''
Command @{action='size'; width=1000; height=800}
$narrow = AssertLayout 'layout-vertical-1000x800'
if (-not $narrow.bodyStacked -or $narrow.detailExtent -le $narrow.detailViewport) { throw 'Narrow layout did not stack with internal scrolling.' }
Capture 'layout-vertical-1000x800' ''
$checks.Add('Narrow layout stacks operation sections and preserves internal scrolling')
Command @{action='closeDetail'}
$checks | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'workspace-visual-checks.json') -Encoding UTF8
$checks
