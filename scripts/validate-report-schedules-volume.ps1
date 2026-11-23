$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$local=Join-Path $workspace 'artifacts/planificacion-dev';$out=Join-Path $workspace 'artifacts/report-schedules'
$r=Get-Content (Join-Path $local 'runtime.json') -Raw|ConvertFrom-Json
if($r.Host -ne '127.0.0.1' -or $r.Port -ne 55441 -or $r.Database -ne 'lims_planning_dev'){throw 'Only restored local database.'}
$a=Get-Content (Join-Path $local 'acceptance-account.json') -Raw|ConvertFrom-Json
$auth=Invoke-RestMethod https://localhost:7441/api/auth/login -Method Post -ContentType application/json -Body (@{identifier=$a.email;password=$a.password;clientName='Read-only schedule volume'}|ConvertTo-Json)
$headers=@{Authorization='Bearer '+$auth.accessToken};$ids=[Collections.Generic.HashSet[string]]::new();$timings=@();$revision=$null
for($page=1;$page -le 10000;$page++){
 $query="View=All&PageSize=500&Page=$page&Sort=Job";if($null -ne $revision){$query+="&Revision=$revision"}
 $watch=[Diagnostics.Stopwatch]::StartNew();$data=Invoke-RestMethod ('https://localhost:7441/api/planning/report-list?'+$query) -Headers $headers;$timings+=$watch.Elapsed.TotalMilliseconds;$revision=$data.revision
 foreach($item in $data.items){if(-not $ids.Add($item.execution.executionId)){throw 'Duplicate execution across server pages'}}
 if($ids.Count -ge $data.total){break};if($data.items.Count -eq 0){throw 'Premature end of real data'}
}
if($ids.Count -ne $data.total -or $data.total -lt 1000){throw 'Real volume not verified'}
Add-Type -AssemblyName System.Net.Http
$handler=[Net.Http.HttpClientHandler]::new();$handler.MaxConnectionsPerServer=50
$client=[Net.Http.HttpClient]::new($handler);$client.Timeout=[TimeSpan]::FromMinutes(3);$client.DefaultRequestHeaders.Authorization=[Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer',$auth.accessToken)
try{
 $watch=[Diagnostics.Stopwatch]::StartNew();$tasks=@();for($i=0;$i -lt 50;$i++){$tasks+=$client.GetAsync('https://localhost:7441/api/planning/report-list?View=All&PageSize=100')}
 $success=0;foreach($task in $tasks){$response=$task.GetAwaiter().GetResult();try{if([int]$response.StatusCode -ne 200){throw 'Parallel read failed'};$success++}finally{$response.Dispose()}}
 $parallelMs=$watch.Elapsed.TotalMilliseconds
}finally{$client.Dispose()}
$process=Get-Process -Name Lims.Planning.Validation -ErrorAction SilentlyContinue
@{Passed=$true;Boundary='Read-only authenticated API, restored real TIPS data, localhost workstation; one account, 50 concurrent requests, not 50 distinct users or LAN';Executions=$ids.Count;Pages=$timings.Count;Revision=$revision;MeanPageMs=($timings|Measure-Object -Average).Average;MaxPageMs=($timings|Measure-Object -Maximum).Maximum;PageTimingsMs=$timings;ConcurrentRequests=$success;ConcurrentBatchMs=$parallelMs;DesktopWorkingSetBytes=$process.WorkingSet64;DesktopPrivateBytes=$process.PrivateMemorySize64;Utc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $out 'real-volume.json') -Encoding UTF8
Write-Output "PASS: $($ids.Count) real executions, $($timings.Count) pages, 50/50 concurrent authenticated reads."
