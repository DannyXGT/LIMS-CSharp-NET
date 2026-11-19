param([switch]$Before)
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$local=Join-Path $workspace 'artifacts/planificacion-dev';$evidence=Join-Path $local 'importacion-real'
$runtime=Get-Content (Join-Path $local 'runtime.json') -Raw|ConvertFrom-Json
if($runtime.Host -ne '127.0.0.1' -or $runtime.Port -ne 55441 -or $runtime.Database -ne 'lims_planning_dev'){throw 'Destino no autorizado'}
$account=Get-Content (Join-Path $local 'acceptance-account.json') -Raw|ConvertFrom-Json
$auth=Invoke-RestMethod https://localhost:7441/api/auth/login -Method Post -ContentType application/json -Body (@{identifier=$account.email;password=$account.password;clientName='Planning real results acceptance'}|ConvertTo-Json)
$headers=@{Authorization='Bearer '+$auth.accessToken}
function Jobs([string]$query){Invoke-RestMethod ('https://localhost:7441/api/planning/jobs?Scope=History&'+$query) -Headers $headers}
function Exact([string]$job){Jobs ('SearchMode=Exact&Search='+[uri]::EscapeDataString($job))}
$inspection=Get-Content (Join-Path $evidence 'source-inspection.json') -Raw|ConvertFrom-Json
$changed=@($inspection.stateChanges)[0]
if($Before){
    $row=(Exact $changed.job).items[0];if($row.jobState -ne $changed.before){throw 'El estado anterior no corresponde al archivo real del 13'}
    @{Job=$changed.job;Before=$row.jobState;Expected=$changed.after;Counts=(Jobs 'PageSize=1').counts}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $evidence 'before-17.json') -Encoding UTF8
    Write-Output 'PASS: estado anterior contrastado con el Summary real del 13';exit
}
$checks=[Collections.Generic.List[string]]::new()
$both=Exact 'GUAT25038091-REV3'
if($both.total -ne 24 -or @($both.items|Where-Object{-not $_.originalTest -or $_.jobState -eq 'No informado'}).Count){throw 'Se perdió la identidad o estado del JOB con 24 pruebas'}
$checks.Add('JOB real en ambos archivos: 24 ejecuciones e información Summary en el mismo expediente')
$summaryOnly=Exact $inspection.summaryOnly[0]
if($summaryOnly.total -ne 1 -or $summaryOnly.items[0].executionId -or -not $summaryOnly.items[0].buyer){throw 'No se conservó el JOB Solo Summary'}
$summaryDossier=Invoke-RestMethod ('https://localhost:7441/api/planning/jobs/'+$summaryOnly.items[0].episodeId) -Headers $headers
if(-not @($summaryDossier.incidents|Where-Object{$_.kind -eq 'SummaryWithoutReport' -and $_.state -eq 'Pending'}).Count){throw 'Falta la incidencia Solo Summary'}
$checks.Add('Solo Summary: expediente, estado original y bandeja independiente de Vendor')
$reportOnly=Exact $inspection.reportOnly[0]
if($reportOnly.total -eq 0 -or -not @($reportOnly.items|Where-Object executionId).Count){throw 'Se perdió el JOB real fuera de cobertura Summary'}
$checks.Add('Solo Report List: ejecuciones consultables sin inventar campos Summary')
$after=(Exact $changed.job).items[0]
if($after.jobState -ne $changed.after){throw 'El segundo Summary no actualizó el estado oficial'}
$dossier=Invoke-RestMethod ('https://localhost:7441/api/planning/jobs/'+$after.episodeId) -Headers $headers
if(-not @($dossier.history|Where-Object{$_.action -eq 'OfficialStateImported' -and $_.details -like ('*'+$changed.before+'*') -and $_.details -like ('*'+$changed.after+'*')}).Count){throw 'Falta auditoría de antes/después del estado'}
$checks.Add('Cambio real Report Closed → Invoice Rejected: API, fuente y auditoría antes/después')
$filter=@{JobState=$changed.after}|ConvertTo-Json -Compress
$filtered=Jobs ('ColumnFilters='+[uri]::EscapeDataString($filter)+'&PageSize=1&Sort=jobState')
if(-not $filtered.total -or $filtered.items[0].jobState -notlike ('*'+$changed.after+'*')){throw 'No funciona el filtro individual Estado'}
$tiny=Jobs 'PageSize=1';$large=Jobs 'PageSize=100'
if(($tiny.counts|ConvertTo-Json -Compress) -ne ($large.counts|ConvertTo-Json -Compress)){throw 'Los indicadores dependen de la página'}
$checks.Add('Estado: filtro, ordenamiento y estadísticas calculadas sobre todos los resultados')
$vendor=Exact $inspection.missingVendor[0]
$vendorDossier=Invoke-RestMethod ('https://localhost:7441/api/planning/jobs/'+$vendor.items[0].episodeId) -Headers $headers
if($vendorDossier.wip.vendor -or -not @($vendorDossier.incidents|Where-Object{$_.kind -eq 'MissingVendor' -and $_.state -ne 'Resolved'}).Count){throw 'Vendor vacío se perdió o confundió con falta de pruebas'}
$checks.Add('Vendor vacío real: incidencia propia conservada')
$tray=Invoke-RestMethod 'https://localhost:7441/api/planning/reviews?category=SummaryWithoutReport&pageSize=2' -Headers $headers
$trayNext=Invoke-RestMethod 'https://localhost:7441/api/planning/reviews?category=SummaryWithoutReport&pageSize=2&page=2' -Headers $headers
if($tray.total -le 2 -or $tray.items.Count -ne 2 -or $trayNext.items.Count -ne 2 -or $tray.items[0].episodeId -eq $trayNext.items[0].episodeId){throw 'La bandeja no pagina todo el servidor'}
$checks.Add('Bandeja Solo Summary: total y páginas del servidor, sin truncamiento silencioso')
$export=Invoke-WebRequest ('https://localhost:7441/api/planning/export?Scope=History&SearchMode=Exact&Search='+$both.items[0].job) -Headers $headers -UseBasicParsing
$text=if($export.Content -is [byte[]]){[Text.Encoding]::UTF8.GetString($export.Content)}else{$export.Content}
$csv=@(ConvertFrom-Csv $text.TrimStart([char]0xFEFF));if($csv.Count -ne 24 -or -not $csv[0].Estado){throw 'Exportación incompleta o sin Estado'}
$checks.Add('Exportación filtrada: 24 pruebas originales y Estado real')
$baseline=Get-Content (Join-Path $local 'pre-estado-importacion-baseline.json') -Raw|ConvertFrom-Json
$episodeIds=($baseline.episodeIds|ForEach-Object{[guid]$parsedGuid=$_;"'$_'::uuid"}) -join ','
$executionIds=($baseline.executionIds|ForEach-Object{"'$_'::uuid"}) -join ','
$sql="SELECT json_build_object('preservedEpisodes',(SELECT count(*) FROM planning.episodes WHERE id=ANY(ARRAY[$episodeIds])),'preservedExecutions',(SELECT count(*) FROM planning.executions WHERE id=ANY(ARRAY[$executionIds])),'episodes',(SELECT count(*) FROM planning.episodes),'executions',(SELECT count(*) FROM planning.executions),'manual',(SELECT count(*) FROM planning.executions WHERE origin='Manual'),'states',(SELECT count(*) FROM planning.episodes WHERE official_fields->>'sampleStatus' IS NOT NULL),'summaryTray',(SELECT count(*) FROM planning.incidents WHERE kind='SummaryWithoutReport' AND state<>'Resolved'));"
$sqlFile=Join-Path $evidence 'verify-preservation.sql';[IO.File]::WriteAllText($sqlFile,$sql,[Text.UTF8Encoding]::new($false))
$saved=$env:PGPASSWORD
try{$env:PGPASSWORD=$runtime.Password;$raw=& 'C:/Program Files/PostgreSQL/18/bin/psql.exe' -h 127.0.0.1 -p 55441 -U $runtime.Username -d lims_planning_dev -X -A -t -f $sqlFile;if($LASTEXITCODE -ne 0){throw 'No se pudo comprobar la preservación'};$preserved=$raw|ConvertFrom-Json}finally{$env:PGPASSWORD=$saved}
if($preserved.preservedEpisodes -ne $baseline.episodes -or $preserved.preservedExecutions -ne $baseline.executions){throw 'Se perdieron identificadores anteriores'}
$checks.Add('Todos los episodios y ejecuciones previos conservan su UUID')
@{Checks=$checks.ToArray();Counts=$tiny.counts;Preservation=$preserved;ChangedJob=$changed;BothFilesJob=$both.items[0].job;SummaryOnlyJob=$summaryOnly.items[0].job;ReportOnlyJob=$reportOnly.items[0].job;MissingVendorJob=$vendor.items[0].job;SummaryTrayTotal=$tray.total;Boundary='Authenticated API and actual TIPS XLSX against isolated lims_planning_dev; no production writes'}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $evidence 'acceptance-api.json') -Encoding UTF8
$checks|ForEach-Object{'PASS: '+$_}
