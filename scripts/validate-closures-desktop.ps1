param([switch]$SkipBuild)
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runner=Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
if(-not $SkipBuild){
    dotnet build (Join-Path $workspace 'Tests/Frontend/Lims.Desktop.VisualHarness/Lims.Desktop.VisualHarness.csproj') --no-restore -m:1 -p:Platform=x64 -p:UseSharedCompilation=false --verbosity quiet
    if($LASTEXITCODE -ne 0){throw 'No se pudo compilar el cliente de validación.'}
}
& $runner -Action Start -SkipBuild -IsolatedIdentity -EvidenceFolder 'closures-desktop'
$evidence=Join-Path $workspace 'artifacts/validation/closures-desktop'
$launchStage=Get-ChildItem -LiteralPath $evidence -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$launchLog=Join-Path $launchStage.FullName 'launch.err.log'
$deadline=[DateTime]::UtcNow.AddSeconds(30)
while(-not(Test-Path -LiteralPath (Join-Path $evidence 'window.json'))){
    if((Test-Path -LiteralPath $launchLog) -and (Get-Item -LiteralPath $launchLog).Length -gt 0){throw ([IO.File]::ReadAllText($launchLog))}
    if([DateTime]::UtcNow -gt $deadline){throw 'Windows no inició el cliente aislado de Cierres.'}
    Start-Sleep -Milliseconds 100
}
& $runner -Action Command -Command '{"action":"closures"}' -EvidenceFolder 'closures-desktop'
$result=Join-Path $workspace 'artifacts/validation/closures-desktop/closures.json'
if(-not(Test-Path -LiteralPath $result)){throw 'No se generó la evidencia de Cierres.'}
$report=Get-Content -LiteralPath $result -Raw|ConvertFrom-Json
if(-not $report.Passed){throw 'Falló la validación WinUI de Cierres.'}
$report
