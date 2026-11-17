$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runtime=Get-Content -LiteralPath (Join-Path $workspace 'artifacts\planificacion-dev\runtime.json') -Raw|ConvertFrom-Json
if($runtime.Host -ne '127.0.0.1' -or $runtime.Port -ne 55441 -or $runtime.Database -ne 'lims_planning_dev'){throw 'Destino de desarrollo inesperado.'}
$saved=$env:LIMS_PLANNING_TEST_CONNECTION
$env:LIMS_PLANNING_TEST_CONNECTION="Host=127.0.0.1;Port=55441;Database=lims_planning_dev;Username=$($runtime.Username);Password=$($runtime.Password)"
try {
    dotnet test Tests/Backend/Lims.Application.Tests/Lims.Application.Tests.csproj --no-restore -m:1 --filter FullyQualifiedName~PlanningTests
    if($LASTEXITCODE -ne 0){throw 'Fallaron las reglas de Planificación.'}
    dotnet test Tests/Backend/Lims.Infrastructure.Tests/Lims.Infrastructure.Tests.csproj --no-restore -m:1 --filter FullyQualifiedName~PlanningPostgresTests
    if($LASTEXITCODE -ne 0){throw 'Fallaron las pruebas PostgreSQL aisladas.'}
}finally{$env:LIMS_PLANNING_TEST_CONNECTION=$saved}
