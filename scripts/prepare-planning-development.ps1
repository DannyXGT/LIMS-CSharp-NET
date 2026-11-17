param(
    [Parameter(Mandatory=$true)][string]$BackupPath,
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin'
)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$local = [IO.Path]::GetFullPath((Join-Path $workspace 'artifacts\planificacion-dev'))
if (-not $local.StartsWith($workspace + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'El clúster debe permanecer dentro del repositorio.' }
$archive = (Resolve-Path -LiteralPath $BackupPath).Path
$archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
$runtimePath = Join-Path $local 'runtime.json'
$data = Join-Path $local 'pgdata'
$port = 55441
$database = 'lims_planning_dev'
$pgUser = 'lims_planning_local'
New-Item -ItemType Directory -Path $local -Force | Out-Null
if (Test-Path -LiteralPath $runtimePath) {
    $existing = Get-Content -LiteralPath $runtimePath -Raw | ConvertFrom-Json
    if ($existing.BackupSha256 -ne $archiveHash -or -not $existing.Restored) { throw 'Configuración existente distinta o restauración incompleta. Revise manualmente; no se sobrescribe.' }
    Write-Output 'La copia aislada ya existe. No se modificó.'
    exit 0
}
if (Test-Path -LiteralPath $data) { throw 'Ya existe pgdata. No se reemplaza ni se elimina.' }
if (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) { throw 'El puerto local 55441 ya está ocupado.' }
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $randomBytes = New-Object byte[] 48
    $rng.GetBytes($randomBytes); $password = [Convert]::ToBase64String($randomBytes)
    $rng.GetBytes($randomBytes); $jwt = [Convert]::ToBase64String($randomBytes)
} finally { $rng.Dispose() }
$passwordFile = Join-Path $local 'init-password.tmp'
[IO.File]::WriteAllText($passwordFile, $password)
try {
    & (Join-Path $PostgresBin 'initdb.exe') -D $data -U $pgUser -A scram-sha-256 --encoding=UTF8 --locale=C --pwfile=$passwordFile
    if ($LASTEXITCODE -ne 0) { throw 'initdb falló.' }
} finally { Remove-Item -LiteralPath $passwordFile -ErrorAction SilentlyContinue }
$runtime = @{Host='127.0.0.1';Port=$port;Database=$database;Username=$pgUser;Password=$password;SigningKey=$jwt;BackupSha256=$archiveHash;BackupPath=$archive;Restored=$false;PostgresBin=$PostgresBin}
$runtime | ConvertTo-Json | Set-Content -LiteralPath $runtimePath -Encoding UTF8
$process = Start-Process -FilePath (Join-Path $PostgresBin 'pg_ctl.exe') -ArgumentList @('start','-D',('"'+$data+'"'),'-l',('"'+(Join-Path $local 'postgres.log')+'"'),'-o',('"-h 127.0.0.1 -p '+$port+'"'),'-w','-t','15') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $local 'pg-start.log') -RedirectStandardError (Join-Path $local 'pg-start-error.log')
$process.WaitForExit()
& (Join-Path $PostgresBin 'pg_ctl.exe') status -D $data
if ($LASTEXITCODE -ne 0) { throw 'El clúster aislado no arrancó.' }
$oldPassword = $env:PGPASSWORD
$env:PGPASSWORD = $password
try {
    & (Join-Path $PostgresBin 'createdb.exe') -h 127.0.0.1 -p $port -U $pgUser $database
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo crear la base nueva.' }
    & (Join-Path $PostgresBin 'pg_restore.exe') -h 127.0.0.1 -p $port -U $pgUser -d $database --no-owner --no-privileges --exit-on-error --single-transaction $archive
    if ($LASTEXITCODE -ne 0) { throw 'Falló la restauración transaccional. No se inicia la API.' }
    $runtime.Restored=$true
    $runtime | ConvertTo-Json | Set-Content -LiteralPath $runtimePath -Encoding UTF8
    & (Join-Path $PostgresBin 'psql.exe') -h 127.0.0.1 -p $port -U $pgUser -d $database -X -v ON_ERROR_STOP=1 -c "SELECT current_database() AS development_database, count(*) AS public_tables FROM information_schema.tables WHERE table_schema='public';"
    if ($LASTEXITCODE -ne 0) { throw 'Falló la comprobación de la restauración.' }
} finally { $env:PGPASSWORD=$oldPassword }
Write-Output 'Restauración local terminada. Secretos en artifacts/planificacion-dev/runtime.json, excluido de Git. No se aplicaron migraciones ni se conectó a producción.'
