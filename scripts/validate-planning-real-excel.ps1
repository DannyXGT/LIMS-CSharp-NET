$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$local=Join-Path $workspace 'artifacts/planificacion-dev'
$RestoreOnly=$args -contains '-RestoreOnly'
$runtime=Get-Content (Join-Path $local 'runtime.json') -Raw|ConvertFrom-Json
if($runtime.Host -ne '127.0.0.1' -or $runtime.Port -ne 55441 -or $runtime.Database -ne 'lims_planning_dev'){throw 'Destino no autorizado'}
$account=Get-Content (Join-Path $local 'acceptance-account.json') -Raw|ConvertFrom-Json
$auth=Invoke-RestMethod https://localhost:7441/api/auth/login -Method Post -ContentType application/json -Body (@{identifier=$account.email;password=$account.password;clientName='Planning native Excel acceptance'}|ConvertTo-Json)
$headers=@{Authorization='Bearer '+$auth.accessToken}
$job='GUAT25038091-REV3';$uri='https://localhost:7441/api/planning/jobs?Scope=History&SearchMode=Exact&Search='+$job
$evidence=Join-Path $local 'importacion-real/excel';New-Item -ItemType Directory -Path $evidence -Force|Out-Null
$snapshot=Join-Path $evidence 'notes-before-private.json'
if($RestoreOnly){$before=Get-Content $snapshot -Raw -Encoding UTF8|ConvertFrom-Json}else{$before=Invoke-RestMethod $uri -Headers $headers;$before|ConvertTo-Json -Depth 8|Set-Content $snapshot -Encoding UTF8}
try{
    if(-not $RestoreOnly){
    powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'validate-consulta-jobs.ps1') -Step All -EvidenceName 'importacion-real/excel'
    if($LASTEXITCODE -ne 0){throw 'Falló la aceptación nativa de selección/pegado'}
    }
}finally{
    $current=Invoke-RestMethod $uri -Headers $headers;$changes=[Collections.Generic.List[object]]::new()
    $runPath=Join-Path $evidence 'run-info.json';$run=if(Test-Path $runPath){Get-Content $runPath -Raw|ConvertFrom-Json}else{$null}
    foreach($old in $before.items){
        $row=$current.items|Where-Object executionId -eq $old.executionId|Select-Object -First 1
        if(-not $row){throw 'Se perdió una ejecución durante la prueba'}
        if($row.responsible -ne $old.responsible){throw 'Responsable cambió de forma ajena a esta prueba; no se restaura a ciegas'}
        if($row.observation -ne $old.observation){
            if(-not $run -or $row.observation -notlike ('VALIDACION LOCAL *'+$run.RunId+'*')){throw 'Cambio concurrente en notas; no se sobrescribe'}
            $changes.Add(@{executionId=$row.executionId;version=$row.noteVersion;column='Observation';value=$old.observation})
        }
    }
    if($changes.Count){$body=@{cells=$changes.ToArray();reason='Restaurar notas previas tras aceptación nativa local; historial de validación conservado'}|ConvertTo-Json -Depth 6;Invoke-RestMethod 'https://localhost:7441/api/planning/notes' -Method Post -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))|Out-Null}
    $restored=Invoke-RestMethod $uri -Headers $headers
    foreach($old in $before.items){$row=$restored.items|Where-Object executionId -eq $old.executionId|Select-Object -First 1;if($row.observation -ne $old.observation -or $row.responsible -ne $old.responsible -or $row.jobState -ne $old.jobState){throw 'La restauración no coincide con las notas y estado anteriores'}}
    @{Restored=$true;CellsRestored=$changes.Count;Executions=$restored.total;OfficialStatePreserved=$true;Boundary='Isolated local data only; native test notes restored through version-checked API, audit retained'}|ConvertTo-Json|Set-Content (Join-Path $evidence 'notes-restored.json') -Encoding UTF8
}
