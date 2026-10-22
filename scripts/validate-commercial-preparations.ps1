param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
Set-ExecutionPolicy -Scope Process Bypass
$driver = Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$folder = 'commercial-fix-20261019'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$evidence = Join-Path $workspace ('artifacts/validation/' + $folder)
function Send-EntryCommand($value) {
    & $driver -Action Command -EvidenceFolder $folder -Command ($value | ConvertTo-Json -Depth 16 -Compress)
    Start-Sleep -Milliseconds 250
}
function Capture-Entry($name) { & $driver -Action Capture -EvidenceFolder $folder -Name $name }
& $driver -Action Start -EvidenceFolder $folder -IsolatedIdentity -SkipBuild:$SkipBuild
$deadline = [DateTime]::UtcNow.AddSeconds(25)
while (-not (Test-Path -LiteralPath (Join-Path $evidence 'window.json'))) {
    if ([DateTime]::UtcNow -gt $deadline) { throw 'WinUI did not start; inspect crash.txt.' }
    Start-Sleep -Milliseconds 200
}
Send-EntryCommand @{ action='standardEntryCatalogs' }
Send-EntryCommand @{ action='editor' }
Send-EntryCommand @{ action='fill' }
Send-EntryCommand @{ action='text'; field='NameBox'; text='Estándar · PRUEBA VISUAL' }
Send-EntryCommand @{ action='text'; field='StorageTemperatureBox'; text='Ambiente' }
Send-EntryCommand @{ action='standardEntryReport' }
foreach ($size in @(@{w=1366;h=768}, @{w=1920;h=1080})) {
    Send-EntryCommand @{ action='size'; width=$size.w; height=$size.h }
    Send-EntryCommand @{ action='editorScroll'; offset=0 }
    Capture-Entry ('standard-'+$size.w+'-form')
    Send-EntryCommand @{ action='editorScroll'; offset=9999 }
    Capture-Entry ('standard-'+$size.w+'-storage')
}
& $driver -Action Invoke -EvidenceFolder $folder -Name 'Cancelar'
Start-Sleep -Milliseconds 500
$standard = Get-Content -LiteralPath (Join-Path $evidence 'standard-entry-report.json') -Raw | ConvertFrom-Json
if (-not $standard.presentationSelectorRemoved -or -not $standard.commercialEditorRemoved -or $standard.request.Presentation -ne 'Solid' -or $standard.request.MethodId -le 0 -or $standard.request.UnitId -le 0 -or $standard.request.LocationId -le 0) { throw 'The standard entry still exposes commercial fields.' }
foreach ($kind in @('Stock', 'Intermedia')) {
    if ($kind -eq 'Stock') { Send-EntryCommand @{ action='preparations' }; Send-EntryCommand @{ action='stockEditor' }; $scroll='stockScroll' }
    else { Send-EntryCommand @{ action='intermediate' }; Send-EntryCommand @{ action='intEditor' }; $scroll='intScroll' }
    Send-EntryCommand @{ action='entryCommercial'; kind=$kind }
    Start-Sleep -Milliseconds 400
    $components = @(for ($i=1; $i -le $(if ($kind -eq 'Stock') { 1 } else { 20 }); $i++) {
        @{ Id=[Guid]::NewGuid().ToString(); Name="Analito de prueba $i"; CasNumber=$null; CertifiedConcentration=$(if ($kind -eq 'Stock') { 999 } elseif ($i -eq 1) { 50 } elseif ($i -eq 2) { 49.9 } else { 50.1 }); ConcentrationUnit='µg/mL' }
    })
    Send-EntryCommand @{ action='entryFill'; kind=$kind; fields=@{ ProductNameBox=$(if ($kind -eq 'Stock') { 'Aluminio · PRUEBA VISUAL' } else { 'Mix · PRUEBA VISUAL' }); BrandBox='Fabricante de prueba'; LotBox='LOTE-VISUAL'; VolumeBox=$(if ($kind -eq 'Stock') { '100' } else { '10' }); BottleCountBox='1'; StorageBox='Ambiente' }; certificate=@{ Reference=''; Matrix=''; Components=$components } }
    foreach ($size in @(@{w=1366;h=768}, @{w=1920;h=1080})) {
        $name = $kind.ToLowerInvariant() + '-' + $size.w
        Send-EntryCommand @{ action='size'; width=$size.w; height=$size.h }
        Send-EntryCommand @{ action=$scroll; offset=0 }
        Capture-Entry ($name+'-form')
        Send-EntryCommand @{ action=$scroll; offset=9999 }
        Capture-Entry ($name+'-inventory')
        Send-EntryCommand @{ action=$scroll; offset=240 }
        Capture-Entry ($name+'-components')
        Send-EntryCommand @{ action='entryGeometry'; kind=$kind; name=$name }
        $report = Get-Content -LiteralPath (Join-Path $evidence ($name+'.json')) -Raw | ConvertFrom-Json
        if ($kind -eq 'Stock' -and ($report.resultText -notlike '*999*' -or $report.resultBalance -notlike '*100*')) { throw 'Stock live result did not update.' }
        if ($report.request.NewMaterial.Commercial.Components.Count -ne $(if ($kind -eq 'Stock') { 1 } else { 20 })) { throw 'Component count mismatch.' }
        if (-not $report.footerVisible -or -not $report.internalFormCollapsed -or -not $report.IsReady) { throw "Invalid editor geometry: $name" }
    }
    & $driver -Action Invoke -EvidenceFolder $folder -Name ("Guardar " + $kind)
    Start-Sleep -Milliseconds 1100
    foreach ($size in @(@{w=1366;h=768}, @{w=1920;h=1080})) {
        Send-EntryCommand @{ action='size'; width=$size.w; height=$size.h }
        Capture-Entry ($kind.ToLowerInvariant()+'-'+$size.w+'-detail')
    }
    Send-EntryCommand @{ action='entryCloseDetail' }
}
Send-EntryCommand @{ action='entryReport' }
Write-Output $evidence
