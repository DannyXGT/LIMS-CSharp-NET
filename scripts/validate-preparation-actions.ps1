param([switch]$SkipBuild, [switch]$ReuseWindow, [switch]$SecondaryForms)
$ErrorActionPreference = 'Stop'
Set-ExecutionPolicy -Scope Process Bypass
$driver = Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$folder = 'preparation-actions'
$evidence = Join-Path $PSScriptRoot '../artifacts/validation/preparation-actions'
# This is a saved test-catalog snapshot, never a replacement catalog in the application.
$catalog = Get-Content (Join-Path $PSScriptRoot '../artifacts/validation/shared-solvents/persistent-catalog.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$ternary = @(@{id=4;percent='30'},@{id=8;percent='20'},@{id=3;percent='50'})
function Send-Action($data) {
    & $driver -Action Command -EvidenceFolder $folder -Command ($data | ConvertTo-Json -Depth 30 -Compress)
    Start-Sleep -Milliseconds $(if($data.action -in @('stockClose','intClose','groupClose')){800}else{300})
}
function Assert-Action($condition, $message) { if (-not $condition) { throw $message } }
function Capture-Action($name) {
    Send-Action @{action='permissionActivate'}
    & $driver -Action Capture -EvidenceFolder $folder -Name $name
}
function Size-Action($width) {
    Send-Action @{action='size';width=$width;height=$(if($width -eq 1366){768}else{1080})}
    Start-Sleep -Milliseconds 250
}
function Report-Action($kind,$name) {
    Send-Action @{action='actionsReport';kind=$kind;name=$name}
    $r=Get-Content (Join-Path $evidence ($name+'.json')) -Raw -Encoding UTF8|ConvertFrom-Json
    foreach($button in @($r.save,$r.cancel)) {
        Assert-Action ($button.width -gt 0 -and $button.height -gt 0 -and $button.x -ge 0 -and $button.y -ge 0 -and ($button.x+$button.width) -le ($r.Size.Width+1) -and ($button.y+$button.height) -le ($r.Size.Height+1)) ('Action outside viewport: '+$name)
    }
    foreach($button in @($r.buttons|Where-Object {$_.icon -ne 'None'})) {
        Assert-Action ($button.renderedIcons -contains $button.icon) ('Missing artwork: '+$button.icon+' '+$name)
    }
    return $r
}
function Capture-Both($kind,$name) {
    foreach($width in @(1366,1920)) {
        Size-Action $width
        Capture-Action ($name+'-'+$width)
        $null=Report-Action $kind ($name+'-'+$width)
    }
}
function Set-Catalog($kind) { Send-Action @{action='actionsCatalog';kind=$kind;catalog=@($catalog | Select-Object Id,Name,Abbreviation,IsActive,DisplayOrder)} }
function Set-Composition($kind,$rows) { Send-Action @{action='actionsComposition';kind=$kind;rows=$rows} }
function Snapshot-Action($report) { @{inputs=$report.inputs;selections=$report.selections;composition=$report.composition}|ConvertTo-Json -Depth 20 -Compress }
function Test-Resize($kind,$prefix) {
    foreach($width in @(1366,1920)) {
        Size-Action $width
        $before=Report-Action $kind ($prefix+'-before-'+$width)
        & $driver -Action Click -EvidenceFolder $folder -Name 'Ampliar'
        Start-Sleep -Milliseconds 250
        $expanded=Report-Action $kind ($prefix+'-expanded-'+$width)
        Assert-Action ($expanded.root.width -gt $before.root.width) 'Expand did not increase the useful area.'
        Assert-Action ((Snapshot-Action $before) -eq (Snapshot-Action $expanded)) 'Expand lost form data.'
        Assert-Action (($expanded.buttons|Where-Object {$_.icon -eq 'Restore'}).tooltip -eq 'Restaurar') 'Restore artwork/tooltip missing.'
        Capture-Action ($prefix+'-ampliado-'+$width)
        & $driver -Action Click -EvidenceFolder $folder -Name 'Restaurar'
        Start-Sleep -Milliseconds 250
        $restored=Report-Action $kind ($prefix+'-restored-'+$width)
        Assert-Action ([Math]::Abs($restored.root.width-$before.root.width) -lt 1 -and (Snapshot-Action $before) -eq (Snapshot-Action $restored)) 'Restore changed the size or form data.'
    }
}
function Pointer-State($kind,$field,$name,$pressed=$false) {
    & $driver -Action $(if($pressed){'PressCapture'}else{'HoverCapture'}) -EvidenceFolder $folder -Name $field -CaptureName $name
    $r=Report-Action $kind $name
    if(-not $pressed) {
        $b=$r.buttons|Where-Object {$_.label -eq $field -or $_.content -eq $field}|Select-Object -First 1
        Assert-Action ($b.state -contains 'PointerOver') ('Wrong hover state: '+$name)
    }
    [LimsVisualCapture]::SetPhysicalCursorPos(12,12)|Out-Null
}
if(-not $ReuseWindow) {
    $env:LIMS_SOLVENT_TEST_CONFIG=$null
    & $driver -Action Start -EvidenceFolder $folder -IsolatedIdentity -SkipBuild:$SkipBuild
    $deadline=[DateTime]::UtcNow.AddSeconds(30)
    while(-not(Test-Path (Join-Path $evidence 'window.json'))){if([DateTime]::UtcNow -gt $deadline){throw 'WinUI did not start.'};Start-Sleep -Milliseconds 200}
    Start-Sleep -Milliseconds 700
    Send-Action @{action='preparations'}
    Send-Action @{action='stockEditor'}
    Send-Action @{action='entryCommercial';kind='Stock'}
}
if($SecondaryForms) { Send-Action @{action='stockClose'} } else {
Set-Catalog 'Stock'
Capture-Both 'Stock' 'stock-comercial-vacio'
$empty=Report-Action 'Stock' 'commercial-empty'
Assert-Action (-not $empty.saveEnabled -and $empty.volume -eq '—' -and ($empty.visibleCompositionError -join ' ') -notmatch '\?') 'Empty commercial state invented data or allowed save.'
Send-Action @{action='formScroll';offset=9999}
Capture-Action 'stock-comercial-matriz-vacia'
$before=Report-Action 'Stock' 'empty-row-before'
& $driver -Action Click -EvidenceFolder $folder -Name 'Agregar solvente'
& $driver -Action Click -EvidenceFolder $folder -Name 'Agregar solvente'
$after=Report-Action 'Stock' 'empty-row-after'
Assert-Action ($before.composition.Count -eq $after.composition.Count) 'Add inserted redundant empty rows.'
$certificate=@{Reference='';Matrix='';Components=@(@{Id=[Guid]::NewGuid().ToString();Name='Producto comercial de prueba';CasNumber='92-67-1';CertifiedConcentration=1000;ConcentrationUnit='µg/mL'})}
Send-Action @{action='entryFill';kind='Stock';fields=@{ProductNameBox='Producto comercial · PRUEBA VISUAL';BrandBox='Fabricante de prueba';LotBox='LOTE-VISUAL';VolumeBox='10';BottleCountBox='1';StorageBox='Ambiente'};certificate=$certificate}
Set-Composition 'Stock' $ternary
Send-Action @{action='formScroll';offset=0}
Capture-Both 'Stock' 'stock-comercial-datos'
$filled=Report-Action 'Stock' 'commercial-filled'
Assert-Action ($filled.saveEnabled -and $filled.concentration -match '1000' -and $filled.volume -eq '10 mL' -and $filled.available -eq 100) 'Commercial result lost certified concentration or inventory preview.'
Test-Resize 'Stock' 'stock-comercial'
Size-Action 1366
Send-Action @{action='actionsSection';kind='Stock';field='CompositionEditor'}
Capture-Action 'stock-comercial-composicion-1366'
Pointer-State 'Stock' 'Guardar Stock' 'guardar-hover'
Pointer-State 'Stock' 'Guardar Stock' 'guardar-pressed' $true
Send-Action @{action='actionsFocus';kind='Stock';field='SaveButton'}
Capture-Action 'guardar-focus'
$focused=Report-Action 'Stock' 'guardar-focus'
Assert-Action (($focused.buttons|Where-Object {$_.Name -eq 'SaveButton'}).focus -eq 'Keyboard') 'Keyboard focus missing.'
Pointer-State 'Stock' 'Solvente 1' 'selector-hover'
Pointer-State 'Stock' 'Solvente 1' 'selector-pressed' $true
Send-Action @{action='actionsFocus';kind='Stock';field='Solvente 1'}
Capture-Action 'selector-focus'
Set-Composition 'Stock' @(@{id=4;percent='40'},@{id=3;percent='50'})
Send-Action @{action='actionsFocus';kind='Stock';field='CancelButton'}
Capture-Action 'guardar-disabled-total-90'
$invalid=Report-Action 'Stock' 'invalid-total'
Assert-Action (-not $invalid.saveEnabled -and $invalid.status -match '100') 'Invalid composition allowed save or lacked an explanation.'
Set-Composition 'Stock' @(@{id=4;percent='50'},@{id=4;percent='50'})
Capture-Action 'solvente-duplicado'
$duplicate=Report-Action 'Stock' 'duplicate'
Assert-Action (-not $duplicate.saveEnabled -and $duplicate.compositionError -match 'Unifique') 'Duplicate validation changed.'
Send-Action @{action='actionsCatalog';kind='Stock';catalog=@()}
Capture-Action 'selector-disabled-catalogo'
$disabled=Report-Action 'Stock' 'selector-disabled'
Assert-Action (-not $disabled.saveEnabled -and -not ($disabled.buttons|Where-Object {$_.label -eq 'Solvente 1'}).IsEnabled) 'Unavailable catalog allowed interaction.'
Send-Action @{action='stockClose'}
Send-Action @{action='actionsCompatibility';supported=$false}
Send-Action @{action='stockEditor'}
Send-Action @{action='entryCommercial';kind='Stock'}
Capture-Both 'Stock' 'integracion-api-pendiente'
$integration=Report-Action 'Stock' 'integration'
Assert-Action (-not $integration.saveEnabled -and $integration.status -match 'Integración pendiente') 'API incompatibility was hidden or bypassed.'
Send-Action @{action='stockClose'}
Send-Action @{action='actionsCompatibility';supported=$true}
Send-Action @{action='stockEditor'}
Set-Catalog 'StockPrepared'
Capture-Both 'StockPrepared' 'stock-preparado-vacio'
Send-Action @{action='stockSelect';index=0}
foreach($field in @(@{field='ConcentrationBox';text='100'},@{field='FinalQuantityBox';text='100'},@{field='ActualQuantityBox';text='10.1'},@{field='StorageTemperatureBox';text='Ambiente'})){$field.action='stockText';Send-Action $field}
Send-Action @{action='stockDate';field='ExpirationDatePicker';text='2027-06-24T00:00:00-06:00'}
Set-Composition 'StockPrepared' $ternary
Capture-Both 'StockPrepared' 'stock-preparado-datos'
$prepared=Report-Action 'StockPrepared' 'prepared-valid'
Assert-Action $prepared.saveEnabled 'Prepared Stock calculation/validation regressed.'
Send-Action @{action='stockClose'}
}
Send-Action @{action='intermediate'}
Send-Action @{action='intEditor'}
Capture-Both 'IntermediaPrepared' 'intermedia-preparada-vacia'
Send-Action @{action='intScenario';volumes=@(1,2)}
Capture-Both 'IntermediaPrepared' 'intermedia-preparada-datos'
Send-Action @{action='intClose'}
Send-Action @{action='mixOpen'}
Set-Catalog 'Intermedia'
Capture-Both 'Intermedia' 'intermedia-comercial-vacia'
Send-Action @{action='mixFill'}
Send-Action @{action='mixPaste';text="Analito A`t`t50`tµg/mL`nAnalito B`t`t49.9`tµg/mL"}
Set-Composition 'Intermedia' $ternary
Capture-Both 'Intermedia' 'intermedia-comercial-datos'
Test-Resize 'Intermedia' 'intermedia-comercial'
& $driver -Action Invoke -EvidenceFolder $folder -Name 'Cancelar'
Start-Sleep -Milliseconds 800
foreach($kind in @('Curva','AQS')) {
    Send-Action @{action='group';kind=$kind}
    Send-Action @{action='groupEditor';kind=$kind}
    Capture-Both $kind ($kind.ToLower()+'-vacia')
    Send-Action @{action='groupScenario';kind=$kind;code=('VISUAL-'+$kind);volumes=@(1,2)}
    Capture-Both $kind ($kind.ToLower()+'-datos')
    Send-Action @{action='groupClose';kind=$kind}
}
@{passed=$true;fixtureOnly=$true;databaseModified=$false;resolutions=@('1366x768','1920x1080');checks=@('six preparation forms','shared action artwork','visible fixed footer','native hover/pressed and keyboard focus','disabled save explanation','resize and restore preserve inputs and selections','contextual composition validation','no redundant empty rows','API integration block retained')}|ConvertTo-Json -Depth 6|Set-Content (Join-Path $evidence $(if($SecondaryForms){'spacing-validation.json'}else{'visual-validation.json'})) -Encoding UTF8
Send-Action @{action='exit'}
Write-Output $evidence
