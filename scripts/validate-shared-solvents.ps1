param([switch]$SkipBuild)
$ErrorActionPreference='Stop'
Set-ExecutionPolicy -Scope Process Bypass
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$evidence=Join-Path $workspace 'artifacts/validation/shared-solvents'
$driver=Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$folder='shared-solvents'
$config=Join-Path $evidence 'ui-database.json'
if(-not(Test-Path -LiteralPath $config)){throw 'Run test-commercial-solvents.ps1 first; a real isolated PostgreSQL schema is required.'}
$env:LIMS_SOLVENT_TEST_CONFIG=$config
function Send-Solvent($data){& $driver -Action Command -EvidenceFolder $folder -Command ($data | ConvertTo-Json -Compress -Depth 20); Start-Sleep -Milliseconds 200}
function Assert-Solvent($value,$message){if(-not $value){throw $message}}
function Report-Solvent($kind,$name){
    Send-Solvent @{action='solventReport';kind=$kind;name=$name}
    return (Get-Content (Join-Path $evidence ($name+'.json')) -Raw -Encoding utf8 | ConvertFrom-Json)
}
function Assert-PersistedComposition($certificate,$expected){
    $rows=@($certificate.Solvents); $details=@($certificate.SolventDetails); $expected=@($expected)
    Assert-Solvent ($rows.Count -eq $expected.Count -and $details.Count -eq $expected.Count) 'Saved row count changed.'
    for($i=0;$i -lt $expected.Count;$i++){
        Assert-Solvent ([decimal]$rows[$i].Percent -eq [decimal]$expected[$i].percent) 'Persisted percentage changed.'
        Assert-Solvent ($details[$i].Abbreviation -eq $expected[$i].abbreviation -and $rows[$i].SolventId -eq $details[$i].SolventId -and $details[$i].Position -eq $i) 'Solvent identity or row order changed.'
    }
}
function Capture-Solvent($name){& $driver -Action Capture -EvidenceFolder $folder -Name $name}
function Set-Solvent($kind,$rows){Send-Solvent @{action='solventSet';kind=$kind;rows=$rows}}
function Close-SolventDetails(){Send-Solvent @{action='solventCloseDetail'}}
& $driver -Action Start -EvidenceFolder $folder -IsolatedIdentity -SkipBuild:$SkipBuild
$deadline=[DateTime]::UtcNow.AddSeconds(25)
while(-not(Test-Path (Join-Path $evidence 'window.json'))){if([DateTime]::UtcNow -gt $deadline){throw 'WinUI did not start.'};Start-Sleep -Milliseconds 200}
Start-Sleep -Milliseconds 1000
Send-Solvent @{action='preparations'}
Send-Solvent @{action='size';width=1366;height=768}
Send-Solvent @{action='stockEditor'}
Send-Solvent @{action='solventReady';kind='StockPrepared'}
$ternary=@(@{abbreviation='MeOH';percent='30'},@{abbreviation='H2O';percent='20'},@{abbreviation='ACN';percent='50'})
Set-Solvent 'StockPrepared' $ternary
$r=Report-Solvent 'StockPrepared' 'prepared-shared-catalog'
Assert-Solvent ($r.catalog.Count -eq 12 -and $r.uiCatalog.Count -eq 12 -and $r.IsValid) 'Prepared Stock did not load the persistent shared catalog.'
Capture-Solvent 'stock-preparado-mezcla-compartida'
Send-Solvent @{action='stockClose'}
$scenarios=@(
    @{name='100';rows=@(@{abbreviation='MeOH';percent='100'})},
    @{name='50-50';rows=@(@{abbreviation='MeOH';percent='50'},@{abbreviation='ACN';percent='50'})},
    @{name='30-20-50';rows=$ternary}
)
$registrations=0
foreach($scenario in $scenarios){
    Send-Solvent @{action='stockEditor'}
    Send-Solvent @{action='entryCommercial';kind='Stock'}
    Send-Solvent @{action='solventReady';kind='Stock'}
    $certificate=@{Reference='';Matrix='';Components=@(@{Id=[Guid]::NewGuid().ToString();Name='Analito de prueba';CertifiedConcentration=1000;ConcentrationUnit='µg/mL'})}
    Send-Solvent @{action='entryFill';kind='Stock';fields=@{ProductNameBox=('Stock comercial · PRUEBA '+$scenario.name);BrandBox='Fabricante de prueba';LotBox=('LOTE-'+$scenario.name);VolumeBox='10';BottleCountBox='1';StorageBox='Ambiente'};certificate=$certificate}
    Set-Solvent 'Stock' $scenario.rows
    if($registrations -eq 0){
        Send-Solvent @{action='solventPopup';kind='Stock'}
        $r=Report-Solvent 'Stock' 'selector-loaded'
        Assert-Solvent ($r.popupOptions.Count -eq 12 -and $r.catalog.Count -eq 12) 'Unfiltered popup did not contain all twelve persistent solvents.'
        Capture-Solvent 'solvent-selector-cargado'
        & $driver -Action Key -EvidenceFolder $folder -Keys 'THF'
        $thf=Report-Solvent 'Stock' 'search-thf'
        Assert-Solvent ($thf.popupOptions.Count -eq 1 -and $thf.popupOptions[0].Abbreviation -eq 'THF') 'THF search did not return the correct solvent.'
        Capture-Solvent 'solvent-busqueda-thf'
        & $driver -Action Key -EvidenceFolder $folder -Keys '^a'
        & $driver -Action Key -EvidenceFolder $folder -Keys 'Tetrahidrofurano'
        $full=Report-Solvent 'Stock' 'search-tetrahidrofurano'
        Assert-Solvent ($full.popupOptions.Count -eq 1 -and $full.popupOptions[0].Id -eq $thf.popupOptions[0].Id) 'Full-name and abbreviation searches returned different solvents.'
        Capture-Solvent 'solvent-busqueda-nombre'
        & $driver -Action Key -EvidenceFolder $folder -Keys '{ESC}'
        Set-Solvent 'Stock' @(@{abbreviation='MeOH';percent='90'})
        $r=Report-Solvent 'Stock' 'invalid-total'
        Assert-Solvent (-not $r.IsValid -and -not $r.saveEnabled -and $r.Error.Length -gt 0) 'Incomplete proportions allowed saving.'
        Capture-Solvent 'solvent-total-invalido'
        Set-Solvent 'Stock' @(@{abbreviation='MeOH';percent='50'},@{abbreviation='MeOH';percent='50'})
        $r=Report-Solvent 'Stock' 'duplicate-solvent'
        Assert-Solvent (-not $r.IsValid -and -not $r.saveEnabled -and $r.Error -match 'Unifique') 'Duplicate solvent did not ask to unify proportions.'
        Capture-Solvent 'solvent-duplicado'
        Set-Solvent 'Stock' $scenario.rows
    }
    $r=Report-Solvent 'Stock' ('commercial-'+$scenario.name)
    Assert-Solvent ($r.IsValid -and $r.saveEnabled -and $r.nominalPanelRemoved -and $r.responsibleIsReadOnly) 'Commercial form or cleanup validation failed.'
    Capture-Solvent ('stock-comercial-mezcla-'+$scenario.name)
    if($scenario.name -eq '30-20-50'){Capture-Solvent 'solvent-mezcla-30-20-50'}
    & $driver -Action Invoke -EvidenceFolder $folder -Name 'Guardar Stock'
    $registrations++
    Send-Solvent @{action='solventSaved';kind='Stock';name=('saved-stock-'+$scenario.name);expected=$registrations}
    Send-Solvent @{action='solventReload';kind='Stock';name=('reloaded-stock-'+$scenario.name);expected=$registrations}
    $saved=Get-Content (Join-Path $evidence ('reloaded-stock-'+$scenario.name+'.json')) -Raw -Encoding utf8 | ConvertFrom-Json
    Assert-PersistedComposition $saved.reloaded.Material.Commercial $scenario.rows
    Assert-Solvent ($saved.reloaded.Material.Commercial.CompositionBasis -eq 'CompositionPercent' -and -not $saved.reloaded.Material.Commercial.UseNominalConcentration) 'Wrong composition basis or calculation source.'
    Capture-Solvent ('stock-comercial-ficha-recargada-'+$scenario.name)
    Close-SolventDetails
}
Send-Solvent @{action='mixOpen'}
Send-Solvent @{action='solventReady';kind='Intermedia'}
Send-Solvent @{action='mixFill'}
Send-Solvent @{action='mixPaste';text="Analito A`t`t50`tµg/mL`nAnalito B`t`t49.9`tµg/mL"}
Set-Solvent 'Intermedia' $ternary
$r=Report-Solvent 'Intermedia' 'intermedia-shared-catalog'
Assert-Solvent ($r.IsValid -and $r.catalog.Count -eq 12 -and $r.uiCatalog.Count -eq 12 -and $r.saveEnabled) 'Intermedia did not reuse the persistent catalog and composition editor.'
Capture-Solvent 'intermedia-comercial-mezcla-30-20-50'
& $driver -Action Invoke -EvidenceFolder $folder -Name 'Guardar Intermedia'
Send-Solvent @{action='solventSaved';kind='Intermedia';name='saved-intermedia';expected=1}
Send-Solvent @{action='solventReload';kind='Intermedia';name='reloaded-intermedia';expected=1}
$saved=Get-Content (Join-Path $evidence 'reloaded-intermedia.json') -Raw -Encoding utf8 | ConvertFrom-Json
Assert-PersistedComposition $saved.reloaded.Material.Commercial $ternary
Capture-Solvent 'intermedia-comercial-ficha-recargada'
Close-SolventDetails
@{passed=$true;persistentIsolatedDatabase=$true;productionDatabaseModified=$false;resolution='1366x768';checks=@('persistent twelve-solvent catalog in all three editors','unfiltered popup twelve rows','THF and Tetrahidrofurano native search','100 and 50/50 and 30/20/50','invalid total and duplicate rejection','real commercial save and fresh PostgreSQL reload','certified concentration only in new forms','optional catalog number and automatic actor')} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $evidence 'visual-validation.json') -Encoding utf8
Send-Solvent @{action='exit'}
Write-Output $evidence
