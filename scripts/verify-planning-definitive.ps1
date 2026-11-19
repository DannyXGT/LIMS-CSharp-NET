$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$local=Join-Path $workspace 'artifacts/planificacion-dev';$out=Join-Path $local 'centro-definitivo'
$runtime=Get-Content (Join-Path $local 'runtime.json') -Raw|ConvertFrom-Json
if($runtime.Host -ne '127.0.0.1' -or $runtime.Port -ne 55441 -or $runtime.Database -ne 'lims_planning_dev'){throw 'Destino no autorizado'}
$baseline=Get-Content (Join-Path $out 'baseline-private.json') -Raw|ConvertFrom-Json
$saved=$env:PGPASSWORD
try{
 $env:PGPASSWORD=$runtime.Password;$bin=$runtime.PostgresBin;if(-not $bin){$bin='C:/Program Files/PostgreSQL/18/bin'}
 $sql="SELECT json_build_object('episodes',count(*),'executions',(SELECT count(*) FROM planning.executions),'manual',(SELECT count(*) FROM planning.executions WHERE origin='Manual'),'episodeIds',(SELECT json_agg(id) FROM planning.episodes),'executionIds',(SELECT json_agg(id) FROM planning.executions),'resolvedManual',(SELECT count(*) FROM planning.incidents WHERE kind='SummaryWithoutReport' AND manual_resolved AND state='Resolved')) FROM planning.episodes"
 $post=(& (Join-Path $bin 'psql.exe') -h 127.0.0.1 -p 55441 -U $runtime.Username -d lims_planning_dev -X -A -t -c $sql)|ConvertFrom-Json
 if($LASTEXITCODE -ne 0){throw 'No se pudo verificar la base'}
}finally{$env:PGPASSWORD=$saved}
$episodes=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);foreach($id in $post.episodeIds){[void]$episodes.Add($id)}
$executions=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);foreach($id in $post.executionIds){[void]$executions.Add($id)}
foreach($id in $baseline.episodeIds){if(-not $episodes.Contains($id)){throw 'Se perdió una identidad de episodio'}}
foreach($id in $baseline.executionIds){if(-not $executions.Contains($id)){throw 'Se perdió una identidad de ejecución'}}
if($baseline.episodes -ne $post.episodes -or $baseline.executions -ne $post.executions -or $post.manual -ne 1 -or $post.resolvedManual -lt 1){throw 'Duplicación o decisión manual perdida'}
$scenarios=@('Ready','Preview','Sections','Confirm','Reviews','Reimport','ImportedRows','Dossier','Interactions','Export','History')
foreach($scenario in $scenarios){$result=Get-Content (Join-Path $out ('native-'+$scenario+'.json')) -Raw|ConvertFrom-Json;if(-not $result.Passed){throw ('Falta escenario '+$scenario)}}
$process=Get-Process -Name Lims.Planning.Validation
if(-not $process.Path.StartsWith((Join-Path $local 'desktop-validation-'),[StringComparison]::OrdinalIgnoreCase)){throw 'Cliente ajeno'}
$built=Join-Path $workspace 'Frontend/Lims.Desktop/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/Lims.Desktop.dll'
$loaded=Join-Path (Split-Path $process.Path) 'Lims.Desktop.dll'
$builtHash=(Get-FileHash -LiteralPath $built -Algorithm SHA256).Hash;$loadedHash=(Get-FileHash -LiteralPath $loaded -Algorithm SHA256).Hash
if($builtHash -ne $loadedHash){throw 'La interfaz activa no corresponde a la compilación final'}
@{Database='127.0.0.1:55441/lims_planning_dev';Boundary='Native WinUI and authenticated local API using original real XLSX; no production coverage';Episodes=$post.episodes;Executions=$post.executions;ManualExecutions=$post.manual;AllPreviousEpisodeIdsPreserved=$true;AllPreviousExecutionIdsPreserved=$true;ManualResolutionPreserved=$true;NoDuplicateOnReimport=$true;NativeScenarios=$scenarios;ActualAppXMatchesFinalBuild=$true;DesktopSha256=$builtHash;Screenshots=@(Get-ChildItem (Join-Path $out 'ui/*.png')|Select-Object -ExpandProperty Name);VerifiedUtc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json -Depth 6|Set-Content (Join-Path $out 'acceptance-final.json') -Encoding UTF8
'PASS: todas las identidades y la resolución manual conservadas; reimportación sin duplicados; AppX coincide con compilación final'
