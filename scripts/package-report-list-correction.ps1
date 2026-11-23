param([string]$ArchiveName='ReportList-Correccion-2026-11-20.zip')
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$out=Join-Path $workspace 'artifacts/report-list-correction'
if([IO.Path]::GetFileName($ArchiveName) -ne $ArchiveName -or -not $ArchiveName.EndsWith('.zip')){throw 'Use a ZIP filename within the workspace'}
$archive=Join-Path $workspace $ArchiveName;if(Test-Path $archive){throw 'Existing delivery is preserved; choose a new archive name'}
$tests=@();foreach($name in @('correction-postgres','correction-api','correction-desktop')){[xml]$trx=Get-Content (Join-Path $out ('tests/'+$name+'.trx')) -Raw;$c=$trx.TestRun.ResultSummary.Counters;if([int]$c.failed -ne 0 -or [int]$c.notExecuted -ne 0 -or [int]$c.passed -lt 1){throw 'Tests did not pass completely'};$tests+=@{Name=$name;Passed=[int]$c.passed}}
$overview=Get-Content (Join-Path $out 'native-Overview.json') -Raw|ConvertFrom-Json
$assembly=Join-Path $workspace 'Frontend/Lims.Desktop/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/Lims.Desktop.dll'
if((Get-FileHash $assembly).Hash -ne $overview.AssemblySha256){throw 'Capture must come from the final build'}
foreach($name in @('native-Repeated','native-Cells','native-Layout','native-Select','native-Save','native-Groups','native-Detail','native-Review','native-Verify','native-References','native-ClockCombined','native-Filters','native-Volume')){if(-not (Get-Content (Join-Path $out ($name+'.json')) -Raw|ConvertFrom-Json).Passed){throw ('Missing native acceptance: '+$name)}}
$persistence=Get-Content (Join-Path $out 'persistence.json') -Raw|ConvertFrom-Json
$db=Get-Content (Join-Path $out 'postgres-reread.json') -Raw|ConvertFrom-Json
if(-not $persistence.Passed -or $db.Count -ne 2){throw 'Actual persistence evidence required'}
$screens=@(
 @('01-report-list-fechas.png','Tabla completa con Fecha de ingreso y Due Date'),
 @('02-ph-repetidas.png','Dos PH del mismo JOB: Digitada y Sin información'),
 @('03-celdas-ctrl-c.png','Selección de celdas y Ctrl+C real: 2 filas por 3 columnas'),
 @('04-preferencias-columnas.png','Orden y ancho por usuario: JOB de 200 px'),
 @('05-seleccion-multiple-fecha-hora.png','Selección múltiple, fecha y hora'),
 @('06-confirmacion.png','Confirmación antes de guardar'),
 @('07-digitacion-guardada.png','Digitación guardada desde WinUI'),
 @('08-chequeo-horarios.png','Chequeo de horarios agrupados por fecha y hora'),
 @('09-detalle-horario.png','Detalle con pruebas y usuarios'),
 @('10-horario-chequeado.png','Chequeo guardado con revisor y fecha'),
 @('13-horario-varias-confirmaciones.png','Un horario con dos confirmaciones y tres pruebas'),
 @('14-filtros-fechas-estado.png','Filtros reales de fechas y estado'),
 @('15-volumen-virtualizado.png','3,000 registros cargados y 16 filas visibles'),
 @('11-estandares-nativos.png','Referencia nativa: Estándares, vacía en esta restauración'),
 @('12-preparaciones-nativas.png','Referencia nativa: Preparaciones, vacía en esta restauración'))
