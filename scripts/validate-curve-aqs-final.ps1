param([ValidateSet('Start','Forms','Responsive','Details','Exit')][string]$Action='Forms',[string]$EvidenceFolder='curve-aqs-final')
$ErrorActionPreference='Stop'
$driver=Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$evidence=Join-Path $PSScriptRoot ('..\artifacts\validation\'+$EvidenceFolder)
function Send($data) { & $driver -Action Command -Command ($data | ConvertTo-Json -Compress -Depth 8) -EvidenceFolder $EvidenceFolder }
function Capture($name) { Start-Sleep -Milliseconds 300; & $driver -Action Capture -Name $name -EvidenceFolder $EvidenceFolder }
function Report($kind,$name) {
 Send @{action='groupReport';kind=$kind}
 $report=Get-Content -LiteralPath (Join-Path $evidence 'group-report.json') -Raw | ConvertFrom-Json
 Copy-Item -LiteralPath (Join-Path $evidence 'group-report.json') -Destination (Join-Path $evidence ($name+'.json'))
 if($null -ne $report.saveBounds -and $report.saveBounds.visible -and ($report.saveBounds.y -lt 0 -or $report.saveBounds.y+$report.saveBounds.height -gt $report.height+2)) { throw 'Guardar fuera de la ventana.' }
 return $report
}
switch($Action) {
 'Start' { & $driver -Action Start -SkipBuild -IsolatedIdentity -EvidenceFolder $EvidenceFolder }
 'Forms' {
  Send @{action='dark'}; Send @{action='size';width=1920;height=1080}
  foreach($kind in @('Curva','AQS')) {
   Send @{action='group';kind=$kind}; Send @{action='groupEditor';kind=$kind}; Start-Sleep -Milliseconds 350
   $r=Report $kind ($kind+'-empty'); if($r.VisibleValidation -or $r.Message) { throw 'Errores al abrir.' }; Capture ($kind+'-empty')
   Send @{action='groupScenario';kind=$kind;code=($kind+'-VALIDACION');volumes=@(2,3,5);selectedIndex=0}
   $r=Report $kind ($kind+'-three'); if(-not $r.CanSave -or $r.Calculation.Debits.Count -ne 1 -or $r.Calculation.Debits[0].Quantity -ne 10 -or $r.Calculation.Debits[0].RemainingVolume -ne 90) { throw 'Consumo compartido incorrecto.' }
   Capture ($kind+'-form-1920x1080'); Send @{action='groupScroll';kind=$kind;offset=145}; Capture ($kind+'-three-solutions')
   Send @{action='groupScenario';kind=$kind;code=($kind+'-INTERNO');volumes=@(2,3,5);sourceIndices=@(1,0,0);internalIndex=0;selectedIndex=0}
   Send @{action='groupScroll';kind=$kind;offset=145}; Capture ($kind+'-internal-standard'); $null=Report $kind ($kind+'-internal-standard')
   Send @{action='groupScenario';kind=$kind;code=($kind+'-AFORO');volumes=@(110);selectedIndex=0}
   Send @{action='groupScroll';kind=$kind;offset=145}; $r=Report $kind ($kind+'-excess'); if($r.CanSave -or $r.rows[0].VolumePreview.Percent -ne 110 -or -not $r.rows[0].FieldError) { throw 'Aforo excedido sin validacion.' }; Capture ($kind+'-excess')
   Send @{action='groupEdit';kind=$kind;index=0;volume='25'}; $r=Report $kind ($kind+'-live-25'); if(-not $r.CanSave -or $r.rows[0].VolumePreview.Remaining -ne 75) { throw 'No se actualizo el recipiente.' }; Capture ($kind+'-live-25')
   Send @{action='groupScene';kind=$kind;scene='CreateError'}; Send @{action='groupSave';kind=$kind}; $r=Report $kind ($kind+'-server-error'); if(-not $r.Message -or $r.Created -or $r.rows[0].VolumePreview.Taken -ne 25) { throw 'No se conserva borrador ante error.' }; Capture ($kind+'-server-error')
   Send @{action='groupScene';kind=$kind;scene='Default'}; Send @{action='groupClose';kind=$kind}
  }
 }
 'Responsive' {
  foreach($kind in @('Curva','AQS')) {
   Send @{action='group';kind=$kind}; Send @{action='groupEditor';kind=$kind}; Start-Sleep -Milliseconds 300
   Send @{action='groupScenario';kind=$kind;code=($kind+'-RESPONSIVE');volumes=@(2,3,5);selectedIndex=0}
   foreach($size in @(@(1920,1080),@(1600,900),@(1366,768),@(1000,768))) {
    Send @{action='size';width=$size[0];height=$size[1]}; Send @{action='groupScroll';kind=$kind;offset=0}
    $r=Report $kind ($kind+'-form-'+$size[0]+'x'+$size[1]); if($r.scale -ne 1.25) { throw 'No se verifico escala real 125%.' }
    Capture ($kind+'-form-'+$size[0]+'x'+$size[1])
    Send @{action='groupScroll';kind=$kind;offset=145}; Capture ($kind+'-components-'+$size[0]+'x'+$size[1])
    Send @{action='groupScroll';kind=$kind;offset=9999}; Capture ($kind+'-results-'+$size[0]+'x'+$size[1])
   }
   Send @{action='groupClose';kind=$kind}
  }
 }
 'Details' {
  Send @{action='size';width=1920;height=1080}
  foreach($kind in @('AQS','Curva')) {
   Send @{action='group';kind=$kind}; Send @{action='groupEditor';kind=$kind}; Start-Sleep -Milliseconds 300
   Send @{action='groupScenario';kind=$kind;code=($kind+'-FICHA');volumes=@(2,3,5);sourceIndices=@(1,0,0);internalIndex=0;selectedIndex=0}
   Send @{action='groupSave';kind=$kind}; $r=Report $kind ($kind+'-saved'); if($null -eq $r.Created -or $r.Created.Calculation.Parts.Count -ne 3 -or $r.Created.Consumptions.Count -ne 2) { throw 'No se guardo el grupo fixture.' }; Capture ($kind+'-saved')
   Send @{action='groupClose';kind=$kind}; Start-Sleep -Milliseconds 350; Send @{action='groupDetail';kind=$kind}; Capture ($kind+'-detail')
   Send @{action='groupDetailSelect';kind=$kind;index=2}; Capture ($kind+'-detail-solution03')
   Send @{action='size';width=1366;height=768}; Capture ($kind+'-detail-1366x768')
   Send @{action='groupDetailScroll';kind=$kind;offset=9999}; Capture ($kind+'-detail-bottom')
   & $driver -Action Invoke -Name 'Cerrar' -EvidenceFolder $EvidenceFolder
   Send @{action='size';width=1920;height=1080}
  }
  @{fixtureOnly=$true;interDbModified=$false;scale=1.25;passed=$true;limitations='Native WinUI real presentation with isolated in-memory fixtures. No authenticated production write.'} | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $evidence 'verification.json')
 }
 'Exit' { Send @{action='exit'} }
}
