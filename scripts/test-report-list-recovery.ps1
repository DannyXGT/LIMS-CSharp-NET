$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runtime=Get-Content (Join-Path $workspace 'artifacts/planificacion-dev/runtime.json') -Raw|ConvertFrom-Json
if($runtime.Host -ne '127.0.0.1' -or $runtime.Port -ne 55441 -or $runtime.Database -ne 'lims_planning_dev'){throw 'Only the isolated local database is authorized.'}
$saved=$env:LIMS_PLANNING_TEST_CONNECTION
$env:LIMS_PLANNING_TEST_CONNECTION="Host=127.0.0.1;Port=55441;Database=lims_planning_dev;Username=$($runtime.Username);Password=$($runtime.Password)"
$out=Join-Path $workspace 'artifacts/report-list/recovery/tests'
try{
    $cases=@(
        @{Project='Tests/Backend/Lims.Application.Tests/Lims.Application.Tests.csproj';Filter='FullyQualifiedName~PlanningTests|FullyQualifiedName~ReportListRulesTests';Name='rules'},
        @{Project='Tests/Backend/Lims.Infrastructure.Tests/Lims.Infrastructure.Tests.csproj';Filter='FullyQualifiedName~PlanningPostgresTests';Name='postgres'},
        @{Project='Tests/Backend/Lims.Api.Tests/Lims.Api.Tests.csproj';Filter='FullyQualifiedName~PlanningAuthorizationTests';Name='authorization'},
        @{Project='Tests/Frontend/Lims.Desktop.Tests/Lims.Desktop.Tests.csproj';Filter='FullyQualifiedName~Planning';Name='desktop'}
    )
    foreach($case in $cases){
        dotnet test $case.Project --no-restore -m:1 --filter $case.Filter --logger "trx;LogFileName=$($case.Name).trx" --results-directory $out
        if($LASTEXITCODE -ne 0){throw "Failed: $($case.Name)"}
    }
}finally{$env:LIMS_PLANNING_TEST_CONNECTION=$saved}
