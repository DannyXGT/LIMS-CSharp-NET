$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$local=Join-Path $workspace 'artifacts/planificacion-dev'
$runtime=Get-Content (Join-Path $local 'runtime.json') -Raw|ConvertFrom-Json
if($runtime.Host -ne '127.0.0.1' -or $runtime.Port -ne 55441 -or $runtime.Database -ne 'lims_planning_dev' -or -not $runtime.Restored){throw 'Only local restored development database.'}
$account=Get-Content (Join-Path $local 'acceptance-account.json') -Raw|ConvertFrom-Json
$email='report.readonly.'+[Guid]::NewGuid().ToString('N')+'@intertek.com'
$savedPg=$env:PGPASSWORD;$env:PGPASSWORD=$runtime.Password;$readerId=$null
function Local-Sql([string]$sql){
    $result=$sql|& (Join-Path $runtime.PostgresBin 'psql.exe') -h 127.0.0.1 -p 55441 -U $runtime.Username -d lims_planning_dev -X -qAt -v ON_ERROR_STOP=1
    if($LASTEXITCODE -ne 0){throw 'Local validation SQL failed.'};return $result
}
function Login([string]$identifier){Invoke-RestMethod https://localhost:7441/api/auth/login -Method Post -ContentType 'application/json' -Body (@{identifier=$identifier;password=$account.password;clientName='Report List permission validation'}|ConvertTo-Json)}
try{
    # A separate, explicitly synthetic local account; no original user or password hash is changed.
    $sourceId=[int]$account.id
    $readerId=[int](Local-Sql "INSERT INTO public.usuarios(nombre,email,password_hash,id_rol,activo) SELECT 'LECTURA VALIDACION REPORT LIST','$email',password_hash,(SELECT id FROM public.roles WHERE nombre='Usuario'),true FROM public.usuarios WHERE id=$sourceId RETURNING id;")
    $admin=Login $account.email;$adminHeaders=@{Authorization='Bearer '+$admin.accessToken}
    Invoke-RestMethod ('https://localhost:7441/api/planning/access/users/'+$readerId) -Headers $adminHeaders -Method Put -ContentType 'application/json' -Body (@{permissions=@('planning.view');reason='Cuenta sintetica local para comprobar prohibicion de marcado'}|ConvertTo-Json)|Out-Null
    $reader=Login $email;$readerHeaders=@{Authorization='Bearer '+$reader.accessToken}
    $before=Invoke-RestMethod 'https://localhost:7441/api/planning/report-list?Search=GUAT26022934&View=All' -Headers $readerHeaders
    $target=@($before.items|Where-Object{$_.execution.equivalence -eq 'PH' -and -not $_.execution.complete})[0]
    if(-not $target){throw 'Missing pending PH for authorization check.'}
    $body=@{executions=@(@{executionId=$target.execution.executionId;version=$target.version});checked=$true;reason='Debe rechazarse por falta de permiso'}|ConvertTo-Json -Depth 6
    $status=0
    try{Invoke-WebRequest 'https://localhost:7441/api/planning/report-list/check' -Headers $readerHeaders -Method Post -ContentType 'application/json' -Body $body -UseBasicParsing|Out-Null}catch{if($_.Exception.Response){$status=[int]$_.Exception.Response.StatusCode}else{throw}}
    if($status -ne 403){throw 'API did not reject the read-only user with 403.'}
    $after=Invoke-RestMethod 'https://localhost:7441/api/planning/report-list?Search=GUAT26022934&View=All' -Headers $adminHeaders
    $preserved=$after.items|Where-Object{$_.execution.executionId -eq $target.execution.executionId}
    if($preserved.execution.complete -or $preserved.version -ne $target.version){throw 'Unauthorized request changed execution state.'}
    @{Passed=$true;Boundary='Actual login, database permission and HTTP API; synthetic local read-only account';HttpStatus=$status;ExecutionId=$target.execution.executionId;VersionPreserved=$true;Utc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json|Set-Content (Join-Path $workspace 'artifacts/report-list/real-authorization.json') -Encoding UTF8
    Write-Output 'PASS: authenticated read-only user can consult and receives 403 on marking; execution version unchanged.'
}finally{
    if($readerId){Local-Sql "UPDATE public.usuarios SET activo=false WHERE id=$readerId AND nombre='LECTURA VALIDACION REPORT LIST';"|Out-Null}
    $env:PGPASSWORD=$savedPg
}
