param([Parameter(Mandatory=$true)][string]$SummaryPath,[Parameter(Mandatory=$true)][string]$ReportPath)
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$savedRuntime=$env:LIMS_PLANNING_ACCEPTANCE_RUNTIME;$savedSummary=$env:LIMS_PLANNING_SUMMARY;$savedReport=$env:LIMS_PLANNING_REPORT
$env:LIMS_PLANNING_ACCEPTANCE_RUNTIME=Join-Path $workspace 'artifacts\planificacion-dev\runtime.json'
$env:LIMS_PLANNING_SUMMARY=(Resolve-Path -LiteralPath $SummaryPath).Path;$env:LIMS_PLANNING_REPORT=(Resolve-Path -LiteralPath $ReportPath).Path
try{
    dotnet test Tests/Backend/Lims.Api.Tests/Lims.Api.Tests.csproj --no-restore -m:1 --filter FullyQualifiedName~PlanningAcceptanceTests
    if($LASTEXITCODE -ne 0){throw 'Falló la aceptación local. Revise artifacts/planificacion-dev/tips-preview.json y la salida de pruebas.'}
}finally{$env:LIMS_PLANNING_ACCEPTANCE_RUNTIME=$savedRuntime;$env:LIMS_PLANNING_SUMMARY=$savedSummary;$env:LIMS_PLANNING_REPORT=$savedReport}
