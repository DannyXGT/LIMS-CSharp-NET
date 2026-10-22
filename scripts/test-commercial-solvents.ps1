param([switch]$SkipBuild)
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$cluster=[IO.Path]::GetFullPath((Join-Path $workspace 'artifacts/validation/stock-postgres/data'))
if(-not $cluster.StartsWith($workspace+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Cluster outside workspace.'}
$pg='C:\Program Files\PostgreSQL\18\bin'
$evidence=Join-Path $workspace 'artifacts/validation/shared-solvents'
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
if(-not(Test-Path (Join-Path $cluster 'PG_VERSION'))){
    & (Join-Path $pg 'initdb.exe') -D $cluster -U stock_test --auth=trust --encoding=UTF8 --locale=C
    if($LASTEXITCODE -ne 0){throw 'Cannot initialize isolated PostgreSQL.'}
}
& (Join-Path $pg 'pg_ctl.exe') -D $cluster status
if($LASTEXITCODE -ne 0){
    & (Join-Path $pg 'pg_isready.exe') -h 127.0.0.1 -p 55439 -q
    if($LASTEXITCODE -eq 0){throw 'Test port belongs to another cluster.'}
    Start-Process -FilePath (Join-Path $pg 'postgres.exe') -ArgumentList @('-D',('"'+$cluster+'"'),'-h','127.0.0.1','-p','55439') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $evidence 'postgres.out.log') -RedirectStandardError (Join-Path $evidence 'postgres.err.log')
    $deadline=[DateTime]::UtcNow.AddSeconds(15)
    do{ & (Join-Path $pg 'pg_isready.exe') -h 127.0.0.1 -p 55439 -q; if($LASTEXITCODE -eq 0){break}; Start-Sleep -Milliseconds 150 }while([DateTime]::UtcNow -lt $deadline)
    if($LASTEXITCODE -ne 0){throw 'Isolated PostgreSQL did not start.'}
}
$exists=& (Join-Path $pg 'psql.exe') -h 127.0.0.1 -p 55439 -U stock_test -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = 'lims_stock_test'"
if($exists -ne '1'){ & (Join-Path $pg 'createdb.exe') -h 127.0.0.1 -p 55439 -U stock_test lims_stock_test; if($LASTEXITCODE -ne 0){throw 'Cannot create isolated database.'} }
$env:LIMS_STOCK_TEST_CONNECTION='Host=127.0.0.1;Port=55439;Database=lims_stock_test;Username=stock_test;Pooling=false'
$env:LIMS_SOLVENT_UI_DIRECTORY=$evidence
$testProject=Join-Path $workspace 'Tests/Backend/Lims.Infrastructure.Tests/Lims.Infrastructure.Tests.csproj'
$filter='FullyQualifiedName~CommercialSolventCompositionTests|FullyQualifiedName~CommercialMaterialsPostgresTests|FullyQualifiedName~StockPostgresTests'
$ErrorActionPreference='Continue'
if($SkipBuild){dotnet test $testProject --no-build --no-restore -m:1 --filter $filter --logger 'trx;LogFileName=solvents-postgres.trx' --results-directory $evidence *> (Join-Path $evidence 'postgres-tests.log')}
else{dotnet test $testProject --no-restore -m:1 --filter $filter --logger 'trx;LogFileName=solvents-postgres.trx' --results-directory $evidence *> (Join-Path $evidence 'postgres-tests.log')}
$testExitCode=$LASTEXITCODE
$ErrorActionPreference='Stop'
if($testExitCode -ne 0){throw ('Tests failed. See '+(Join-Path $evidence 'postgres-tests.log'))}
Write-Output ('Tests passed; isolated database kept running for WinUI validation. Evidence: '+$evidence)
