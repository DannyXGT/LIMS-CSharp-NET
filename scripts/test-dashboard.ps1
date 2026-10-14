param([string]$PostgresBin='C:\Program Files\PostgreSQL\18\bin')
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$cluster=[IO.Path]::GetFullPath((Join-Path $workspace 'artifacts\validation\stock-postgres\data'))
if(-not $cluster.StartsWith($workspace+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Cluster fuera del workspace' }
if(-not (Test-Path (Join-Path $cluster 'PG_VERSION'))) { throw 'Provisionar primero con scripts/test-stock.ps1' }
& (Join-Path $PostgresBin 'pg_ctl.exe') -D $cluster status
$started=$LASTEXITCODE -ne 0
if($started) {
    if(Get-NetTCPConnection -LocalPort 55439 -State Listen -ErrorAction SilentlyContinue) { throw 'Puerto 55439 ocupado por otro proceso' }
    Start-Process -FilePath (Join-Path $PostgresBin 'postgres.exe') -ArgumentList @('-D',('"'+$cluster+'"'),'-h','127.0.0.1','-p','55439') -WindowStyle Hidden
    $deadline=[DateTime]::UtcNow.AddSeconds(15)
    do { & (Join-Path $PostgresBin 'pg_isready.exe') -h 127.0.0.1 -p 55439 -U stock_test -q; if($LASTEXITCODE -eq 0) { break }; Start-Sleep -Milliseconds 100 } while([DateTime]::UtcNow -lt $deadline)
    if($LASTEXITCODE -ne 0) { throw 'No inició PostgreSQL aislado' }
}
$previous=$env:LIMS_STOCK_TEST_CONNECTION
try {
    $env:LIMS_STOCK_TEST_CONNECTION='Host=127.0.0.1;Port=55439;Database=lims_stock_test;Username=stock_test;Pooling=false'
    dotnet test (Join-Path $workspace 'Lims.sln') --no-build -c Debug -p:Platform=x64 -m:1 --filter 'FullyQualifiedName~Dashboard' --logger 'trx;LogFilePrefix=dashboard' --results-directory (Join-Path $workspace 'artifacts\validation\dashboard\tests')
    if($LASTEXITCODE -ne 0) { throw 'Pruebas del dashboard fallaron' }
} finally {
    $env:LIMS_STOCK_TEST_CONNECTION=$previous
    if($started) { & (Join-Path $PostgresBin 'pg_ctl.exe') -D $cluster -m fast -w stop }
}
