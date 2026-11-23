$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$local=Join-Path $workspace 'artifacts/planificacion-dev';$out=Join-Path $workspace 'artifacts/report-schedules'
$r=Get-Content (Join-Path $local 'runtime.json') -Raw|ConvertFrom-Json
if($r.Host -ne '127.0.0.1' -or $r.Port -ne 55441 -or $r.Database -ne 'lims_planning_dev' -or -not $r.Restored){throw 'Only restored local database.'}
$a=Get-Content (Join-Path $local 'acceptance-account.json') -Raw|ConvertFrom-Json
$saved=$env:PGPASSWORD;$env:PGPASSWORD=$r.Password;$secondId=$null
function Sql([string]$sql){$result=$sql|& (Join-Path $r.PostgresBin 'psql.exe') -h 127.0.0.1 -p 55441 -U $r.Username -d lims_planning_dev -X -qAt -v ON_ERROR_STOP=1;if($LASTEXITCODE -ne 0){throw 'Local SQL failed'};return $result}
function Login([string]$email){Invoke-RestMethod https://localhost:7441/api/auth/login -Method Post -ContentType application/json -Body (@{identifier=$email;password=$a.password;clientName='Local two-user schedule acceptance'}|ConvertTo-Json)}
function Get-Json([string]$path,$headers){Invoke-RestMethod ('https://localhost:7441/api/planning/'+$path) -Headers $headers}
function Post-Json([string]$path,$value,$headers){Invoke-RestMethod ('https://localhost:7441/api/planning/'+$path) -Headers $headers -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes(($value|ConvertTo-Json -Depth 10)))}
try{
 $email='schedule.validation.'+[Guid]::NewGuid().ToString('N')+'@intertek.com'
 $secondId=[int](Sql "INSERT INTO public.usuarios(nombre,email,password_hash,id_rol,activo) SELECT 'REVISION LOCAL HORARIOS','$email',password_hash,(SELECT id FROM public.roles WHERE nombre='Usuario'),true FROM public.usuarios WHERE id=$([int]$a.id) RETURNING id;")
 $one=Login $a.email;$h1=@{Authorization='Bearer '+$one.accessToken}
 Invoke-RestMethod ('https://localhost:7441/api/planning/access/users/'+$secondId) -Headers $h1 -Method Put -ContentType application/json -Body (@{permissions=@('planning.view','planning.report.mark','planning.report.review');reason='Cuenta de validacion local de concurrencia de horarios'}|ConvertTo-Json)|Out-Null
 $two=Login $email;$h2=@{Authorization='Bearer '+$two.accessToken}
 $target=@((Get-Json 'report-list?Test=PH&View=Pending&PageSize=500' $h1).items|Where-Object{$_.execution.job -ne 'GUAT26022934' -and $_.checkMode -eq 'manual' -and -not $_.execution.onHold -and -not $_.execution.noProgram})[0]
 if(-not $target){throw 'No real pending PH for concurrency'}
 Add-Type -AssemblyName System.Net.Http
 $clients=@([Net.Http.HttpClient]::new(),[Net.Http.HttpClient]::new());$tasks=@();$requests=@()
 for($i=0;$i -lt 2;$i++){
  $body=@{sessionId=[Guid]::NewGuid().ToString();executions=@(@{executionId=$target.execution.executionId;version=$target.version});operationalAt='2026-11-20T15:10:00-06:00';reason='Concurrencia local: dos usuarios, una ejecucion real'}
  $requests+=$body;$token=if($i -eq 0){$one.accessToken}else{$two.accessToken};$clients[$i].DefaultRequestHeaders.Authorization=[Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer',$token)
  $content=[Net.Http.StringContent]::new(($body|ConvertTo-Json -Depth 8),[Text.Encoding]::UTF8,'application/json')
  $tasks+=$clients[$i].PostAsync('https://localhost:7441/api/planning/report-list/digitize',$content)
 }
 $responses=@($tasks|ForEach-Object{$_.GetAwaiter().GetResult()});$codes=@($responses|ForEach-Object{[int]$_.StatusCode})
 if(@($codes|Where-Object{$_ -eq 200}).Count -ne 1 -or @($codes|Where-Object{$_ -eq 409}).Count -ne 1){throw ('Unexpected two-user status codes: '+($codes -join ','))}
 $winner=if($codes[0] -eq 200){0}else{1};$id=$requests[$winner].sessionId;$reviewHeaders=if($winner -eq 0){$h2}else{$h1}
 $group=Get-Json ('report-list/schedules/'+$id) $h1
 $review=Post-Json 'report-list/schedules/review' @{sessionId=$id;version=$group.schedule.version;executions=@($group.members|ForEach-Object{@{executionId=$_.item.execution.executionId;version=$_.item.version}});reason='Revision por usuario distinto al digitador'} $reviewHeaders
 if($review.digitizedBy -eq $review.reviewedBy -or $review.state -ne 'Chequeado'){throw 'Separate typist and reviewer not preserved'}
 # An actual correction invalidates this reviewed group and excludes it from checked export.
 $member=(Get-Json ('report-list/schedules/'+$id) $h1).members[0].item
 Post-Json 'report-list/correction' @{executions=@(@{executionId=$member.execution.executionId;version=$member.version});checked=$false;reason='Validacion de invalidacion del chequeo tras corregir'} $h1|Out-Null
 $changed=Get-Json ('report-list/schedules/'+$id) $h1
 if($changed.schedule.state -ne 'Revisar excepciones' -or $changed.schedule.exceptions -ne 1){throw 'Correction did not invalidate the reviewed group'}
 $staleStatus=0;try{Post-Json 'report-list/schedules/review' @{sessionId=$id;version=$group.schedule.version;executions=@(@{executionId=$member.execution.executionId;version=$member.version});reason='Intento de revision obsoleta'} $h1|Out-Null}catch{if($_.Exception.Response){$staleStatus=[int]$_.Exception.Response.StatusCode}else{throw}}
 if($staleStatus -ne 409){throw 'Obsolete group review was not rejected'}
 @{Passed=$true;Boundary='Two authenticated users; original real execution on restored development data; explicitly synthetic local second account';Execution=$target.execution.executionId;ActorIds=@([int]$a.id,$secondId);HttpStatuses=$codes;Reviewed=$review;AfterCorrection=$changed;ObsoleteReviewStatus=$staleStatus;Utc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json -Depth 15|Set-Content (Join-Path $out 'real-two-user-concurrency.json') -Encoding UTF8
 Write-Output 'PASS: two authenticated users, one winner/one 409, separate reviewer, corrected group invalidated and stale review rejected.'
}finally{
 if($clients){foreach($client in $clients){$client.Dispose()}}
 if($secondId){Sql "UPDATE public.usuarios SET activo=false WHERE id=$secondId AND nombre='REVISION LOCAL HORARIOS';"|Out-Null}
 $env:PGPASSWORD=$saved
}
