param([ValidateSet('Preview','Confirm')][string]$Action='Preview',[ValidateSet('13','17')][string]$Source='17')
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$local=Join-Path $workspace 'artifacts/planificacion-dev'
$runtime=Get-Content (Join-Path $local 'runtime.json') -Raw|ConvertFrom-Json
if($runtime.Host -ne '127.0.0.1' -or $runtime.Port -ne 55441 -or $runtime.Database -ne 'lims_planning_dev'){throw 'Destino no autorizado'}
$account=Get-Content (Join-Path $local 'acceptance-account.json') -Raw|ConvertFrom-Json
$auth=Invoke-RestMethod https://localhost:7441/api/auth/login -Method Post -ContentType application/json -Body (@{identifier=$account.email;password=$account.password;clientName='Planning real XLSX local validation'}|ConvertTo-Json)
$headers=@{Authorization='Bearer '+$auth.accessToken}
$evidence=Join-Path $local 'importacion-real';New-Item -ItemType Directory -Path $evidence -Force|Out-Null
$previewPath=Join-Path $evidence ('preview-'+$Source+'.json')
if($Action -eq 'Confirm'){
    $preview=Get-Content $previewPath -Raw|ConvertFrom-Json
    if($preview.issues|Where-Object blocking){throw 'No se confirma una vista previa inválida'}
    $result=Invoke-RestMethod ('https://localhost:7441/api/planning/imports/'+$preview.id+'/confirm') -Headers $headers -Method Post -TimeoutSec 240
    $result|ConvertTo-Json -Depth 8|Set-Content (Join-Path $evidence ('confirmed-'+$Source+'.json')) -Encoding UTF8
    Write-Output ($result|ConvertTo-Json -Compress);exit
}
$directory=if($Source -eq '17'){Join-Path $env:USERPROFILE 'Downloads'}else{Join-Path $env:USERPROFILE 'Desktop/Backup'}
$summary=Join-Path $directory $(if($Source -eq '17'){'a0_-_pro-invoice_summary_17Nov2026045852237.xlsx'}else{'a0_-_pro-invoice_summary_13Nov2026104743316.xlsx'})
$report=Join-Path $directory $(if($Source -eq '17'){'a0_-_delay_report_list_based_on_due_date_17Nov2026045945888.xlsx'}else{'a0_-_delay_report_list_based_on_due_date_13Nov2026103900894.xlsx'})
Add-Type -AssemblyName System.Net.Http
$client=[Net.Http.HttpClient]::new();$client.Timeout=[TimeSpan]::FromMinutes(4);$client.DefaultRequestHeaders.Authorization=[Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer',$auth.accessToken)
$form=[Net.Http.MultipartFormDataContent]::new()
try{
    $form.Add([Net.Http.StringContent]::new('First'),'window');$form.Add([Net.Http.StringContent]::new(('2026-11-'+$Source)),'day')
    $form.Add([Net.Http.ByteArrayContent]::new([IO.File]::ReadAllBytes($summary)),'summary',[IO.Path]::GetFileName($summary))
    $form.Add([Net.Http.ByteArrayContent]::new([IO.File]::ReadAllBytes($report)),'report',[IO.Path]::GetFileName($report))
    $response=$client.PostAsync('https://localhost:7441/api/planning/imports/preview',$form).GetAwaiter().GetResult()
    $text=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult();if(-not $response.IsSuccessStatusCode){throw ('Preview HTTP '+[int]$response.StatusCode+': '+$text)}
    [IO.File]::WriteAllText($previewPath,$text,[Text.UTF8Encoding]::new($false));$preview=$text|ConvertFrom-Json
    [pscustomobject]@{Id=$preview.id;Status=$preview.status;NewJobs=$preview.newJobs;NewTests=$preview.newTests;UpdatedJobs=$preview.updatedJobs;Unchanged=$preview.unchangedJobs;Ambiguous=$preview.ambiguousRows;Issues=@($preview.issues|Group-Object code|Select-Object Name,Count);Blocking=@($preview.issues|Where-Object blocking|Select-Object -First 5);Files=$preview.files}|ConvertTo-Json -Depth 6
}finally{$form.Dispose();$client.Dispose()}
