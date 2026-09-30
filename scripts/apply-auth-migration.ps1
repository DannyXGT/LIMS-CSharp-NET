param(
    [string]$ConnectionStringEnvironmentVariable = 'LIMS_AUTH_MIGRATION_CONNECTION_STRING',
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sqlPath = Join-Path $projectRoot 'artifacts\sql\authentication.sql'
$expectedDatabase = 'InterDB'
$migrationId = '20260926045625_CreateAuthenticationSessions'

function Assert-SafeMigrationSql {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Authentication migration SQL was not found at $Path."
    }

    $sql = Get-Content -LiteralPath $Path -Raw
    $forbiddenStatements = [ordered]@{
        'DROP TABLE' = '(?im)^\s*DROP\s+TABLE\b'
        'TRUNCATE' = '(?im)^\s*TRUNCATE\b'
        'DELETE' = '(?im)^\s*DELETE\s+FROM\b'
        'UPDATE' = '(?im)^\s*UPDATE\b'
        'ALTER TABLE' = '(?im)^\s*ALTER\s+TABLE\b'
    }

    foreach ($entry in $forbiddenStatements.GetEnumerator()) {
        if ($sql -match $entry.Value) {
            throw "Unsafe statement '$($entry.Key)' was found in $Path."
        }
    }

    $allowedCreatedTables = @(
        '__EFMigrationsHistory',
        'auth_sessions',
        'auth_refresh_tokens'
    )
    $createTableMatches = [regex]::Matches(
        $sql,
        '(?im)^\s*CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?(?:public\.)?"?([A-Za-z_][A-Za-z0-9_]*)"?\s*\('
    )
    foreach ($match in $createTableMatches) {
        $createdTable = $match.Groups[1].Value
        if ($createdTable -cnotin $allowedCreatedTables) {
            throw "Unexpected CREATE TABLE for '$createdTable' was found in $Path."
        }
    }

    $insertMatches = [regex]::Matches(
        $sql,
        '(?im)^\s*INSERT\s+INTO\s+(?:public\.)?"?([A-Za-z_][A-Za-z0-9_]*)"?\b'
    )
    foreach ($match in $insertMatches) {
        if ($match.Groups[1].Value -cne '__EFMigrationsHistory') {
            throw "Unexpected INSERT INTO '$($match.Groups[1].Value)' was found in $Path."
        }
    }

    foreach ($requiredTable in @('auth_sessions', 'auth_refresh_tokens')) {
        if ($sql -notmatch "(?im)^\s*CREATE\s+TABLE\s+(?:public\.)?$requiredTable\s*\(") {
            throw "Required CREATE TABLE for $requiredTable was not found."
        }
    }

    if ($sql -notmatch '(?im)FOREIGN\s+KEY\s*\(user_id\)\s+REFERENCES\s+usuarios\s*\(id\)\s+ON\s+DELETE\s+RESTRICT') {
        throw 'The expected auth_sessions.user_id foreign key was not found.'
    }

    if ($sql -notmatch '(?im)FOREIGN\s+KEY\s*\(session_id\)\s+REFERENCES\s+auth_sessions\s*\(id\)\s+ON\s+DELETE\s+CASCADE') {
        throw 'The expected refresh-token session foreign key was not found.'
    }

    if ($sql -notmatch '(?im)^\s*START\s+TRANSACTION\s*;' -or
        $sql -notmatch '(?im)^\s*COMMIT\s*;') {
        throw 'The authentication migration must execute inside an explicit transaction.'
    }

    if ($sql -notmatch '(?im)^\s*SET\s+search_path\s+TO\s+public\s*;') {
        throw 'The authentication migration must explicitly target the public schema.'
    }

    $transactionIndex = $sql.IndexOf('START TRANSACTION', [StringComparison]::OrdinalIgnoreCase)
    $historyTableIndex = $sql.IndexOf('CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory"', [StringComparison]::OrdinalIgnoreCase)
    if ($transactionIndex -lt 0 -or $historyTableIndex -lt 0 -or $transactionIndex -gt $historyTableIndex) {
        throw 'The EF migrations history table must be created inside the transaction.'
    }
}

function Get-ConnectionValue {
    param(
        [System.Data.Common.DbConnectionStringBuilder]$Builder,
        [string[]]$Keys,
        [string]$DefaultValue = ''
    )

    foreach ($key in $Keys) {
        if ($Builder.ContainsKey($key)) {
            return [string]$Builder[$key]
        }
    }

    return $DefaultValue
}

function New-PsqlConnection {
    param([string]$ConnectionString)

    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $builder.ConnectionString = $ConnectionString

    $hostName = Get-ConnectionValue $builder @('Host', 'Server')
    $database = Get-ConnectionValue $builder @('Database', 'Initial Catalog')
    $username = Get-ConnectionValue $builder @('Username', 'User ID', 'UserId', 'User')
    $password = Get-ConnectionValue $builder @('Password')
    $port = Get-ConnectionValue $builder @('Port') '5432'
    $sslMode = Get-ConnectionValue $builder @('SSL Mode', 'SslMode')

    if ([string]::IsNullOrWhiteSpace($hostName) -or
        [string]::IsNullOrWhiteSpace($database) -or
        [string]::IsNullOrWhiteSpace($username)) {
        throw 'The connection string must contain Host, Database, and Username.'
    }

    return [pscustomobject]@{
        Arguments = @(
            '--host', $hostName,
            '--port', $port,
            '--username', $username,
            '--dbname', $database
        )
        Password = $password
        SslMode = $sslMode
    }
}

