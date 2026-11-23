param([ValidateSet('Prepare','Verify','Exercise','AfterRestart')][string]$Step='Verify')
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$local=Join-Path $workspace 'artifacts/planificacion-dev';$out=Join-Path $workspace 'artifacts/report-schedules'
$r=Get-Content (Join-Path $local 'runtime.json') -Raw|ConvertFrom-Json
if($r.Host -ne '127.0.0.1' -or $r.Port -ne 55441 -or $r.Database -ne 'lims_planning_dev' -or -not $r.Restored){throw 'Only restored local development database.'}
$a=Get-Content (Join-Path $local 'acceptance-account.json') -Raw|ConvertFrom-Json
$auth=Invoke-RestMethod https://localhost:7441/api/auth/login -Method Post -ContentType application/json -Body (@{identifier=$a.email;password=$a.password;clientName='Schedule acceptance local'}|ConvertTo-Json)
$headers=@{Authorization='Bearer '+$auth.accessToken}
function Get-Json([string]$path){Invoke-RestMethod ('https://localhost:7441/api/planning/'+$path) -Headers $headers}
function Post-Json([string]$path,$value){Invoke-RestMethod ('https://localhost:7441/api/planning/'+$path) -Headers $headers -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes(($value|ConvertTo-Json -Depth 12)))}
function Ph{ @((Get-Json 'report-list?Search=GUAT26022934&View=All').items|Where-Object{$_.execution.planningTest -eq 'PH'}) }
function Sql([string]$sql){
 $saved=$env:PGPASSWORD;$env:PGPASSWORD=$r.Password
 try{$result=& (Join-Path $r.PostgresBin 'psql.exe') -h 127.0.0.1 -p 55441 -U $r.Username -d lims_planning_dev -X -At -v ON_ERROR_STOP=1 -c $sql;if($LASTEXITCODE -ne 0){throw 'SQL failed'};return $result}finally{$env:PGPASSWORD=$saved}
}
if($Step -eq 'Prepare'){
 $rows=Ph;if($rows.Count -ne 2){throw 'Real repeated PH identities missing'}
 $prior=Get-Content (Join-Path $workspace 'artifacts/report-list/real-mark-identity.json') -Raw|ConvertFrom-Json
 $marked=@($rows|Where-Object{$_.execution.complete})
 # Reset only the exact PH marked by the earlier local acceptance, never an operational user's record.
 if($marked.Count -eq 1){
  $item=$marked[0];$d=Get-Json ('jobs/'+$item.execution.episodeId)
  if($item.execution.executionId -ne $prior.Selected -or $item.execution.digitizedBy -ne 'VALIDACION LOCAL PLANIFICACION' -or -not($d.history|Where-Object{$_.action -eq 'ReportMarked' -and $_.reason -like 'Validacion WinUI local autorizada*'})){throw 'The prior mark is not the verified earlier acceptance record.'}
  Post-Json 'report-list/correction' @{executions=@(@{executionId=$item.execution.executionId;version=$item.version});checked=$false;reason='Preparar escenario A: restaurar exclusivamente el marcado de la validacion WinUI anterior'}|Out-Null
 }
 $rows=Ph;if(@($rows|Where-Object{$_.execution.complete}).Count -ne 0){throw 'Both real PH must be pending'}
 $baseline=Sql "SELECT md5(coalesce(string_agg(row_to_json(r)::text,',' ORDER BY execution_id),'')) FROM planning.report_checks r WHERE execution_id NOT IN ('25fdff34-301c-4593-933d-4461a385072a','6f159138-56b1-417e-a0a6-0958e570e37b')"
 @{Boundary='Restored real data; prior acceptance mark reset through authenticated correction';Job='GUAT26022934';Rows=$rows;HistoricalDigest=$baseline;Utc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json -Depth 12|Set-Content (Join-Path $out 'real-baseline.json') -Encoding UTF8
 Write-Output 'PASS: two distinct real PH executions pending; unrelated historical baseline recorded.'
}
if($Step -in @('Verify','AfterRestart')){
 $rows=Ph;$done=@($rows|Where-Object{$_.execution.complete});$pending=@($rows|Where-Object{-not $_.execution.complete})
 if($rows.Count -ne 2 -or $done.Count -ne 1 -or $pending.Count -ne 1){throw 'Scenario A requires exactly one PH digitized and one still pending.'}
 $item=$done[0];if($item.execution.digitizedAt -ne '2026-11-20T20:54:00+00:00' -and [DateTimeOffset]$item.execution.digitizedAt -ne [DateTimeOffset]'2026-11-20T14:54:00-06:00'){throw 'Operational clock was not preserved'}
 if(-not $item.execution.digitizationSessionId){throw 'Persisted session missing'}
 $schedule=Get-Json ('report-list/schedules/'+$item.execution.digitizationSessionId)
 if($schedule.members.Count -ne 1 -or $schedule.members[0].item.execution.executionId -ne $item.execution.executionId){throw 'Schedule membership does not match the selected PH'}
 $consulta=Get-Json 'jobs?Search=GUAT26022934&SearchMode=Exact&PageSize=100'
 $same=$consulta.items|Where-Object{$_.executionId -eq $item.execution.executionId}
 if(-not $same.complete -or $same.digitizationSessionId -ne $schedule.schedule.id){throw 'Consulta disagrees with Report List'}
 if($Step -eq 'AfterRestart' -and ($schedule.schedule.state -ne 'Chequeado' -or $same.scheduleReviewState -ne 'Chequeado')){throw 'Review did not persist after restart'}
 $dossier=Get-Json ('jobs/'+$item.execution.episodeId)
 if(-not($dossier.history|Where-Object{$_.action -eq 'ReportMarked' -and $_.details -like ('*'+$item.execution.executionId+'*')})){throw 'Audited execution not found'}
 @{Passed=$true;Boundary='Authenticated API and PostgreSQL restored real development data';Rows=$rows;Schedule=$schedule;Consulta=$same;Utc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json -Depth 15|Set-Content (Join-Path $out ('real-'+$Step+'.json')) -Encoding UTF8
 $sql="SELECT json_build_object('execution',x.id,'digitized_at',x.digitized_at,'tested_on',x.tested_on,'digitized_by',x.digitized_by,'session',m.session_id,'reviewed_at',s.reviewed_at,'reviewer',a.nombre)::text FROM planning.executions x JOIN planning.report_session_members m ON m.execution_id=x.id JOIN planning.report_sessions s ON s.id=m.session_id LEFT JOIN planning.actors a ON a.id=s.reviewed_by WHERE x.id='$($item.execution.executionId)'"
 Sql $sql|Set-Content (Join-Path $out ('postgres-'+$Step+'.json')) -Encoding UTF8
 Write-Output ('PASS: PH identity, operational clock, session, Consulta and audit; '+$Step)
}
if($Step -eq 'Exercise'){
 # Representative extra session: actual pending PH executions, no invented rows.
 $candidates=@((Get-Json 'report-list?Test=PH&View=Pending&PageSize=500').items|Where-Object{$_.execution.job -ne 'GUAT26022934' -and $_.checkMode -eq 'manual' -and -not $_.execution.onHold -and -not $_.execution.noProgram}|Select-Object -First 2)
 if($candidates.Count -ne 2){throw 'Two real pending PH executions not available'}
 $request=@{sessionId=[Guid]::NewGuid().ToString();operationalAt='2026-11-20T14:54:00-06:00';reason='Validacion local de lote real de dos pruebas';executions=@($candidates|ForEach-Object{@{executionId=$_.execution.executionId;version=$_.version}})}
 Post-Json 'report-list/digitize' $request|Out-Null;Post-Json 'report-list/digitize' $request|Out-Null
 $group=Get-Json ('report-list/schedules/'+$request.sessionId)
 if($group.members.Count -ne 2 -or $group.schedule.tests -ne 2){throw 'Two independent members were not persisted'}
 $all=Get-Json 'report-list/schedules';$sameClock=@($all.items|Where-Object{[DateTimeOffset]$_.operationalAt -eq [DateTimeOffset]$request.operationalAt})
 if($sameClock.Count -lt 2){throw 'Independent sessions at the same clock were merged'}
 $conflict=$false;try{Post-Json 'report-list/digitize' ($request+@{} |ForEach-Object{$_})|Out-Null}catch{throw 'Identical retry must succeed'}
 $stale=@{};foreach($k in $request.Keys){$stale[$k]=$request[$k]};$stale.sessionId=[Guid]::NewGuid().ToString()
 try{Post-Json 'report-list/digitize' $stale|Out-Null}catch{if([int]$_.Exception.Response.StatusCode -eq 409){$conflict=$true}else{throw}}
 if(-not $conflict){throw 'Stale version silently overwrote a digitization'}
 $timings=@();foreach($path in @('report-list?View=All&PageSize=500','report-list?Test=PH&View=Pending&PageSize=500','report-list/schedules','report-list/closures')){
  $samples=@();for($i=0;$i -lt 8;$i++){$watch=[Diagnostics.Stopwatch]::StartNew();$page=Get-Json $path;$samples+=$watch.Elapsed.TotalMilliseconds}
  $sorted=@($samples|Sort-Object);$timings+=@{Query=$path;Rows=$page.items.Count;Total=$page.total;MinMs=[Math]::Round($sorted[0],2);MedianMs=[Math]::Round(($sorted[3]+$sorted[4])/2,2);MaxMs=[Math]::Round($sorted[-1],2)}
 }
 @{Passed=$true;Boundary='Authenticated local API, restored real data; sequential latency, not LAN or target hardware';Session=$group;IndependentSessions=$sameClock;StaleConflict409=$conflict;Timings=$timings;Utc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json -Depth 15|Set-Content (Join-Path $out 'real-exercise-performance.json') -Encoding UTF8
 Write-Output 'PASS: real two-test batch, idempotent retries, separate same-clock sessions, stale conflict and local query timings.'
}
