param([string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin', [string]$Filter = 'FullyQualifiedName~AlertPostgresTests')
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
# Reuse the existing, guarded test cluster only; production configuration is never read.
$cluster = Join-Path $workspace 'artifacts\validation\stock-postgres\data'
$evidence = Join-Path $workspace 'artifacts\validation\alerts-postgres'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $cluster 'PG_VERSION'))) { throw 'Primero provisione el clúster de prueba mediante scripts/test-stock.ps1.' }
& (Join-Path $PostgresBin 'pg_ctl.exe') -D $cluster status
$started = $LASTEXITCODE -ne 0
if ($started) {
    if (Get-NetTCPConnection -LocalPort 55439 -State Listen -ErrorAction SilentlyContinue) { throw 'El puerto de prueba está ocupado.' }
    Start-Process -FilePath (Join-Path $PostgresBin 'postgres.exe') -ArgumentList @('-D', ('"' + $cluster + '"'), '-h', '127.0.0.1', '-p', '55439') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $evidence 'server.out.log') -RedirectStandardError (Join-Path $evidence 'server.err.log')
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do { & (Join-Path $PostgresBin 'pg_isready.exe') -h 127.0.0.1 -p 55439 -U stock_test -q; if ($LASTEXITCODE -eq 0) { break }; Start-Sleep -Milliseconds 100 } while ([DateTime]::UtcNow -lt $deadline)
    if ($LASTEXITCODE -ne 0) { throw 'No arrancó PostgreSQL de prueba.' }
}
$previous = $env:LIMS_STOCK_TEST_CONNECTION
try {
    $env:LIMS_STOCK_TEST_CONNECTION = 'Host=127.0.0.1;Port=55439;Database=lims_stock_test;Username=stock_test;Pooling=false'
    dotnet test (Join-Path $workspace 'Tests\Backend\Lims.Infrastructure.Tests\Lims.Infrastructure.Tests.csproj') --no-restore -m:1 --filter $Filter --logger 'trx;LogFileName=alerts-postgres.trx' --results-directory $evidence
    if ($LASTEXITCODE -ne 0) { throw 'Falló la validación de Alertas en PostgreSQL aislado.' }
} finally {
    $env:LIMS_STOCK_TEST_CONNECTION = $previous
    if ($started) { & (Join-Path $PostgresBin 'pg_ctl.exe') -D $cluster -m fast -w stop }
}
