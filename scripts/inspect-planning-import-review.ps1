$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$local=Join-Path $root 'artifacts/planificacion-dev'
$runtime=Get-Content (Join-Path $local 'runtime.json') -Raw|ConvertFrom-Json
if($runtime.Host -ne '127.0.0.1' -or $runtime.Port -ne 55441 -or $runtime.Database -ne 'lims_planning_dev'){throw 'Solo base local aislada'}
$out=Join-Path $local 'centro-operativo';New-Item -ItemType Directory -Path $out -Force|Out-Null
$sql=@'
SELECT json_build_object(
 'incidents',(SELECT json_agg(t) FROM (SELECT kind,state,count(*) total,count(DISTINCT episode_id) episodes FROM planning.incidents GROUP BY kind,state ORDER BY kind,state)t),
 'summaryTray',(SELECT json_build_object('pending',count(DISTINCT i.id) FILTER(WHERE i.state<>'Resolved'),'resolved',count(DISTINCT i.id) FILTER(WHERE i.state='Resolved'),'distinctEpisodes',count(DISTINCT i.episode_id),'summarySourceBatches',count(DISTINCT s.batch_id)) FROM planning.incidents i LEFT JOIN planning.source_rows s ON s.episode_id=i.episode_id AND s.file='Summary' WHERE i.kind='SummaryWithoutReport'),
 'sourceCoverage',(SELECT json_agg(t) FROM (SELECT b.id,b.day,b.cut_window,b.status,count(*) FILTER(WHERE s.file='Summary') summary_rows,count(*) FILTER(WHERE s.file='Report List') report_rows FROM planning.batches b LEFT JOIN planning.source_rows s ON s.batch_id=b.id WHERE b.status='Applied' GROUP BY b.id ORDER BY b.applied_at)t),
 'invariants',json_build_object('episodes',(SELECT count(*) FROM planning.episodes),'executions',(SELECT count(*) FROM planning.executions),'manual',(SELECT count(*) FROM planning.executions WHERE origin='Manual'),'summaryPendingWithReport',(SELECT count(*) FROM planning.incidents i WHERE i.kind='SummaryWithoutReport' AND i.state<>'Resolved' AND EXISTS(SELECT 1 FROM planning.source_rows s WHERE s.episode_id=i.episode_id AND s.file='Report List' AND s.execution_id IS NOT NULL))),
 'unclassified',(SELECT count(*) FROM planning.executions WHERE equivalence IS NULL AND NOT no_program),
 'unlinkedReportRows',(SELECT count(*) FROM planning.source_rows WHERE file='Report List' AND execution_id IS NULL));
'@
$path=Join-Path $out 'inspection.sql';[IO.File]::WriteAllText($path,$sql,[Text.UTF8Encoding]::new($false))
$saved=$env:PGPASSWORD
try{$env:PGPASSWORD=$runtime.Password;$result=& 'C:/Program Files/PostgreSQL/18/bin/psql.exe' -h 127.0.0.1 -p 55441 -U $runtime.Username -d lims_planning_dev -X -A -t -f $path;if($LASTEXITCODE){throw 'Consulta fallida'};$result|Set-Content (Join-Path $out 'inspection-before.json') -Encoding UTF8;$result}finally{$env:PGPASSWORD=$saved}