function ConvertTo-PsqlSslMode {
    param([string]$SslMode)

    switch ($SslMode.Replace(' ', '').ToLowerInvariant()) {
        'disable' { return 'disable' }
        'allow' { return 'allow' }
        'prefer' { return 'prefer' }
        'require' { return 'require' }
        'verifyca' { return 'verify-ca' }
        'verifyfull' { return 'verify-full' }
        default { return $null }
    }
}

Assert-SafeMigrationSql -Path $sqlPath

if ($ValidateOnly) {
    Write-Host 'authentication.sql passed the local safety validation. No database connection was opened.'
    return
}

$connectionString = [Environment]::GetEnvironmentVariable(
    $ConnectionStringEnvironmentVariable,
    [EnvironmentVariableTarget]::Process)
if ([string]::IsNullOrWhiteSpace($connectionString)) {
    throw "Environment variable $ConnectionStringEnvironmentVariable is required."
}

$psql = Get-Command 'psql' -ErrorAction SilentlyContinue
if (-not $psql) {
    throw 'psql was not found in PATH. Install the PostgreSQL command-line tools before continuing.'
}

$connection = New-PsqlConnection -ConnectionString $connectionString
$savedPgPassword = $env:PGPASSWORD
$savedPgSslMode = $env:PGSSLMODE

try {
    $env:PGPASSWORD = if ([string]::IsNullOrEmpty($connection.Password)) { $null } else { $connection.Password }

    $mappedSslMode = ConvertTo-PsqlSslMode ([string]$connection.SslMode)
    $env:PGSSLMODE = if ($mappedSslMode) { $mappedSslMode } else { $null }

    function Invoke-PsqlScalar {
        param([string]$Query)

        $psqlArguments = @(
            '-X',
            '--no-password',
            '--set', 'ON_ERROR_STOP=1',
            '--tuples-only',
            '--no-align'
        ) + $connection.Arguments + @('--command', $Query)

        $output = & $psql.Source @psqlArguments
        if ($LASTEXITCODE -ne 0) {
            throw "psql query failed with exit code $LASTEXITCODE."
        }

        return (($output | Out-String).Trim())
    }

    function Test-MigrationRecorded {
        $query = @'
SELECT CASE WHEN EXISTS (
    SELECT 1
    FROM public."__EFMigrationsHistory"
    WHERE "MigrationId" = '__MIGRATION_ID__'
) THEN '1' ELSE '0' END;
'@.Replace('__MIGRATION_ID__', $migrationId)
        return (Invoke-PsqlScalar $query) -eq '1'
    }

    $currentDatabase = Invoke-PsqlScalar 'SELECT current_database();'
    if (-not [string]::Equals($currentDatabase, $expectedDatabase, [StringComparison]::Ordinal)) {
        throw "Refusing to continue: connected database is '$currentDatabase', expected exactly '$expectedDatabase'."
    }

    $userIdType = Invoke-PsqlScalar @'
SELECT format_type(attribute.atttypid, attribute.atttypmod)
FROM pg_attribute AS attribute
JOIN pg_class AS relation ON relation.oid = attribute.attrelid
JOIN pg_namespace AS schema ON schema.oid = relation.relnamespace
WHERE schema.nspname = 'public'
  AND relation.relname = 'usuarios'
  AND attribute.attname = 'id'
  AND attribute.attnum > 0
  AND NOT attribute.attisdropped;
'@
    if ($userIdType -ne 'integer') {
        throw "Refusing to continue: public.usuarios.id type is '$userIdType', expected 'integer'."
    }

    $tableState = Invoke-PsqlScalar @'
SELECT
    CASE WHEN to_regclass('public.auth_sessions') IS NULL THEN '0' ELSE '1' END
    || '|'
    || CASE WHEN to_regclass('public.auth_refresh_tokens') IS NULL THEN '0' ELSE '1' END;
'@

    if ($tableState -eq '1|1') {
        Write-Host 'Authentication tables already exist in InterDB. No changes were made.'
        return
    }

    if ($tableState -ne '0|0') {
        throw "Refusing to continue: partial authentication schema detected ($tableState). Manual review is required."
    }

    $historyTableExists = Invoke-PsqlScalar @'
SELECT CASE WHEN to_regclass('public."__EFMigrationsHistory"') IS NULL THEN '0' ELSE '1' END;
'@
    if ($historyTableExists -eq '1' -and (Test-MigrationRecorded)) {
        throw "Refusing to continue: migration $migrationId is recorded, but its tables are missing."
    }

    Write-Host 'Applying authentication.sql to InterDB in its explicit transaction...'
    $applyArguments = @(
        '-X',
        '--no-password',
        '--set', 'ON_ERROR_STOP=1'
    ) + $connection.Arguments + @('--file', $sqlPath)

    & $psql.Source @applyArguments
    if ($LASTEXITCODE -ne 0) {
        throw "authentication.sql failed with exit code $LASTEXITCODE."
    }

    $verifiedState = Invoke-PsqlScalar @'
SELECT
    CASE WHEN to_regclass('public.auth_sessions') IS NULL THEN '0' ELSE '1' END
    || '|'
    || CASE WHEN to_regclass('public.auth_refresh_tokens') IS NULL THEN '0' ELSE '1' END;
'@
    if ($verifiedState -ne '1|1') {
        throw "Migration finished, but authentication table verification failed ($verifiedState)."
    }

    if (-not (Test-MigrationRecorded)) {
        throw "Migration finished, but $migrationId was not recorded."
    }

    Write-Host 'Authentication migration applied and verified successfully in InterDB.'
}
finally {
    $env:PGPASSWORD = $savedPgPassword
    $env:PGSSLMODE = $savedPgSslMode
}
