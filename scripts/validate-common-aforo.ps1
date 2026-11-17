param([switch]$SkipStart)
$ErrorActionPreference = 'Stop'
$driver = Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$folder = 'common-aforo'
$evidence = Join-Path $PSScriptRoot '..\artifacts\validation\common-aforo'
function Send($data) { & $driver -Action Command -Command ($data | ConvertTo-Json -Compress) -EvidenceFolder $folder }
function Click($name) { & $driver -Action Click -Name $name -EvidenceFolder $folder }
function EnterValue($text) { & $driver -Action Key -Keys ('^a'+$text) -EvidenceFolder $folder }
function Capture($name) { & $driver -Action Capture -Name $name -EvidenceFolder $folder }
function Report($kind) {
    Send @{action='groupReport';kind=$kind}
    Get-Content (Join-Path $evidence 'group-report.json') -Raw | ConvertFrom-Json
}
function CheckSelection($report) {
    if ($report.selectedTitle -ne $report.selectorTitle -or $report.selectedTitle -ne $report.resultTitle) { throw 'El selector y el resultado muestran soluciones distintas.' }
}
if (-not $SkipStart) {
    & $driver -Action Start -SkipBuild -IsolatedIdentity -EvidenceFolder $folder
    Start-Sleep -Seconds 2
}
Send @{action='dark'}
foreach ($kind in @('Curva','AQS')) {
    Send @{action='size';width=1920;height=1080}
    Send @{action='group';kind=$kind}
    Send @{action='groupEditor';kind=$kind}
    Start-Sleep -Milliseconds 500
    Send @{action='groupScenario';kind=$kind;code=($kind+'-AFORO-COMUN');volumes=@(2,3,4)}
    Click 'Aforo común'; EnterValue '25'
    $shared = Report $kind
    if (@($shared.rows | Where-Object { $_.FinalVolume -ne '25' -or $_.UseCustomFinalVolume }).Count -gt 0) { throw 'El aforo común no llegó a todas las soluciones.' }
    CheckSelection $shared
    Capture ($kind+'-comun-1920')
    Click 'Usar aforo diferente'
    Click 'Volumen de aforo'; EnterValue '50'
    Click 'Aforo común'; EnterValue '40'
    $custom = Report $kind
    if ($custom.rows[0].FinalVolume -ne '50' -or -not $custom.rows[0].UseCustomFinalVolume -or $custom.rows[1].FinalVolume -ne '40') { throw 'Se modificó la excepción al cambiar el aforo común.' }
    Capture ($kind+'-excepcion-1920')
    Click 'Usar aforo diferente'
    $rejoined = Report $kind
    if ($rejoined.rows[0].FinalVolume -ne '40' -or $rejoined.rows[0].UseCustomFinalVolume) { throw 'La solución no volvió al aforo común.' }
    Send @{action='groupEdit';kind=$kind;index=2;finalVolume='50'}
    Send @{action='groupSelect';kind=$kind;index=2}
    Send @{action='groupRemove';kind=$kind;index=0}
    $renumbered = Report $kind
    CheckSelection $renumbered
    if ($renumbered.selectedTitle -notlike 'Solución 02*') { throw 'La solución seleccionada no se renumeró.' }
    Send @{action='groupCommonAforo';kind=$kind;volume='1'}
    $invalid = Report $kind
    if ($invalid.CanSave -or $invalid.rows[1].FinalVolume -ne '50') { throw 'No se detectó el exceso o se alteró la excepción.' }
    Send @{action='groupCommonAforo';kind=$kind;volume='25'}
    foreach ($size in @(@(1366,768),@(960,768))) {
        Send @{action='size';width=$size[0];height=$size[1]}
        Send @{action='groupScroll';kind=$kind;offset=0}
        $layout = Report $kind
        if ($layout.saveBounds.y+$layout.saveBounds.height -gt $layout.height+2) { throw 'Guardar quedó fuera de la ventana.' }
        Capture ($kind+'-comun-'+$size[0])
    }
    Send @{action='size';width=1366;height=768}
    Send @{action='groupScroll';kind=$kind;offset=185}
    Click 'Solución a visualizar'
    Click 'Solución 01 · Intermedia'
    $selected = Report $kind
    CheckSelection $selected
    if ($selected.selectedTitle -notlike 'Solución 01*') { throw 'El selector no cambió el resultado.' }
    Capture ($kind+'-resultado-1366')
    Click ('Guardar '+$kind)
    $saved = Report $kind
    if ($saved.Created.Calculation.Parts.Count -ne 2 -or $saved.Created.Calculation.Parts[0].FinalVolume -ne 25 -or $saved.Created.Calculation.Parts[1].FinalVolume -ne 50) { throw 'No se conservaron los aforos efectivos al guardar.' }
    Copy-Item -LiteralPath (Join-Path $evidence 'group-report.json') -Destination (Join-Path $evidence ($kind+'-guardado.json'))
    Send @{action='groupClose';kind=$kind}
    Start-Sleep -Milliseconds 350
}
@{fixtureOnly=$true;productionDatabaseModified=$false;commonAforo=$true;individualException=$true;returnToCommon=$true;resultSelectionAndRenumbering=$true;savedAforos=@(25,50);sizes=@('1920x1080','1366x768','960x768')} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'verification.json') -Encoding utf8
