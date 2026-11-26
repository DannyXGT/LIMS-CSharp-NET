param([switch]$SkipBuild,[switch]$SkipStart)
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$driver=Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$folder='import-flow-20261126'
$evidence=Join-Path $workspace ('artifacts/validation/'+$folder)
if(-not $SkipBuild){
    dotnet build (Join-Path $workspace 'Tests/Frontend/Lims.Desktop.VisualHarness/Lims.Desktop.VisualHarness.csproj') --no-restore -p:Platform=x64 -m:1 -p:UseSharedCompilation=false -v:minimal
    if($LASTEXITCODE){throw 'Falló la compilación del harness nativo.'}
}
if(-not $SkipStart){
    $evidenceRoot=[IO.Path]::GetFullPath($evidence)+[IO.Path]::DirectorySeparatorChar
    Get-Process -Name ('Lims.VisualValidation.'+$folder) -ErrorAction SilentlyContinue |
        Where-Object {$_.Path -and $_.Path.StartsWith($evidenceRoot,[StringComparison]::OrdinalIgnoreCase)} | Stop-Process
    & $driver -Action Start -SkipBuild -IsolatedIdentity -EvidenceFolder $folder
}
$deadline=[DateTime]::UtcNow.AddSeconds(20)
while(-not(Test-Path -LiteralPath (Join-Path $evidence 'window.json'))){
    if([DateTime]::UtcNow -gt $deadline){throw 'No inició la ventana aislada. Consulte launch.err.log.'}
    Start-Sleep -Milliseconds 150
}
foreach($scenario in @('empty','selected','timeout','review')){
    & $driver -Action Command -EvidenceFolder $folder -Command (@{action='planningImport';scenario=$scenario;width=1366;height=768}|ConvertTo-Json -Compress)
    $result=Get-Content -LiteralPath (Join-Path $evidence ('planning-import-'+$scenario+'.json')) -Raw|ConvertFrom-Json
    if(-not $result.Passed){throw ('Falló el escenario '+$scenario)}
    & $driver -Action Capture -EvidenceFolder $folder -Name ('import-'+$scenario)
    Write-Output ($scenario+': PASS; HTTP requests: '+$result.Requests)
}
Write-Output ('Evidencia de WinUI con archivos y HTTP ficticios: '+$evidence)
