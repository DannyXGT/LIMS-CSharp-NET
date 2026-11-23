param([switch]$Install)
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runtime=Get-Content (Join-Path $workspace 'artifacts/planificacion-dev/runtime.json') -Raw|ConvertFrom-Json
if($runtime.Host -ne '127.0.0.1' -or $runtime.Port -ne 55441 -or $runtime.Database -ne 'lims_planning_dev'){throw 'Se rechaza cualquier destino distinto de la copia local.'}
$sql=Join-Path $workspace 'scripts/planning/install-html-react-integration.sql'
if(-not $Install){Write-Output 'Solo preparado. Instalar requiere -Install y autorizacion del usuario para las ampliaciones.';exit 0}
$evidence=Join-Path $workspace 'artifacts/html-react-integration'
New-Item -ItemType Directory -Path $evidence -Force|Out-Null
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss'
$backup=Join-Path $evidence ('pre-install-'+$stamp+'.backup')
$saved=$env:PGPASSWORD;$env:PGPASSWORD=$runtime.Password
$query=@'
SELECT json_build_object(
  'activeRows',(SELECT count(*) FROM public.planificacion_activa),
  'catalogueRows',(SELECT count(*) FROM public.planificacion_test_map),
  'episodes',(SELECT count(*) FROM planning.episodes),
  'executions',(SELECT count(*) FROM planning.executions),
  'history',(SELECT count(*) FROM planning.history),
  'activeHash',(SELECT md5(string_agg((to_jsonb(a)-'manual_pinned')::text,'' ORDER BY a.id)) FROM public.planificacion_activa a),
  'catalogueHash',(SELECT md5(string_agg(to_jsonb(m)::text,'' ORDER BY m.id)) FROM public.planificacion_test_map m),
  'executionHash',(SELECT md5(string_agg(to_jsonb(x)::text,'' ORDER BY x.id)) FROM planning.executions x))::text;
'@
try {
    $psql=Join-Path $runtime.PostgresBin 'psql.exe'
    $before=& $psql -h 127.0.0.1 -p 55441 -U $runtime.Username -d lims_planning_dev -X -A -t -v ON_ERROR_STOP=1 -c $query
    if($LASTEXITCODE -ne 0){throw 'Fallo la inspeccion previa.'}
    & (Join-Path $runtime.PostgresBin 'pg_dump.exe') -h 127.0.0.1 -p 55441 -U $runtime.Username -d lims_planning_dev -Fc --no-owner --no-privileges -f $backup
    if($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $backup)){throw 'Fallo el respaldo. No se instala.'}
    & $psql -h 127.0.0.1 -p 55441 -U $runtime.Username -d lims_planning_dev -X -v ON_ERROR_STOP=1 -f $sql
    if($LASTEXITCODE -ne 0){throw 'Fallo la instalacion transaccional. Revise el error SQL.'}
    $after=& $psql -h 127.0.0.1 -p 55441 -U $runtime.Username -d lims_planning_dev -X -A -t -v ON_ERROR_STOP=1 -c $query
    if($LASTEXITCODE -ne 0){throw 'Fallo la inspeccion posterior.'}
    if(($before -join '') -ne ($after -join '')){throw 'La inspeccion detecto cambios de datos: no se acredita conservacion; revise antes de importar.'}
    $report=@{Boundary='Isolated local schema installation only; no imports or production connection';Database='127.0.0.1:55441/lims_planning_dev';Before=($before|ConvertFrom-Json);After=($after|ConvertFrom-Json);Backup=$backup;BackupSha256=(Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash;SourceValuesPreserved=$true;Utc=[DateTime]::UtcNow.ToString('O')}
    $report|ConvertTo-Json -Depth 8|Set-Content (Join-Path $evidence 'installation.json') -Encoding UTF8
    Write-Output 'Esquema instalado en copia local. Conteos y huellas de datos anteriores conservados.'
} finally {$env:PGPASSWORD=$saved}
