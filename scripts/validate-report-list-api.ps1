param([ValidateSet('Inspect','Mark','Verify','Restore')][string]$Step='Inspect')
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$local=Join-Path $workspace 'artifacts/planificacion-dev';$evidence=Join-Path $workspace 'artifacts/report-list'
$runtime=Get-Content (Join-Path $local 'runtime.json') -Raw|ConvertFrom-Json
if($runtime.Host -ne '127.0.0.1' -or $runtime.Port -ne 55441 -or $runtime.Database -ne 'lims_planning_dev'){throw 'Unexpected database.'}
$account=Get-Content (Join-Path $local 'acceptance-account.json') -Raw|ConvertFrom-Json
$auth=Invoke-RestMethod https://localhost:7441/api/auth/login -Method Post -ContentType 'application/json' -Body (@{identifier=$account.email;password=$account.password;clientName='Report List local validation'}|ConvertTo-Json)
$headers=@{Authorization='Bearer '+$auth.accessToken}
function Get-Json([string]$path){Invoke-RestMethod ('https://localhost:7441/api/planning/'+$path) -Headers $headers}
function Post-Json([string]$path,$value){Invoke-RestMethod ('https://localhost:7441/api/planning/'+$path) -Headers $headers -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes(($value|ConvertTo-Json -Depth 10)))}
$page=Get-Json 'report-list?Search=GUAT26022934&View=All&PageSize=100'
$ph=@($page.items|Where-Object{$_.execution.equivalence -eq 'PH'})
if($ph.Count -ne 2 -or @($ph.execution.executionId|Select-Object -Unique).Count -ne 2){throw 'Expected two real distinct PH executions.'}
if($Step -eq 'Inspect'){
    $before=Get-Json 'jobs?Search=GUAT26022934&SearchMode=Exact&PageSize=100'
    if(@($before.items|Where-Object{$_.planningTest -eq 'PH'}).Count -ne 2){throw 'Consulta did not return both approved names.'}
    $noMap=Get-Json 'reviews/workspace?Category=MissingEquivalence&Search=GUAT26022934&PageSize=100'
    if($noMap.total -lt 1){throw 'Missing equivalence was lost.'}
    $summaryOnly=Get-Json 'jobs?Incident=MissingTests&PageSize=1'
    if($summaryOnly.items.Count -ne 1 -or $summaryOnly.items[0].executionId -or -not $summaryOnly.items[0].inDate){throw 'Expected real Summary-only WIP without an invented test.'}
    Add-Type -AssemblyName System.Net.Http
    $client=[Net.Http.HttpClient]::new();$client.Timeout=[TimeSpan]::FromMinutes(3)
    $client.DefaultRequestHeaders.Authorization=[Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer',$auth.accessToken)
    $summary=Join-Path $env:USERPROFILE 'Downloads/a0_-_pro-invoice_summary_17Nov2026045852237.xlsx'
    $report=Join-Path $env:USERPROFILE 'Downloads/a0_-_delay_report_list_based_on_due_date_17Nov2026045945888.xlsx'
    $countsBefore=Get-Json 'jobs?PageSize=1'
    $form=[Net.Http.MultipartFormDataContent]::new()
    $form.Add([Net.Http.ByteArrayContent]::new([IO.File]::ReadAllBytes($summary)),'summary',[IO.Path]::GetFileName($summary))
    $form.Add([Net.Http.ByteArrayContent]::new([IO.File]::ReadAllBytes($report)),'report',[IO.Path]::GetFileName($report))
    $form.Add([Net.Http.StringContent]::new('First'),'window');$form.Add([Net.Http.StringContent]::new('2026-11-18'),'day')
    try{
        $response=$client.PostAsync('https://localhost:7441/api/planning/imports/preview',$form).GetAwaiter().GetResult()
        $response.EnsureSuccessStatusCode()|Out-Null
        $preview=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json
    }finally{$form.Dispose();$client.Dispose()}
    if($preview.status -ne 'Applied'){throw 'The reference XLSX batch should already be applied; no new batch is confirmed automatically.'}
    $again=Post-Json ('imports/'+$preview.id+'/confirm') @{}
    if(-not $again.alreadyApplied){throw 'Repeated confirmation was not idempotent.'}
    $countsAfter=Get-Json 'jobs?PageSize=1'
    if($countsBefore.counts.executions -ne $countsAfter.counts.executions -or $countsBefore.counts.jobs -ne $countsAfter.counts.jobs){throw 'Reimport changed record counts.'}
    @{Boundary='Authenticated API with real XLSX, restored local database';Database='127.0.0.1:55441/lims_planning_dev';Job='GUAT26022934';Ph=$ph;SummaryOnly=$summaryOnly.items[0];MissingEquivalences=$noMap.total;Reimport=$again;CountsBefore=$countsBefore.counts;CountsAfter=$countsAfter.counts;Utc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json -Depth 12|Set-Content (Join-Path $evidence 'real-api-inspection.json') -Encoding UTF8
    Write-Output 'PASS: two real PH executions, approved names, missing-equivalence tray and idempotent actual XLSX reimport.'
}
if($Step -eq 'Mark'){
    if(@($ph|Where-Object{$_.execution.complete}).Count -ne 0){throw 'Expected unmarked real PH rows before validation.'}
    $selected=$ph[0];$other=$ph[1]
    Post-Json 'report-list/check' @{executions=@(@{executionId=$selected.execution.executionId;version=$selected.version});checked=$true;reason='Validacion local autorizada: marcar una de dos PH reales'}|Out-Null
    @{Selected=$selected.execution.executionId;Other=$other.execution.executionId;Episode=$selected.execution.episodeId;Job=$selected.execution.job}|ConvertTo-Json|Set-Content (Join-Path $evidence 'real-mark-identity.json') -Encoding UTF8
    Write-Output 'PASS: one real PH marked through authenticated API.'
}
if($Step -eq 'Verify'){
    $identity=Get-Content (Join-Path $evidence 'real-mark-identity.json') -Raw|ConvertFrom-Json
    $selected=$ph|Where-Object{$_.execution.executionId -eq $identity.Selected};$other=$ph|Where-Object{$_.execution.executionId -eq $identity.Other}
    if(-not $selected.execution.complete -or $other.execution.complete){throw 'Mark isolation failed.'}
    $consult=Get-Json 'jobs?Search=GUAT26022934&SearchMode=Exact&PageSize=100'
    if(-not ($consult.items|Where-Object{$_.executionId -eq $identity.Selected}).complete){throw 'Consulta did not reflect mark.'}
    $dossier=Get-Json ('jobs/'+$identity.Episode)
    if(-not($dossier.history|Where-Object{$_.action -eq 'ReportMarked' -and $_.details -like ('*'+$identity.Selected+'*')})){throw 'Mark audit missing.'}
    @{Passed=$true;Boundary='Authenticated API read after refresh or restart';Selected=$selected;Other=$other;Utc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json -Depth 12|Set-Content (Join-Path $evidence 'real-mark-persistence.json') -Encoding UTF8
    Write-Output 'PASS: Consulta and Report List agree; only selected PH is marked and history persists.'
}
if($Step -eq 'Restore'){
    $identity=Get-Content (Join-Path $evidence 'real-mark-identity.json') -Raw|ConvertFrom-Json
    $selected=$ph|Where-Object{$_.execution.executionId -eq $identity.Selected}
    Post-Json 'report-list/correction' @{executions=@(@{executionId=$selected.execution.executionId;version=$selected.version});checked=$false;reason='Correccion de la validacion local; restaurar PH para prueba nativa'}|Out-Null
    Write-Output 'PASS: local validation mark corrected with history.'
}
