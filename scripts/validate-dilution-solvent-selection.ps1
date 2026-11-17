param([switch]$SkipStart)
$ErrorActionPreference = 'Stop'
$driver = Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$folder = 'solvent-selection'
$evidence = Join-Path $PSScriptRoot '..\artifacts\validation\solvent-selection'
function Send($data) { & $driver -Action Command -Command ($data | ConvertTo-Json -Compress) -EvidenceFolder $folder }
function Capture($name) { & $driver -Action Capture -Name $name -EvidenceFolder $folder }
function Click($name) { & $driver -Action Click -Name $name -EvidenceFolder $folder }
function GroupReport($kind) {
    Send @{action='groupReport';kind=$kind}
    Get-Content (Join-Path $evidence 'group-report.json') -Raw | ConvertFrom-Json
}
if (-not $SkipStart) {
    & $driver -Action Start -SkipBuild -IsolatedIdentity -EvidenceFolder $folder
    Start-Sleep -Seconds 2
}
Send @{action='dark'}
foreach ($kind in @('Curva', 'AQS')) {
    Send @{action='size';width=1920;height=1080}
    Send @{action='group';kind=$kind}
    Send @{action='groupEditor';kind=$kind}
    Start-Sleep -Milliseconds 500
    Send @{action='groupScenario';kind=$kind;code=($kind+'-SOLVENTE');volumes=@(10)}
    Click 'Solvente de aforo'
    Capture ($kind+'-catalogo')
    Click 'Metanol (MeOH)'
    $selected = GroupReport $kind
    if ($selected.rows[0].Solvent.Id -ne 4 -or -not $selected.rows[0].CanSelectSolvent) { throw "$kind no conserva la selección real del solvente." }
    Click 'Actualizar orígenes'
    $refreshed = GroupReport $kind
    if ($refreshed.rows[0].Solvent.Id -ne 4) { throw "$kind perdió el solvente al actualizar orígenes." }
    foreach ($size in @(@(1920,1080), @(1366,768))) {
        Send @{action='size';width=$size[0];height=$size[1]}
        Send @{action='groupScroll';kind=$kind;offset=185}
        Capture ($kind+'-seleccion-'+$size[0])
    }
    Click ('Guardar '+$kind)
    $saved = GroupReport $kind
    if ($saved.Created.Calculation.Parts[0].Solvent.Id -ne 4) { throw "$kind no conservó el solvente al guardar en el fixture." }
    Copy-Item -LiteralPath (Join-Path $evidence 'group-report.json') -Destination (Join-Path $evidence ($kind+'-guardado.json'))
    Send @{action='groupClose';kind=$kind}
    Start-Sleep -Milliseconds 350
}
Send @{action='size';width=1366;height=768}
Send @{action='intermediate'}
Send @{action='intEditor'}
Start-Sleep -Milliseconds 500
Send @{action='intScenario';volumes=@(10)}
Click 'Solvente de la Intermedia'
Capture 'Intermedia-catalogo'
Click 'Metanol (MeOH)'
Send @{action='intReport'}
$intermediate = Get-Content (Join-Path $evidence 'intermediate-report.json') -Raw | ConvertFrom-Json
if ($intermediate.Solvent.Id -ne 4 -or -not $intermediate.CanSelectSolvent) { throw 'Intermedia no conserva la selección real del solvente.' }
Capture 'Intermedia-seleccion'
Click 'Crear Intermedia'
Click 'Confirmar'
Send @{action='intReport'}
$savedIntermediate = Get-Content (Join-Path $evidence 'intermediate-report.json') -Raw | ConvertFrom-Json
if ($savedIntermediate.Created.Solvent.Id -ne 4) { throw 'Intermedia no conservó el solvente al guardar en el fixture.' }
Send @{action='intClose'}
@{fixtureOnly=$true;productionDatabaseModified=$false;pointerSelectionVerified=@('Intermedia','Curva','AQS');savedSolventId=4;groupRefreshPreservesSelection=$true;sizes=@('1920x1080','1366x768')} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'verification.json') -Encoding utf8
