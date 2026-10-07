param(
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$evidence = Join-Path $workspace 'artifacts\validation\stock-postgres'
$cluster = [IO.Path]::GetFullPath((Join-Path $evidence 'data'))
if (-not $cluster.StartsWith($workspace + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Test cluster must be inside the workspace.' }
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $cluster 'PG_VERSION'))) {
    & (Join-Path $PostgresBin 'initdb.exe') -D $cluster -U stock_test --auth=trust --encoding=UTF8 --locale=C
    if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize isolated PostgreSQL test cluster.' }
}
& (Join-Path $PostgresBin 'pg_ctl.exe') -D $cluster status
$started = $LASTEXITCODE -ne 0
if ($started) {
    $port = Get-NetTCPConnection -LocalPort 55439 -State Listen -ErrorAction SilentlyContinue
    if ($port) { throw 'Test port 55439 is occupied by another server.' }
    Start-Process -FilePath (Join-Path $PostgresBin 'postgres.exe') -ArgumentList @('-D', ('"' + $cluster + '"'), '-h', '127.0.0.1', '-p', '55439') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $evidence 'server.out.log') -RedirectStandardError (Join-Path $evidence 'server.err.log')
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        & (Join-Path $PostgresBin 'pg_isready.exe') -h 127.0.0.1 -p 55439 -U stock_test -q
        if ($LASTEXITCODE -eq 0) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($LASTEXITCODE -ne 0) { throw 'The isolated PostgreSQL test server did not start.' }
}
$previousConnection = $env:LIMS_STOCK_TEST_CONNECTION
try {
    $exists = & (Join-Path $PostgresBin 'psql.exe') -h 127.0.0.1 -p 55439 -U stock_test -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = 'lims_stock_test'"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the isolated test database.' }
    if ($exists -ne '1') {
        & (Join-Path $PostgresBin 'createdb.exe') -h 127.0.0.1 -p 55439 -U stock_test lims_stock_test
        if ($LASTEXITCODE -ne 0) { throw 'Cannot create isolated test database.' }
    }
    $env:LIMS_STOCK_TEST_CONNECTION = 'Host=127.0.0.1;Port=55439;Database=lims_stock_test;Username=stock_test;Pooling=false'
    if (-not $SkipBuild) {
        dotnet build (Join-Path $workspace 'Lims.sln') --no-restore -c Debug -p:Platform=x64 -m:1
        if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    }
    dotnet test (Join-Path $workspace 'Lims.sln') --no-build -c Debug -p:Platform=x64 -m:1 --logger "trx;LogFilePrefix=stock" --results-directory (Join-Path $evidence 'results')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
} finally {
    $env:LIMS_STOCK_TEST_CONNECTION = $previousConnection
    if ($started) {
        & (Join-Path $PostgresBin 'pg_ctl.exe') -D $cluster -m fast -w stop
    }
}
