$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$local=Join-Path $workspace 'artifacts/planificacion-dev'
$runtime=Get-Content (Join-Path $local 'runtime.json') -Raw|ConvertFrom-Json
if($runtime.Host -ne '127.0.0.1' -or $runtime.Port -ne 55441 -or $runtime.Database -ne 'lims_planning_dev'){throw 'Destino no autorizado'}
$savedPassword=$env:PGPASSWORD;$savedConnection=$env:LIMS_DB_CONNECTION_STRING
try{
    $env:PGPASSWORD=$runtime.Password
    $bin=$runtime.PostgresBin;if(-not $bin){$bin='C:/Program Files/PostgreSQL/18/bin'}
    $backup=Join-Path $local ('pre-estado-importacion-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'.backup')
    & (Join-Path $bin 'pg_dump.exe') -h 127.0.0.1 -p 55441 -U $runtime.Username -d lims_planning_dev -n planning -Fc -f $backup
    if($LASTEXITCODE -ne 0){throw 'No se pudo respaldar Planificación'}
    & (Join-Path $bin 'psql.exe') -h 127.0.0.1 -p 55441 -U $runtime.Username -d lims_planning_dev -X -A -t -c "SELECT json_build_object('episodes',count(*),'executions',(SELECT count(*) FROM planning.executions),'manual',(SELECT count(*) FROM planning.executions WHERE origin='Manual'),'notes',(SELECT count(*) FROM planning.executions WHERE observation IS NOT NULL OR responsible IS NOT NULL),'episodeIds',(SELECT json_agg(id) FROM planning.episodes),'executionIds',(SELECT json_agg(id) FROM planning.executions)) FROM planning.episodes" -o (Join-Path $local 'pre-estado-importacion-baseline.json')
    if($LASTEXITCODE -ne 0){throw 'No se pudo registrar la línea base'}
    $env:LIMS_DB_CONNECTION_STRING="Host=127.0.0.1;Port=55441;Database=lims_planning_dev;Username=$($runtime.Username);Password=$($runtime.Password)"
    dotnet tool run dotnet-ef database update --project Backend/Lims.Infrastructure --startup-project Backend/Lims.Infrastructure --no-build
    if($LASTEXITCODE -ne 0){throw 'Falló la migración aditiva'}
    Write-Output ('Respaldo local: '+$backup)
}finally{$env:PGPASSWORD=$savedPassword;$env:LIMS_DB_CONNECTION_STRING=$savedConnection}
