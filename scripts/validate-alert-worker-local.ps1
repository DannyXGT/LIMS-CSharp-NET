param([string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin')
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$cluster = Join-Path $workspace 'artifacts\validation\stock-postgres\data'
$evidence = Join-Path $workspace 'artifacts\validation\alerts-functional'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$worker = Join-Path $workspace 'Backend\Lims.AlertWorker\bin\Debug\net10.0\Lims.AlertWorker.exe'
if (-not (Test-Path -LiteralPath $worker)) { throw 'Compile Lims.AlertWorker en Debug antes de validar.' }
if (-not (Test-Path -LiteralPath (Join-Path $cluster 'PG_VERSION'))) { throw 'Falta el clúster aislado existente.' }
& (Join-Path $PostgresBin 'pg_ctl.exe') -D $cluster status
$started = $LASTEXITCODE -ne 0
if ($started) {
    if (Get-NetTCPConnection -LocalPort 55439 -State Listen -ErrorAction SilentlyContinue) { throw 'Puerto de pruebas ocupado.' }
    Start-Process -FilePath (Join-Path $PostgresBin 'postgres.exe') -ArgumentList @('-D', ('"' + $cluster + '"'), '-h', '127.0.0.1', '-p', '55439') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $evidence 'worker-pg.out.log') -RedirectStandardError (Join-Path $evidence 'worker-pg.err.log')
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do { & (Join-Path $PostgresBin 'pg_isready.exe') -h 127.0.0.1 -p 55439 -U stock_test -d lims_stock_test -q; if ($LASTEXITCODE -eq 0) { break }; Start-Sleep -Milliseconds 100 } while ([DateTime]::UtcNow -lt $deadline)
    if ($LASTEXITCODE -ne 0) { throw 'No arrancó PostgreSQL aislado.' }
}
function Query([string]$sql) {
    $result = & (Join-Path $PostgresBin 'psql.exe') -X -h 127.0.0.1 -p 55439 -U stock_test -d lims_stock_test -v ON_ERROR_STOP=1 -At -c $sql
    if ($LASTEXITCODE -ne 0) { throw 'Falló la consulta al clúster aislado.' }; return ($result -join "`n").Trim()
}
$oldConnection = $env:ConnectionStrings__Lims; $oldLog = $env:Alerts__LogDirectory
try {
    # Reuse a material created by the existing isolated suite; this script never creates materials.
    $schema = Query "SELECT nspname FROM pg_namespace WHERE nspname ~ '^stock_[0-9a-f]{32}$' ORDER BY oid DESC LIMIT 1"
    if ($schema -notmatch '^stock_[0-9a-f]{32}$') { throw 'Ejecute primero test-alerts.ps1.' }
    $env:ConnectionStrings__Lims = "Host=127.0.0.1;Port=55439;Database=lims_stock_test;Username=stock_test;Pooling=false;Search Path=$schema"
    $env:Alerts__LogDirectory = Join-Path $evidence 'worker-logs'
    $balanceSql = "SELECT md5(string_agg(row_to_json(m)::text, ',' ORDER BY m.id)) FROM $schema.reference_materials m"
    $before = Query $balanceSql
    $runs = @()
    foreach ($cycle in 1..2) {
        $output = & $worker --evaluate-only
        if ($LASTEXITCODE -ne 0) { throw "Falló el ciclo $cycle del ejecutor aislado." }
        $event = $output | Where-Object { $_ -like '{*' } | Select-Object -Last 1 | ConvertFrom-Json
        if (-not $event.Result.Acquired) { throw 'Otro ejecutor mantiene el bloqueo.' }
        $runs += $event
    }
    $after = Query $balanceSql
    if ($before -ne $after) { throw 'El ejecutor alteró inventario.' }
    if ($runs[1].Result.Changed -ne 0) { throw 'La reevaluación no fue idempotente.' }
    $report = [ordered]@{ boundary='Isolated PostgreSQL with existing test material; no production or real SMTP'; schema=$schema; runs=$runs; inventoryUnchanged=($before -eq $after); smtpDispatchEnabled=$false }
    $report | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $evidence 'worker-controlled.json')
    $report | ConvertTo-Json -Depth 8
} finally {
    $env:ConnectionStrings__Lims=$oldConnection; $env:Alerts__LogDirectory=$oldLog
    if ($started) { & (Join-Path $PostgresBin 'pg_ctl.exe') -D $cluster -m fast -w stop }
}
