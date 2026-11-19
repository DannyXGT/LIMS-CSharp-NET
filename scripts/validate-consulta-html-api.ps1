param([string]$Name='baseline')
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$local=Join-Path $workspace 'artifacts/planificacion-dev'
$runtime=Get-Content -LiteralPath (Join-Path $local 'runtime.json') -Raw|ConvertFrom-Json
if($runtime.Host -ne '127.0.0.1' -or $runtime.Port -ne 55441 -or $runtime.Database -ne 'lims_planning_dev'){throw 'Destino no autorizado.'}
$account=Get-Content -LiteralPath (Join-Path $local 'acceptance-account.json') -Raw|ConvertFrom-Json
$auth=Invoke-RestMethod -Uri https://localhost:7441/api/auth/login -Method Post -ContentType application/json -Body (@{identifier=$account.email;password=$account.password;clientName='Consulta HTML acceptance'}|ConvertTo-Json)
$headers=@{Authorization='Bearer '+$auth.accessToken}
function Read-Jobs([string]$query){Invoke-RestMethod -Uri ('https://localhost:7441/api/planning/jobs?'+$query) -Headers $headers}
$source=Get-Content -LiteralPath (Join-Path $local 'acceptance-api.json') -Raw|ConvertFrom-Json
$job=$source.sourceJob
$first=Read-Jobs 'Scope=History&PageSize=500';$rows=[Collections.Generic.List[object]]::new();foreach($row in $first.items){$rows.Add($row)}
for($page=2;$rows.Count -lt $first.total;$page++){$next=Read-Jobs ('Scope=History&PageSize=500&Page='+$page);if($next.total -ne $first.total -or $next.items.Count -eq 0){throw 'La consulta cambió o quedó incompleta.'};foreach($row in $next.items){$rows.Add($row)}}
$tiny=Read-Jobs 'Scope=History&PageSize=1'
if(($tiny.counts|ConvertTo-Json -Compress) -ne ($first.counts|ConvertTo-Json -Compress)){throw 'Los contadores dependen del tamaño de página.'}
$operational=Read-Jobs 'Scope=Operational&PageSize=1'
$one=Read-Jobs ('Scope=History&SearchMode=Exact&Search='+[uri]::EscapeDataString($job))
if($one.total -ne 24){throw 'El JOB real fuente debe conservar sus 24 ejecuciones.'}
$date=$one.items[0].inDate
$dateFilters=@{InDate=$date}|ConvertTo-Json -Compress
$calendar=Read-Jobs ('Scope=History&PageSize=1&ColumnFilters='+[uri]::EscapeDataString($dateFilters))
$evidence=Join-Path $local ('consulta-html-api-'+$Name);New-Item -ItemType Directory -Path $evidence -Force|Out-Null
$cases=@(@{name='Histórico completo';expected=$first.counts},@{name='Período operativo';from=$operational.scope.dueFrom;expected=$operational.counts},@{name='JOB real con múltiples pruebas y vencido';job=$job;expected=$one.counts},@{name='Filtro calendario Fecha Ingreso';filters=@{fi=$date};expected=$calendar.counts})
@{today=$first.scope.today;rows=$rows.ToArray();cases=$cases}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $evidence 'api-rows.json') -Encoding UTF8
$savedZone=$env:TZ
try{$env:TZ='America/Guatemala';node (Join-Path $PSScriptRoot 'planning/verify-consulta-html.cjs') 'C:/Users/danny.jimenez/Desktop/programas/App_web/Job_Consulta_V_final.html' (Join-Path $evidence 'api-rows.json') (Join-Path $evidence 'comparison.json');if($LASTEXITCODE -ne 0){throw 'Diferencia entre HTML original y API.'}}finally{$env:TZ=$savedZone}
$export=Invoke-WebRequest -UseBasicParsing -Uri ('https://localhost:7441/api/planning/export?Scope=History&SearchMode=Exact&Search='+[uri]::EscapeDataString($job)) -Headers $headers
$text=if($export.Content -is [byte[]]){[Text.Encoding]::UTF8.GetString($export.Content)}else{$export.Content}
$csv=@(ConvertFrom-Csv -InputObject $text.TrimStart([char]0xFEFF))
if($csv.Count -ne $one.total){throw 'La exportación no contiene todo el resultado filtrado.'}
if(@($csv|Where-Object{$_.'JOB' -ne $job -or -not $_.'Prueba'}).Count){throw 'La exportación perdió el JOB o el nombre original.'}
@{SourceJob=$job;FullCounts=$first.counts;OperationalCounts=$operational.counts;SourceCounts=$one.counts;Coverage=$first.scope;RowsRead=$rows.Count;Exported=$csv.Count;CountsIndependentOfPageSize=$true;Boundary='Authenticated isolated local API and original HTML pure JavaScript'}|ConvertTo-Json -Depth 7|Set-Content -LiteralPath (Join-Path $evidence 'acceptance.json') -Encoding UTF8
Write-Output 'PASS: paginación completa, alcance, calendario, exportación filtrada y cobertura explícita.'
