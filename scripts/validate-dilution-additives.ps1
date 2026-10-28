param([ValidateSet('Start','Run','Intermediate')][string]$Action='Run')
$ErrorActionPreference='Stop'
$driver=Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$folder='dilution-additives'
$evidence=Join-Path $PSScriptRoot '..\artifacts\validation\dilution-additives'
function Send($data) { & $driver -Action Command -Command ($data | ConvertTo-Json -Compress) -EvidenceFolder $folder }
function Capture($name) { Start-Sleep -Milliseconds 250; & $driver -Action Capture -Name $name -EvidenceFolder $folder }
function Report($kind,$name) {
    Send @{action='groupReport';kind=$kind}
    $report=Get-Content (Join-Path $evidence 'group-report.json') -Raw | ConvertFrom-Json
    Copy-Item -LiteralPath (Join-Path $evidence 'group-report.json') -Destination (Join-Path $evidence ($name+'.json'))
    return $report
}
if($Action -eq 'Start') { & $driver -Action Start -SkipBuild -IsolatedIdentity -EvidenceFolder $folder; return }
if($Action -eq 'Intermediate') {
    Send @{action='intermediate'}; Send @{action='intEditor'}
    Start-Sleep -Milliseconds 700
    Send @{action='intScenario';volumes=@(10)}
    Send @{action='intPopup';field='SolventSelect'}
    Capture 'Intermedia-solvent-catalog'
    & $driver -Action Click -Name 'Metanol (MeOH)' -EvidenceFolder $folder
    Capture 'Intermedia-solvent-selected'
    Send @{action='intClose'}
    return
}
Send @{action='dark'}
foreach($kind in @('Curva','AQS')) {
    Send @{action='group';kind=$kind}; Send @{action='groupEditor';kind=$kind}
    Send @{action='groupScenario';kind=$kind;code=($kind+'-ADITIVOS');volumes=@(95);standardVolume=10;solvent=4}
    $invalid=Report $kind ($kind+'-combined-overflow')
    if($invalid.CanSave) { throw 'Se permitió superar el aforo combinado.' }
    Send @{action='groupScenario';kind=$kind;code=($kind+'-ADITIVOS');volumes=@(30);standardVolume=20;solvent=4}
    foreach($size in @(@(1920,1080),@(1366,768))) {
        Send @{action='size';width=$size[0];height=$size[1]}
        Send @{action='groupScroll';kind=$kind;offset=185}
        $r=Report $kind ($kind+'-additives-'+$size[0])
        if(-not $r.CanSave -or $r.rows[0].Preview.Solvent.Id -ne 4 -or $r.rows[0].Preview.InternalStandard.VolumeTaken -ne 20) { throw 'No se calculó solvente y estándar interno.' }
        if($r.saveBounds.y+$r.saveBounds.height -gt $r.height+2) { throw 'Guardar quedó fuera de la ventana.' }
        Capture ($kind+'-additives-'+$size[0])
    }
    Send @{action='groupSave';kind=$kind}
    $saved=Report $kind ($kind+'-saved')
    if($null -eq $saved.Created.Calculation.Parts[0].InternalStandard) { throw 'El estándar interno no se conservó al guardar.' }
    Send @{action='groupClose';kind=$kind}
    Start-Sleep -Milliseconds 700
    Send @{action='groupDetail';kind=$kind}
    Start-Sleep -Milliseconds 700
    Capture ($kind+'-saved-detail')
    & $driver -Action Invoke -Name 'Cerrar' -EvidenceFolder $folder
}
@{fixtureOnly=$true;productionDatabaseModified=$false;solvent='MeOH';internalStandard='Intermedia';sizes=@('1920x1080','1366x768');combinedOverflowRejected=$true;savedDetailVerified=$true} | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $evidence 'verification.json')
