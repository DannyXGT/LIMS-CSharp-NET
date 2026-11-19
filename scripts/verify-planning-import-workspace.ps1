$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$local=Join-Path $root 'artifacts/planificacion-dev';$out=Join-Path $local 'centro-operativo'
$r=Get-Content (Join-Path $local 'runtime.json') -Raw|ConvertFrom-Json
if($r.Host -ne '127.0.0.1' -or $r.Port -ne 55441 -or $r.Database -ne 'lims_planning_dev'){throw 'Destino no autorizado'}
$baseline=Get-Content (Join-Path $out 'baseline-private.json') -Raw|ConvertFrom-Json
$episodes=($baseline.episodeIds|ForEach-Object{[guid]$id=$_;"'$id'::uuid"}) -join ','
$executions=($baseline.executionIds|ForEach-Object{[guid]$id=$_;"'$id'::uuid"}) -join ','
$sql="SELECT json_build_object('preservedEpisodes',(SELECT count(*) FROM planning.episodes WHERE id=ANY(ARRAY[$episodes])),'preservedExecutions',(SELECT count(*) FROM planning.executions WHERE id=ANY(ARRAY[$executions])),'episodes',(SELECT count(*) FROM planning.episodes),'executions',(SELECT count(*) FROM planning.executions),'manual',(SELECT count(*) FROM planning.executions WHERE origin='Manual'),'summaryPending',(SELECT count(*) FROM planning.incidents WHERE kind='SummaryWithoutReport' AND state<>'Resolved'),'summaryResolved',(SELECT count(*) FROM planning.incidents WHERE kind='SummaryWithoutReport' AND state='Resolved'));"
$sqlPath=Join-Path $out 'verify-preservation-private.sql';[IO.File]::WriteAllText($sqlPath,$sql,[Text.UTF8Encoding]::new($false));$saved=$env:PGPASSWORD
try{$env:PGPASSWORD=$r.Password;$raw=& 'C:/Program Files/PostgreSQL/18/bin/psql.exe' -h 127.0.0.1 -p 55441 -U $r.Username -d lims_planning_dev -X -A -t -f $sqlPath;if($LASTEXITCODE){throw 'Consulta de conservación fallida'};$preserved=$raw|ConvertFrom-Json}finally{$env:PGPASSWORD=$saved}
if($preserved.preservedEpisodes -ne $baseline.episodeIds.Count -or $preserved.preservedExecutions -ne $baseline.executionIds.Count){throw 'Se perdieron UUID originales'}
$scenarios=@();foreach($letter in @('A','B','C','D','E','F')){$file=Join-Path $out ('scenario-'+$letter+'.json');if(-not (Test-Path $file)){throw ('Falta escenario '+$letter)};$scenario=Get-Content $file -Raw|ConvertFrom-Json;if(-not $scenario.Passed){throw $letter};$scenarios+=$scenario}
$interactions=Get-Content (Join-Path $out 'scenario-Interactions.json') -Raw|ConvertFrom-Json;if(-not $interactions.Passed){throw 'Faltan interacciones nativas'}
$v=Get-Content (Join-Path $local 'desktop-validation.json') -Raw|ConvertFrom-Json
$build=Get-FileHash (Join-Path $root 'Frontend/Lims.Desktop/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/Lims.Desktop.dll')
$staged=Get-FileHash (Join-Path $v.Stage 'Lims.Desktop.dll');$executed=Get-FileHash (Join-Path $v.Stage 'AppX/Lims.Desktop.dll')
if($build.Hash -ne $staged.Hash -or $build.Hash -ne $executed.Hash){throw 'El cliente ejecutado no corresponde a la compilación x64 normal'}
@{Boundary='Actual native WinUI and original TIPS XLSX; PostgreSQL 127.0.0.1:55441/lims_planning_dev only';Preservation=$preserved;Scenarios=$scenarios;NativeInteractions=$interactions;DesktopSha256=$build.Hash;StageMatches=$true;ExecutedAppXMatches=$true;ApiSha256=(Get-FileHash (Join-Path $root 'Backend/Lims.Api/bin/Debug/net10.0/Lims.Api.dll')).Hash}|ConvertTo-Json -Depth 12|Set-Content (Join-Path $out 'acceptance-final.json') -Encoding UTF8
'PASS: seis escenarios reales, todos los UUID anteriores y compilación x64 ejecutada verificados.'
