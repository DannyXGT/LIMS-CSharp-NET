$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runtime=Get-Content (Join-Path $workspace 'artifacts/planificacion-dev/runtime.json') -Raw|ConvertFrom-Json
if($runtime.Host -ne '127.0.0.1' -or $runtime.Port -ne 55441 -or $runtime.Database -ne 'lims_planning_dev'){throw 'Only local validation is authorized.'}
$account=Get-Content (Join-Path $workspace 'artifacts/planificacion-dev/acceptance-account.json') -Raw|ConvertFrom-Json
$auth=Invoke-RestMethod https://localhost:7441/api/auth/login -Method Post -ContentType application/json -Body (@{identifier=$account.email;password=$account.password;clientName='Report List volume read'}|ConvertTo-Json)
$headers=@{Authorization='Bearer '+$auth.accessToken};$ids=[Collections.Generic.HashSet[string]]::new();$timings=@();$revision=$null;$total=0
for($page=1;$page -le 10000;$page++){
    $query="View=All&PageSize=500&Page=$page&Sort=Job";if($null -ne $revision){$query+="&Revision=$revision"}
    $timer=[Diagnostics.Stopwatch]::StartNew()
    $result=Invoke-RestMethod ("https://localhost:7441/api/planning/report-list?"+$query) -Headers $headers
    $timer.Stop();$timings+=$timer.Elapsed.TotalMilliseconds;$revision=$result.revision;$total=$result.total
    foreach($item in $result.items){if(-not $ids.Add($item.execution.executionId)){throw 'Execution was duplicated between pages.'}}
    if($ids.Count -ge $total){break};if($result.items.Count -eq 0){throw 'Paging ended prematurely.'}
}
if($ids.Count -ne $total -or $total -lt 1000){throw 'Real volume not verified.'}
$out=Join-Path $workspace 'artifacts/report-list/recovery';New-Item -ItemType Directory $out -Force|Out-Null
@{Boundary='Authenticated read-only localhost API, restored real TIPS executions';Total=$total;DistinctExecutions=$ids.Count;Pages=$timings.Count;FirstPageMs=$timings[0];MeanPageMs=($timings|Measure-Object -Average).Average;MaximumPageMs=($timings|Measure-Object -Maximum).Maximum;PageTimingsMs=$timings;Revision=$revision;Scanner=$result.capabilities;Utc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $out 'real-volume.json') -Encoding UTF8
Write-Output "PASS: $total real executions read without duplicates across $($timings.Count) requests."
