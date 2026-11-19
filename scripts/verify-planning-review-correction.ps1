$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$local=Join-Path $workspace 'artifacts/planificacion-dev'
$out=Join-Path $workspace 'artifacts/validation/centro-importaciones-correccion-2026-11-19'
$runtime=Get-Content (Join-Path $local 'runtime.json') -Raw|ConvertFrom-Json
if($runtime.Host -ne '127.0.0.1' -or $runtime.Port -ne 55441 -or $runtime.Database -ne 'lims_planning_dev'){throw 'Destino ajeno al entorno aislado'}
$baseline=Get-Content (Join-Path $local 'centro-definitivo/baseline-private.json') -Raw|ConvertFrom-Json
$saved=$env:PGPASSWORD
try{
 $env:PGPASSWORD=$runtime.Password;$bin=$runtime.PostgresBin;if(-not $bin){$bin='C:/Program Files/PostgreSQL/18/bin'}
 $sql="SELECT json_build_object('episodes',count(*),'executions',(SELECT count(*) FROM planning.executions),'manual',(SELECT count(*) FROM planning.executions WHERE origin='Manual'),'episodeIds',(SELECT json_agg(id) FROM planning.episodes),'executionIds',(SELECT json_agg(id) FROM planning.executions),'resolvedManual',(SELECT count(*) FROM planning.incidents WHERE kind='SummaryWithoutReport' AND manual_resolved AND state='Resolved')) FROM planning.episodes"
 $post=(& (Join-Path $bin 'psql.exe') -h 127.0.0.1 -p 55441 -U $runtime.Username -d lims_planning_dev -X -A -t -c $sql)|ConvertFrom-Json
 if($LASTEXITCODE -ne 0){throw 'No se pudo verificar PostgreSQL'}
}finally{$env:PGPASSWORD=$saved}
$episodes=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);foreach($id in $post.episodeIds){[void]$episodes.Add($id)}
$executions=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);foreach($id in $post.executionIds){[void]$executions.Add($id)}
foreach($id in $baseline.episodeIds){if(-not $episodes.Contains($id)){throw 'Se perdió un episodio previo'}}
foreach($id in $baseline.executionIds){if(-not $executions.Contains($id)){throw 'Se perdió una ejecución previa'}}
if($post.episodes -ne $baseline.episodes -or $post.executions -ne $baseline.executions -or $post.manual -ne 1 -or $post.resolvedManual -lt 1){throw 'Duplicación o resolución perdida'}
$process=Get-Process -Name Lims.Planning.Validation
if(-not $process.Path.StartsWith((Join-Path $local 'desktop-validation-'),[StringComparison]::OrdinalIgnoreCase)){throw 'Cliente ajeno'}
$loaded=(Get-FileHash (Join-Path (Split-Path $process.Path) 'Lims.Desktop.dll')).Hash
$built=(Get-FileHash (Join-Path $workspace 'Frontend/Lims.Desktop/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/Lims.Desktop.dll')).Hash
if($loaded -ne $built){throw 'El cliente activo no coincide con el código compilado'}
$scenarios=@('Layout','Keyboard','Detail','Resolved','Filters','Sizes','Decisions','History','Export','Import','Columns')
foreach($step in $scenarios){$result=Get-Content (Join-Path $out ($step+'.json')) -Raw|ConvertFrom-Json;if(-not $result.Passed -or $result.AssemblySha256 -ne $built){throw ('Escenario pendiente o de otra compilación: '+$step)}}
@{VerifiedUtc=[DateTime]::UtcNow.ToString('O');Boundary='Actual native WinUI 3 client and isolated 127.0.0.1:55441/lims_planning_dev';Episodes=$post.episodes;Executions=$post.executions;ManualExecutions=$post.manual;PreviousIdsPreserved=$true;NoDuplicateOnImport=$true;ManualSummaryResolutionPreserved=$true;DesktopAssemblySha256=$built;NativeScenarios=$scenarios;ProductionValidated=$false}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $out 'final.json') -Encoding UTF8
Write-Output 'PASS: identidades previas conservadas, importación sin duplicados, resolución manual persistida y once recorridos de la compilación final'
