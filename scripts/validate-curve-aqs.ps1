param([ValidateSet('Start','Run','Responsive','Finish','Exit')][string]$Action='Run', [string]$EvidenceFolder='curve-aqs')
$ErrorActionPreference='Stop'
$driver=Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$evidence=Join-Path $PSScriptRoot ('..\artifacts\validation\'+$EvidenceFolder)
function Send($data) { & $driver -Action Command -Command ($data | ConvertTo-Json -Compress) -EvidenceFolder $EvidenceFolder }
function Capture($name) { Start-Sleep -Milliseconds 250; & $driver -Action Capture -Name $name -EvidenceFolder $EvidenceFolder }
function Report($kind,$name) {
    Send @{action='groupReport';kind=$kind}
    $report=Get-Content -LiteralPath (Join-Path $evidence 'group-report.json') -Raw | ConvertFrom-Json
    Copy-Item -LiteralPath (Join-Path $evidence 'group-report.json') -Destination (Join-Path $evidence ($name+'.json'))
    if($report.theme -ne 'Dark') { throw 'El grupo no usa el tema oscuro.' }
    if($null -ne $report.saveBounds -and $report.saveBounds.visible -and ($report.saveBounds.y -lt 0 -or $report.saveBounds.y+$report.saveBounds.height -gt $report.height+2)) { throw 'Guardar queda fuera de la ventana.' }
    return $report
}
switch($Action) {
 'Start' {
    & $driver -Action Start -SkipBuild -IsolatedIdentity -EvidenceFolder $EvidenceFolder
    $deadline=[DateTime]::UtcNow.AddSeconds(40)
    while(-not (Test-Path -LiteralPath (Join-Path $evidence 'window.json'))) {
        if([DateTime]::UtcNow -gt $deadline) { throw 'No inicio WinUI.' }; Start-Sleep -Milliseconds 100
    }
 }
 'Run' {
    Send @{action='dark'}; Send @{action='size';width=1920;height=1080}
    foreach($kind in @('Curva','AQS')) {
      Send @{action='group';kind=$kind}; Capture ($kind+'-empty')
      Send @{action='groupEditor';kind=$kind}; Start-Sleep -Milliseconds 400
      Capture ($kind+'-new-empty')
      Send @{action='groupScenario';kind=$kind;code=($kind+'-VISUAL-001');volumes=@(60,60)}
      $r=Report $kind ($kind+'-invalid'); if($r.CanSave) { throw 'El consumo acumulado excesivo no se rechazo.' }; Capture ($kind+'-invalid')
      Send @{action='groupScenario';kind=$kind;code=($kind+'-VISUAL-001');volumes=@(2,3,5);internalIndex=0}
      $r=Report $kind ($kind+'-ready'); if(-not $r.CanSave) { throw ('No se habilita guardar: '+$r.Validation) }
      Capture ($kind+'-components-1920')
      Send @{action='groupScroll';kind=$kind;offset=9999}; Capture ($kind+'-review-1920')
      Send @{action='groupClose';kind=$kind}
    }
 }
 'Responsive' {
    foreach($kind in @('Curva','AQS')) {
      Send @{action='group';kind=$kind}; Send @{action='groupEditor';kind=$kind}; Start-Sleep -Milliseconds 350
      Send @{action='groupScenario';kind=$kind;code=($kind+'-RESPONSIVE');volumes=@(2,3,5);internalIndex=0}
      foreach($size in @(@(1920,1080),@(1600,900),@(1366,768))) {
        Send @{action='size';width=$size[0];height=$size[1]}
        Send @{action='groupScroll';kind=$kind;offset=0}; Capture ($kind+'-form-'+$size[0]+'x'+$size[1])
        $null=Report $kind ($kind+'-form-'+$size[0]+'x'+$size[1])
        Send @{action='groupScroll';kind=$kind;offset=250}; Capture ($kind+'-components-'+$size[0]+'x'+$size[1])
        Send @{action='groupScroll';kind=$kind;offset=9999}; Capture ($kind+'-results-'+$size[0]+'x'+$size[1])
      }
      Send @{action='groupClose';kind=$kind}
    }
 }
 'Finish' {
    Send @{action='size';width=1366;height=768}
    foreach($kind in @('AQS','Curva')) {
      Send @{action='group';kind=$kind}; Send @{action='groupEditor';kind=$kind}; Start-Sleep -Milliseconds 350
      $taken = if($kind -eq 'Curva') { @(3,5,7) } else { @(2,3,5) }
      Send @{action='groupScenario';kind=$kind;code=($kind+'-SAVED');volumes=$taken;internalIndex=0}
      Send @{action='groupSave';kind=$kind}; Capture ($kind+'-saved')
      $r=Report $kind ($kind+'-saved'); if($null -eq $r.Created -or $r.Created.Calculation.Parts.Count -ne 3 -or $r.Created.Consumptions.Count -ne 1) { throw 'El grupo no se guardo completo.' }
      Send @{action='groupClose';kind=$kind}; Capture ($kind+'-list')
      Send @{action='groupDetail';kind=$kind}; Capture ($kind+'-detail')
      & $driver -Action Invoke -Name 'Cerrar' -EvidenceFolder $EvidenceFolder
    }
    Send @{action='intermediate'}; Capture 'Intermediate-shared-75mL'
    $r=Report 'Curva' 'shared-balance'; if($r.sources[0].AvailableVolume -ne 75) { throw 'Saldo fixture compartido incorrecto.' }
    @{fixtureOnly=$true;interDbModified=$false;passed=$true;scale=$r.scale;limitations='Native WinUI views with fixtures; PostgreSQL persistence is validated separately. All captures use the current display DPI.'} | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $evidence 'verification.json')
 }
 'Exit' { Send @{action='exit'} }
}