$gallery=($screens|ForEach-Object{'<figure><figcaption>'+[Net.WebUtility]::HtmlEncode($_[1])+'</figcaption><a href="ui/'+$_[0]+'"><img loading="lazy" src="ui/'+$_[0]+'" alt="'+[Net.WebUtility]::HtmlEncode($_[1])+'"></a></figure>'}) -join "`n"
$html=@"
<!doctype html><html lang="es"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Report List — evidencia local</title>
<style>body{font:16px Segoe UI,sans-serif;color:#183047;background:#edf2f6;margin:24px auto;max-width:1500px;padding:0 24px}h1{font-size:26px}p{line-height:1.6}main{display:grid;grid-template-columns:repeat(auto-fit,minmax(480px,1fr));gap:20px}figure{margin:0;background:white;border:1px solid #cad5df;border-radius:8px;overflow:hidden}figcaption{padding:12px;font-weight:600}img{width:100%;height:auto;display:block}a{color:#075eab}small{color:#425c70}</style>
<h1>Report List — corrección y evidencia real</h1><p>20 de noviembre de 2026. Cliente WinUI autenticado, dentro del LIMS existente. Base restaurada local <code>127.0.0.1:55441/lims_planning_dev</code>. <strong>104 pruebas aprobadas, ninguna omitida.</strong> Dos ejecuciones guardadas y chequeadas desde la interfaz, verificadas después de reiniciar API y cliente y mediante consulta PostgreSQL de solo lectura. Producción sin cambios.</p>
<p><a href="persistence.json">Persistencia y Consulta</a> · <a href="postgres-reread.json">Lectura PostgreSQL</a> · <a href="persistence-read-only.sql">SQL de solo lectura</a> · <a href="chequeados.csv">CSV de chequeados</a> · <a href="combined-clock.json">Sesiones del horario</a> · <a href="volume.json">Virtualización</a></p>
<p><small>El texto adjunto no contenía las capturas anterior/actual mencionadas. Sí se inspeccionó el HTML original Digitacion_23_OCT.html. Las imágenes siguientes son capturas de la aplicación abierta; Estándares y Preparaciones muestran el estado vacío de esta restauración. Esta evidencia no acredita despliegue productivo, LAN ni hardware físico.</small></p><main>$gallery</main></html>
"@
[IO.File]::WriteAllText((Join-Path $out 'evidencia.html'),$html,[Text.UTF8Encoding]::new($false))
@{Passed=$true;Boundary='Restored local PostgreSQL and authenticated native WinUI only';Tests=$tests;TotalTests=($tests|ForEach-Object{$_.Passed}|Measure-Object -Sum).Sum;FinalClientSha256=$overview.AssemblySha256;NativeSaveExecutions=@($db.executionId);PostRestartVerifiedUtc=$persistence.Utc;ProductionChanged=$false;OriginalScreenshotsAttached=$false;Utc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $out 'acceptance.json') -Encoding UTF8
$sources=@(
 'Backend/Lims.Api/Planning/PlanningEndpoints.cs','Backend/Lims.Application/Planning/PlanningModels.cs',
 'Backend/Lims.Infrastructure/Planning/PlanningColumnFilters.cs','Backend/Lims.Infrastructure/Planning/PostgresPlanningQueries.cs','Backend/Lims.Infrastructure/Planning/PostgresReportList.cs','Backend/Lims.Infrastructure/Planning/PostgresReportSchedules.cs','Backend/Lims.Infrastructure/Planning/PostgresReportClockGroups.cs','Backend/Lims.Infrastructure/Planning/ReportScheduleSchema.cs','Backend/Lims.Infrastructure/Persistence/Migrations/20261120120000_AddReportScheduleReview.cs',
 'Frontend/Lims.Desktop/Planning/PlanningApiClient.cs','Frontend/Lims.Desktop/Planning/PlanningReportListView.cs','Frontend/Lims.Desktop/Planning/PlanningSheetModel.cs','Frontend/Lims.Desktop/Planning/PlanningUi.cs','Frontend/Lims.Desktop/Planning/PlanningJobStatus.cs','Frontend/Lims.Desktop/Planning/ReportListGrid.cs','Frontend/Lims.Desktop/Planning/ReportListTableModel.cs','Frontend/Lims.Desktop/Planning/ReportNativeTable.cs','Frontend/Lims.Desktop/Planning/PlanningWorkspacePage.cs','Frontend/Lims.Desktop/Planning/WorkspaceNavigationService.cs',
 'Shared/Lims.Contracts/Planning/PlanningContracts.cs','Shared/Lims.Contracts/Planning/ReportListContracts.cs',
 'Tests/Backend/Lims.Api.Tests/PlanningAuthorizationTests.cs','Tests/Backend/Lims.Infrastructure.Tests/PlanningPostgresTests.cs','Tests/Backend/Lims.Infrastructure.Tests/ReportListPostgresTests.cs','Tests/Backend/Lims.Infrastructure.Tests/ReportSchedulePostgresTests.cs','Tests/Backend/Lims.Infrastructure.Tests/ReportClockGroupPostgresTests.cs','Tests/Frontend/Lims.Desktop.Tests/PlanningCompatibilityTests.cs','Tests/Frontend/Lims.Desktop.Tests/ReportListTableTests.cs',
 'docs/report-list-horarios-auditoria.md','docs/report-list-horarios-entrega.md','scripts/start-report-schedules-development.ps1','scripts/validate-report-list-correction.ps1','scripts/package-report-list-correction.ps1')
$evidence=@(Get-ChildItem $out -File|Where-Object{$_.Extension -in @('.json','.html','.csv','.sql','.log') -and $_.Name -notin @('manifest.json','package-verification.json')}|ForEach-Object{'artifacts/report-list-correction/'+$_.Name})
$files=@($sources)+$evidence+@($screens|ForEach-Object{'artifacts/report-list-correction/ui/'+$_[0]})+@(Get-ChildItem (Join-Path $out 'tests') -Filter '*.trx'|ForEach-Object{'artifacts/report-list-correction/tests/'+$_.Name})
$manifest=@($files|ForEach-Object{$file=Get-Item -LiteralPath (Join-Path $workspace $_);@{Path=$_;Bytes=$file.Length;Sha256=(Get-FileHash $file.FullName).Hash}})
$manifest|ConvertTo-Json -Depth 5|Set-Content (Join-Path $out 'manifest.json') -Encoding UTF8
Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create)
try{foreach($file in $files){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $workspace $file),$file,[IO.Compression.CompressionLevel]::Optimal)|Out-Null};[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $out 'manifest.json'),'manifest.json',[IO.Compression.CompressionLevel]::Optimal)|Out-Null}finally{$zip.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead($archive);$sha=[Security.Cryptography.SHA256]::Create()
try{if($zip.Entries.Count -ne $files.Count+1){throw 'Wrong ZIP count'};foreach($item in $manifest){$entry=$zip.Entries|Where-Object{$_.FullName.Replace('\','/') -eq $item.Path}|Select-Object -First 1;$stream=$entry.Open();try{$hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}finally{$stream.Dispose()};if($hash -ne $item.Sha256){throw ('Wrong ZIP hash: '+$item.Path)}}}finally{$sha.Dispose();$zip.Dispose()}
@{Passed=$true;Entries=$files.Count+1;Archive=$ArchiveName;Sha256=(Get-FileHash $archive).Hash;Excluded='Credentials, tokens, full backups, runtime settings and unrelated local configuration';Utc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json|Set-Content (Join-Path $out 'package-verification.json') -Encoding UTF8
Write-Output ('PASS: '+($files.Count+1)+' ZIP entries verified by SHA256')
