$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runtime=Get-Content -LiteralPath (Join-Path $workspace 'artifacts/planificacion-dev/runtime.json') -Raw|ConvertFrom-Json
if($runtime.Host -ne '127.0.0.1' -or $runtime.Port -ne 55441 -or $runtime.Database -ne 'lims_planning_dev'){throw 'Solo se permite PostgreSQL local aislado.'}
$saved=$env:LIMS_PLANNING_TEST_CONNECTION
$env:LIMS_PLANNING_TEST_CONNECTION="Host=127.0.0.1;Port=55441;Database=lims_planning_dev;Username=$($runtime.Username);Password=$($runtime.Password)"
try {
    dotnet test (Join-Path $workspace 'Tests/Backend/Lims.Infrastructure.Tests/Lims.Infrastructure.Tests.csproj') --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~Closure|FullyQualifiedName~Closures|FullyQualifiedName~SessionsSeparateSameClock|FullyQualifiedName~GroupReviewRejects' --logger 'trx;LogFileName=closures-postgres.trx' --results-directory (Join-Path $workspace 'artifacts/validation/closures/tests') --verbosity minimal
    if($LASTEXITCODE -ne 0){throw 'Fallaron las pruebas de Cierres.'}
} finally {$env:LIMS_PLANNING_TEST_CONNECTION=$saved}
