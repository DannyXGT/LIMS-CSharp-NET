param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
Set-ExecutionPolicy -Scope Process Bypass
$driver = Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$folder = 'preparation-dialogs'
$evidence = Join-Path $PSScriptRoot '../artifacts/validation/preparation-dialogs'
function Send-Form($data) {
    & $driver -Action Command -EvidenceFolder $folder -Command ($data | ConvertTo-Json -Depth 20 -Compress)
    Start-Sleep -Milliseconds 160
}
function Assert-Form($condition, $message) { if (-not $condition) { throw $message } }
function Capture-Form($name, $width=1366, $height=768) {
    Send-Form @{action='size';width=$width;height=$height}
    Send-Form @{action='permissionActivate'}
    Start-Sleep -Milliseconds 250
    & $driver -Action Capture -EvidenceFolder $folder -Name $name
}
function Report-Form($name) {
    Send-Form @{action='formReport';name=$name}
    $r = Get-Content (Join-Path $evidence ($name+'.json')) -Raw -Encoding utf8 | ConvertFrom-Json
    Assert-Form (($r.footer.y+$r.footer.height) -le ($r.Size.Height+1)) ('Footer outside viewport: '+$name)
    return $r
}
function Report-Mix($name) {
    Send-Form @{action='mixReport';name=$name}
    $r = Get-Content (Join-Path $evidence ($name+'.json')) -Raw -Encoding utf8 | ConvertFrom-Json
    Assert-Form (($r.footer.y+$r.footer.height) -le ($r.Size.Height+1)) ('Intermedia footer outside viewport: '+$name)
    return $r
}
& $driver -Action Start -EvidenceFolder $folder -IsolatedIdentity -SkipBuild:$SkipBuild
$deadline = [DateTime]::UtcNow.AddSeconds(25)
while (-not (Test-Path (Join-Path $evidence 'window.json'))) {
    if ([DateTime]::UtcNow -gt $deadline) { throw 'WinUI did not start.' }
    Start-Sleep -Milliseconds 200
}
Start-Sleep -Milliseconds 900
Send-Form @{action='preparations'}
Send-Form @{action='stockEditor'}
foreach ($w in @(1366,1920)) {
    Capture-Form ('stock-preparado-vacio-'+$w) $w $(if ($w -eq 1366) {768} else {1080})
    $r=Report-Form ('stock-empty-'+$w)
    Assert-Form (-not $r.saveEnabled -and $null -eq $r.Calculation) 'Empty Stock unexpectedly calculated or enabled save.'
}
Send-Form @{action='stockSelect';index=0}
foreach ($field in @(@{field='ConcentrationBox';text='100'},@{field='FinalQuantityBox';text='100'},@{field='ActualQuantityBox';text='10.1'},@{field='StorageTemperatureBox';text='Ambiente'})) {
    $field.action='stockText'; Send-Form $field
}
Send-Form @{action='stockDate';field='ExpirationDatePicker';text='2027-06-24T00:00:00-06:00'}
Send-Form @{action='stockMixture';rows=@(@{id=4;percent='50'},@{id=8;percent='50'})}
foreach ($w in @(1366,1920)) {
    Send-Form @{action='formScroll';offset=0}
    Capture-Form ('stock-preparado-datos-'+$w) $w $(if ($w -eq 1366) {768} else {1080})
    $r=Report-Form ('stock-filled-'+$w)
    Assert-Form ($r.saveEnabled -and $r.Calculation.ActualWeight -eq 10.1 -and $r.Compound.Length -gt 0) 'Stock lost real calculation, compound derivation or validation.'
    Send-Form @{action='formScroll';offset=9999}
    Capture-Form ('stock-preparado-solvente-vigencia-'+$w) $w $(if ($w -eq 1366) {768} else {1080})
    $r=Report-Form ('stock-scroll-'+$w)
    Assert-Form ($r.VerticalOffset -gt 0) 'Stock form did not scroll.'
}
Send-Form @{action='stockMixture';rows=@(@{id=4;percent='40'},@{id=8;percent='50'})}
Capture-Form 'stock-preparado-validacion-1366'
$r=Report-Form 'stock-composition-error'
Assert-Form (-not $r.saveEnabled) 'Invalid solvent total allowed saving.'
Send-Form @{action='stockMixture';rows=@(@{id=4;percent='50'},@{id=8;percent='50'})}
Send-Form @{action='formScroll';offset=0}
Send-Form @{action='formPopup';field='SourceList'}
& $driver -Action Key -EvidenceFolder $folder -Keys '4-Amino'
& $driver -Action Capture -EvidenceFolder $folder -Name stock-preparado-busqueda
& $driver -Action Key -EvidenceFolder $folder -Keys '{ESC}'
Send-Form @{action='stockText';field='ActualQuantityBox';text='10.1'}
Send-Form @{action='stockSave'}
Send-Form @{action='stockReport'}
$stock=Get-Content (Join-Path $evidence 'interaction.json') -Raw -Encoding utf8 | ConvertFrom-Json
Assert-Form ($stock.CreateCalls -eq 1 -and $null -ne $stock.Created -and $stock.AvailableQuantity -eq 239.9) 'Fixture Stock save did not retain consumption.'
Send-Form @{action='stockClose'}
Write-Output 'Stock preparado: capturas y validacion completadas.'
Send-Form @{action='stockEditor'}
Send-Form @{action='entryCommercial';kind='Stock'}
foreach ($w in @(1366,1920)) {
    Capture-Form ('stock-comercial-vacio-'+$w) $w $(if ($w -eq 1366) {768} else {1080})
    $r=Report-Form ('commercial-empty-'+$w)
    Assert-Form ($r.balance -match '—') 'Empty commercial Stock invented volume.'
}
& $driver -Action Invoke -EvidenceFolder $folder -Name 'Guardar Stock'
Capture-Form 'stock-comercial-validacion-1366'
$r=Report-Form 'commercial-invalid'
Assert-Form ($r.commercialError.Length -gt 0) 'Empty commercial form did not explain required fields.'
$certificate=@{Reference='';Matrix='';Components=@(@{Id=[Guid]::NewGuid().ToString();Name='Producto comercial de prueba';CasNumber='92-67-1';CertifiedConcentration=1000;ConcentrationUnit='µg/mL'})}
Send-Form @{action='entryFill';kind='Stock';fields=@{ProductNameBox='Producto comercial · PRUEBA VISUAL';BrandBox='Fabricante de prueba';LotBox='LOTE-VISUAL';VolumeBox='10';BottleCountBox='1';StorageBox='Ambiente'};certificate=$certificate}
foreach ($w in @(1366,1920)) {
    Send-Form @{action='formScroll';offset=0}
    Capture-Form ('stock-comercial-datos-'+$w) $w $(if ($w -eq 1366) {768} else {1080})
    Send-Form @{action='entryGeometry';kind='Stock';name=('commercial-geometry-'+$w)}
    $r=Report-Form ('commercial-filled-'+$w)
    Assert-Form ($r.isCommercial -and $r.concentration -match '1000' -and $r.balance -match '100 %') 'Commercial summary did not show concentration and initial availability.'
    Send-Form @{action='formScroll';offset=9999}
    Capture-Form ('stock-comercial-inventario-'+$w) $w $(if ($w -eq 1366) {768} else {1080})
    $s=Report-Form ('commercial-scroll-'+$w)
    Assert-Form ([Math]::Abs($r.result.y-$s.result.y) -lt 1) 'Commercial summary moved with form scroll.'
}
Send-Form @{action='formField';field='MatrixSelect'}
Send-Form @{action='formPopup';field='MatrixSelect'}
& $driver -Action Capture -EvidenceFolder $folder -Name stock-comercial-matriz
& $driver -Action Key -EvidenceFolder $folder -Keys '{ESC}'
Send-Form @{action='stockClose'}
Write-Output 'Stock comercial: capturas y validacion completadas.'
Send-Form @{action='mixOpen'}
foreach ($w in @(1366,1920)) {
    Capture-Form ('intermedia-vacia-'+$w) $w $(if ($w -eq 1366) {768} else {1080})
    $r=Report-Mix ('mix-empty-'+$w)
    Assert-Form ($r.totalRows -eq 0 -and -not $r.CanSave -and $r.viewport.height -le 110) 'Empty component table reserves excessive height or enables saving.'
}
Send-Form @{action='mixFill'}
$rows=@("o-Toluidina`t95-53-4`t50`tµg/mL","2-Anisidina`t90-04-0`t49.9`tµg/mL","4-Aminobifenilo`t92-67-1`t50.1`tµg/mL")
Send-Form @{action='mixPaste';text=($rows -join "`n")}
foreach ($w in @(1366,1920)) {
    Send-Form @{action='mixSection';section='table'}
    Capture-Form ('intermedia-tres-analitos-'+$w) $w $(if ($w -eq 1366) {768} else {1080})
    $r=Report-Mix ('mix-three-'+$w)
    Assert-Form ($r.ValidCount -eq 3 -and $r.CanSave) 'Three components did not validate.'
}
$more=@(for ($i=4;$i -le 20;$i++) {"Analito de prueba $i`t`t50.1`tµg/mL"})
Send-Form @{action='mixPaste';text=($more -join "`n")}
foreach ($w in @(1366,1920)) {
    Send-Form @{action='mixScroll';offset=0}
    Capture-Form ('intermedia-veinte-analitos-'+$w) $w $(if ($w -eq 1366) {768} else {1080})
    $before=Report-Mix ('mix-twenty-'+$w)
    Send-Form @{action='mixScroll';offset=9999}
    Capture-Form ('intermedia-veinte-desplazamiento-'+$w) $w $(if ($w -eq 1366) {768} else {1080})
    $after=Report-Mix ('mix-scroll-'+$w)
    Assert-Form ($after.ValidCount -eq 20 -and $after.VerticalOffset -gt 0 -and ($after.rows | Where-Object {$_.Number -eq 20}).visible) 'Twentieth analyte is inaccessible.'
    Assert-Form ([Math]::Abs($before.header.y-$after.header.y) -lt 1 -and [Math]::Abs($before.footer.y-$after.footer.y) -lt 1) 'Intermedia table scroll displaced its header or footer.'
}
Send-Form @{action='mixSearch';text='2-Anisidina'}
Capture-Form 'intermedia-busqueda-1366'
$r=Report-Mix 'mix-search'
Assert-Form ($r.ValidCount -eq 20 -and @($r.rows | Where-Object {$_.visible}).Count -eq 1) 'Analyte search lost data or failed.'
Send-Form @{action='mixSearch';text=''}
Send-Form @{action='mixPaste';text="Analito inválido`t`t-1`tµg/mL"}
Capture-Form 'intermedia-validacion-1366'
$r=Report-Mix 'mix-invalid-paste'
Assert-Form ($r.ValidCount -eq 20 -and $r.ImportMessage -notmatch '^1 analitos importados') 'Invalid paste altered composition.'
Send-Form @{action='mixSection';section='inventory'}
Capture-Form 'intermedia-inventario-1366'
Send-Form @{action='mixListReport';name='before-cancel'}
& $driver -Action Invoke -EvidenceFolder $folder -Name 'Cancelar'
Start-Sleep -Milliseconds 300
Send-Form @{action='mixListReport';name='after-cancel'}
$before=Get-Content (Join-Path $evidence 'before-cancel.json') -Raw | ConvertFrom-Json
$after=Get-Content (Join-Path $evidence 'after-cancel.json') -Raw | ConvertFrom-Json
Assert-Form (-not $after.IsCommercialWorkspaceOpen -and $after.Page -eq $before.Page -and $after.search -eq $before.search) 'Cancel did not preserve the Intermedia list.'
# The general creation route must also lead to the modal.
Send-Form @{action='intEditor'}
Send-Form @{action='entryCommercial';kind='Intermedia'}
Send-Form @{action='mixFill'}
Send-Form @{action='mixPaste';text=($rows -join "`n")}
Capture-Form 'intermedia-desde-nueva-preparacion-1366'
& $driver -Action Invoke -EvidenceFolder $folder -Name 'Guardar Intermedia'
Start-Sleep -Milliseconds 1500
Send-Form @{action='entryReport'}
$saved=Get-Content (Join-Path $evidence 'commercial-entry-report.json') -Raw -Encoding utf8 | ConvertFrom-Json
Assert-Form ($saved.Roles.Count -eq 1 -and $saved.LastRequest.NewMaterial.Commercial.Components.Count -eq 3) 'Modal save did not reach the commercial fixture client exactly once.'
Send-Form @{action='entryCloseDetail'}
@{fixtureOnly=$true;passed=$true;resolutions=@('1366x768','1920x1080');scale=$r.RasterizationScale;completedUtc=[DateTime]::UtcNow.ToString('o');checks=@('three real WinUI dialogs','empty and populated states','compact empty table','3 and 20 analytes','internal scroll and fixed footer','search and catalogue popups','invalid composition and commercial fields','Excel clipboard and invalid paste','cancel preserves list','both Intermedia modal routes','fixture Stock consumption and Intermedia save')} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $evidence 'validation-result.json') -Encoding utf8
Send-Form @{action='exit'}
Write-Output $evidence
