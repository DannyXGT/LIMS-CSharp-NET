param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
Set-ExecutionPolicy -Scope Process Bypass
$driver = Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$folder = 'intermedia-workspace'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$evidence = Join-Path $workspace ('artifacts/validation/' + $folder)
function Send-Mix($value) {
    & $driver -Action Command -EvidenceFolder $folder -Command ($value | ConvertTo-Json -Depth 16 -Compress)
    Start-Sleep -Milliseconds 240
}
function Capture-Mix($name) {
    for ($attempt=0;$attempt -lt 3;$attempt++) {
        if ($name -match '(1366|1920)') {
            $width = [int]$Matches[1]; $height = if ($width -eq 1366) {768} else {1080}
            Send-Mix @{action='size';width=$width;height=$height}
            Start-Sleep -Milliseconds 400
        }
        $capture = & $driver -Action Capture -EvidenceFolder $folder -Name $name
        if ($name -notmatch '(1366|1920)' -or ($capture.Width -eq $width -and $capture.Height -eq $height)) { Write-Output $capture; return }
    }
    throw ('Incorrect capture size '+$name)
}
function Key-Mix($keys) {
    Send-Mix @{action='permissionActivate'}
    & $driver -Action Key -EvidenceFolder $folder -Keys $keys
}
function Report-Mix($name) {
    Send-Mix @{action='mixReport';name=$name}
    return (Get-Content -LiteralPath (Join-Path $evidence ($name+'.json')) -Raw -Encoding utf8 | ConvertFrom-Json)
}
function Assert-Mix($condition, $message) { if (-not $condition) { throw $message } }
& $driver -Action Start -EvidenceFolder $folder -IsolatedIdentity -SkipBuild:$SkipBuild
$deadline = [DateTime]::UtcNow.AddSeconds(30)
while (-not (Test-Path -LiteralPath (Join-Path $evidence 'window.json'))) {
    if ([DateTime]::UtcNow -gt $deadline) { throw 'WinUI did not start; inspect crash.txt.' }
    Start-Sleep -Milliseconds 200
}
Send-Mix @{action='mixOpen'}
Send-Mix @{action='permissionActivate'}
foreach ($size in @(@{w=1366;h=768},@{w=1920;h=1080})) {
    Send-Mix @{action='size';width=$size.w;height=$size.h}
    Capture-Mix ('empty-'+$size.w)
    $r = Report-Mix ('empty-'+$size.w)
    Assert-Mix ($r.ValidCount -eq 0 -and $r.totalRows -eq 0 -and -not $r.CanSave -and $null -eq $r.InitialVolume) 'Empty page invented inventory or components.'
    Assert-Mix (($r.footer.y+$r.footer.height) -le $r.Size.Height) 'Footer is outside viewport.'
}
Send-Mix @{action='mixFill'}
$rows = @("o-Toluidina`t95-53-4`t50`tµg/mL", "2-Anisidina`t90-04-0`t49.9`tµg/mL", "4-Aminobifenilo`t92-67-1`t50.1`tµg/mL")
Send-Mix @{action='mixPaste';text=($rows -join "`n")}
foreach ($size in @(@{w=1366;h=768},@{w=1920;h=1080})) {
    Send-Mix @{action='size';width=$size.w;height=$size.h}
    Send-Mix @{action='mixSection';section='table'}
    Capture-Mix ('three-'+$size.w)
    $r=Report-Mix ('three-'+$size.w)
    Assert-Mix ($r.ValidCount -eq 3 -and $r.CanSave -and $r.AvailabilityPercent -eq 100) 'Three-row live summary or field bindings failed.'
}
$more = @(for ($i=4;$i -le 20;$i++) { "Analito de prueba $i`t`t50.1`tµg/mL" })
Send-Mix @{action='mixPaste';text=($more -join "`n")}
foreach ($size in @(@{w=1366;h=768},@{w=1920;h=1080})) {
    Send-Mix @{action='size';width=$size.w;height=$size.h}
    Send-Mix @{action='mixSection';section='table'}
    Send-Mix @{action='mixScroll';offset=0}
    $before=Report-Mix ('twenty-'+$size.w)
    Capture-Mix ('twenty-'+$size.w)
    Send-Mix @{action='mixScroll';offset=9999}
    $after=Report-Mix ('scroll-'+$size.w)
    Capture-Mix ('scroll-'+$size.w)
    Assert-Mix ($after.ValidCount -eq 20 -and $after.rows.Count -eq 20 -and $after.CanSave) 'Twenty analytes were not retained.'
    Assert-Mix ($after.VerticalOffset -gt 0 -and ($after.rows | Where-Object { $_.Number -eq 20 }).visible) 'Last row cannot be reached through table scroll.'
    Assert-Mix ([Math]::Abs($before.header.y-$after.header.y) -lt 1 -and [Math]::Abs($before.summary.y-$after.summary.y) -lt 1 -and [Math]::Abs($before.captureOffset-$after.captureOffset) -lt 1) 'Table scrolling displaced header or summary.'
    Send-Mix @{action='mixSection';section='inventory'}
    Capture-Mix ('inventory-'+$size.w)
    Send-Mix @{action='mixScroll';field='SummaryScroll';offset=9999}
    Capture-Mix ('composition-expiry-'+$size.w)
    Send-Mix @{action='mixScroll';field='SummaryScroll';offset=0}
}
Send-Mix @{action='size';width=1366;height=768}
Send-Mix @{action='mixSection';section='table'}
Send-Mix @{action='mixSearch';text='2-Anisidina'}
$r=Report-Mix 'search'
Assert-Mix ($r.ValidCount -eq 20 -and @($r.rows | Where-Object {$_.visible}).Count -eq 1) 'Search altered composition or failed to filter.'
Send-Mix @{action='mixSearch';text=''}
Send-Mix @{action='mixScroll';offset=0}
Send-Mix @{action='mixFocus';row=0;field='AnaltyeName'}
Key-Mix '{DOWN}'
$r=Report-Mix 'keyboard-down'
Assert-Mix ($r.focused.label -eq 'Analito 2 · Nombre') 'Arrow navigation did not focus same cell in next row.'
Key-Mix '{TAB}'
$r=Report-Mix 'keyboard-tab'
Assert-Mix ($r.focused.label -eq 'Analito 2 · CAS') 'Tab navigation skipped the CAS cell.'
Send-Mix @{action='mixSection';section='product'}
foreach ($field in @('ProductNameBox','MethodSelect','BrandBox','CatalogBox','LotBox')) {
    Send-Mix @{action='mixField';field=$field}
    $r=Report-Mix ('access-'+$field)
    Assert-Mix ($r.focused.Name -eq $field -and $r.focused.intersectsCapture) ('Inaccessible product field '+$field)
}
Send-Mix @{action='mixSection';section='inventory'}
foreach ($field in @('VolumeBox','VolumeUnitSelect','BottleCountBox','SolventSelect','ReceivedPicker','ExpirationPicker','LocationSelect','StorageBox')) {
    Send-Mix @{action='mixField';field=$field}
    $r=Report-Mix ('access-'+$field)
    Assert-Mix ($r.focused.Name -eq $field -and $r.focused.intersectsCapture) ('Inaccessible inventory field '+$field)
}
Send-Mix @{action='mixPopup';field='SolventSelect'}
Capture-Mix 'solvent-catalogue'
Key-Mix '{ESC}'
Send-Mix @{action='mixSection';section='table'}
& $driver -Action Invoke -EvidenceFolder $folder -Name 'Agregar componente al Mix'
$r=Report-Mix 'blank-row'
Assert-Mix ($r.totalRows -eq 21 -and $r.ValidCount -eq 20 -and $r.CanSave) 'Empty row was counted or blocked valid composition.'
Send-Mix @{action='mixFocus';row=19;field='AnaltyeName'}
Key-Mix '{ENTER}'
$r=Report-Mix 'keyboard-enter'
Assert-Mix ($r.focused.label -eq 'Analito 21 · Nombre') 'Enter did not focus next row.'
Send-Mix @{action='size';width=960;height=768}
Send-Mix @{action='mixSection';section='table'}
Capture-Mix 'narrow-capture-960'
& $driver -Action Invoke -EvidenceFolder $folder -Name 'Ver resumen'
Capture-Mix 'narrow-summary-960'
& $driver -Action Invoke -EvidenceFolder $folder -Name 'Volver a captura'
Send-Mix @{action='size';width=1366;height=768}
Send-Mix @{action='mixListReport';name='before-cancel'}
& $driver -Action Invoke -EvidenceFolder $folder -Name 'Cancelar'
Send-Mix @{action='mixListReport';name='after-cancel'}
$before=Get-Content (Join-Path $evidence 'before-cancel.json') -Raw -Encoding utf8 | ConvertFrom-Json
$after=Get-Content (Join-Path $evidence 'after-cancel.json') -Raw -Encoding utf8 | ConvertFrom-Json
Assert-Mix (-not $after.IsCommercialWorkspaceOpen -and $after.Page -eq $before.Page -and $after.search -eq $before.search -and $after.selected -eq $before.selected) 'Cancel lost Intermedia list context.'
# Exercise the original Nueva Intermedia > Comercial route too.
Send-Mix @{action='intEditor'}
Send-Mix @{action='entryCommercial';kind='Intermedia'}
Send-Mix @{action='mixSection';section='product'}
Send-Mix @{action='mixFill'}
Send-Mix @{action='mixPaste';text=($rows -join "`n")}
$r=Report-Mix 'before-save'
Assert-Mix $r.CanSave 'Reopened page is not valid for save.'
& $driver -Action Invoke -EvidenceFolder $folder -Name 'Guardar Intermedia'
Start-Sleep -Milliseconds 800
Send-Mix @{action='entryReport'}
$registration=Get-Content (Join-Path $evidence 'commercial-entry-report.json') -Raw -Encoding utf8 | ConvertFrom-Json
Assert-Mix ($registration.Roles.Count -eq 1 -and $registration.LastRequest.NewMaterial.Commercial.Components.Count -eq 3) 'Real page save did not reach commercial client exactly once.'
Capture-Mix 'saved-commercial-detail'
Send-Mix @{action='entryCloseDetail'}
@{ fixtureOnly=$true; passed=$true; completedUtc=[DateTime]::UtcNow.ToString('o'); resolutions=@('1366x768','1920x1080','960x768'); checks=@('empty','3 and 20 components','real Windows clipboard paste','independent table scroll','sticky headers','search','keyboard arrows Tab Enter','product and inventory field access','solvent catalogue','blank row excluded','cancel context','modal origin routes to page','commercial API save') } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $evidence 'validation-result.json') -Encoding utf8
Send-Mix @{action='exit'}
Write-Output $evidence
