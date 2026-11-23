param([string]$ArchiveName='ReportList-Usabilidad-2026-11-20.zip')
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$out=Join-Path $workspace 'artifacts/report-list-usability'
if([IO.Path]::GetFileName($ArchiveName) -ne $ArchiveName -or -not $ArchiveName.EndsWith('.zip')){throw 'Use a ZIP filename within the workspace'}
$archive=Join-Path $workspace $ArchiveName;if(Test-Path $archive){throw 'Previous archives are preserved; use a new filename'}
$assembly=Join-Path $workspace 'Frontend/Lims.Desktop/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/Lims.Desktop.dll';$hash=(Get-FileHash $assembly).Hash
$steps=@('Overview','Select','Save','Saved','Groups','Review','Verify','Correction','Scope','Layout','Cells','Preferences','Volume')
foreach($step in $steps){$record=Get-Content (Join-Path $out ('native-'+$step+'.json')) -Raw|ConvertFrom-Json;if(-not $record.Passed -or $record.AssemblySha256 -ne $hash){throw ('Final build acceptance missing: '+$step)}}
$review=Get-Content (Join-Path $out 'native-Review.json') -Raw|ConvertFrom-Json;$reopened=Get-Content (Join-Path $out 'native-Saved.json') -Raw|ConvertFrom-Json
if($review.ProcessId -eq $reopened.ProcessId){throw 'Client restart evidence required'}
$persistence=Get-Content (Join-Path $out 'persistence-after-restart.json') -Raw|ConvertFrom-Json;$correction=Get-Content (Join-Path $out 'correction-audit.json') -Raw|ConvertFrom-Json
if(-not $persistence.Passed -or -not $persistence.OriginalPHUnchanged -or -not $correction.Passed -or -not $correction.OriginalPHUnchanged){throw 'Persistence and independent execution evidence required'}
[xml]$trx=Get-Content (Join-Path $out 'tests/usability-desktop.trx') -Raw;$c=$trx.TestRun.ResultSummary.Counters
if([int]$c.failed -ne 0 -or [int]$c.notExecuted -ne 0 -or [int]$c.passed -ne 31){throw 'Expected 31 passed desktop tests without omissions'}
$screens=@(
 @('01-digitacion-simple-ph-repetidas.png','Vista actual: dos PH independientes del JOB GUAT26022934'),
 @('02-chk-temporal-fecha-hora.png','Selección temporal y barra contextual tras desmarcar y volver a marcar'),
 @('03-confirmacion.png','Confirmación cancelable antes de guardar'),
 @('04-digitacion-persistida.png','Fecha, hora, autor y CHK persistidos; cliente reabierto'),
 @('05-chequeo-horarios.png','Chequeo de horarios en su propia pestaña'),
 @('06-detalle-horario.png','Detalle del horario guardado'),
 @('07-horario-chequeado.png','Horario chequeado desde WinUI'),
 @('08-correccion-separada-auditada.png','Corrección separada con permiso y motivo obligatorio'),
 @('09-ph-independientes-final.png','Estado final: una PH guardada y otra pendiente; historial conservado'),
 @('10-checks-multiples-visibles.png','Selección múltiple mediante CHK solo entre pendientes visibles'),
 @('11-alcance-fuera-vista-confirmacion.png','Bloqueo del guardado fuera de vista sin consentimiento explícito'),
 @('12-configuracion-compacta.png','Menú compacto de configuración'),
 @('13-celdas-y-teclado.png','Selección de celdas y copia real con teclado'),
 @('14-columnas-orden-ancho.png','Mostrar, ocultar, ordenar y cambiar ancho; preferencias guardadas'),
 @('15-volumen-virtualizado.png','3,000 registros cargados; virtualización nativa'))
$gallery=($screens|ForEach-Object{'<figure><figcaption>'+[Net.WebUtility]::HtmlEncode($_[1])+'</figcaption><a href="ui/'+$_[0]+'"><img loading="lazy" src="ui/'+$_[0]+'" alt="'+[Net.WebUtility]::HtmlEncode($_[1])+'"></a></figure>'}) -join "`n"
$html=@"
<!doctype html><html lang="es"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Report List: corrección de usabilidad</title>
<style>body{font:16px Segoe UI,sans-serif;color:#183047;background:#edf2f6;margin:24px auto;max-width:1500px;padding:0 24px}h1{font-size:26px}p{line-height:1.6}main{display:grid;gap:20px}figure{margin:0;background:white;border:1px solid #cad5df;border-radius:8px;overflow:hidden}figcaption{padding:12px;font-weight:600}img{width:100%;height:auto;display:block}a{color:#075eab}</style>
<h1>Report List: corrección de usabilidad</h1><p>Capturas reales del cliente WinUI autenticado del LIMS existente. JOB GUAT26022934, dos PH con identidades independientes. Se comprobó selección, guardado, revisión, reinicio y corrección auditada. Al terminar se revirtió exclusivamente el marcado creado para esta prueba, conservando su historia y la otra PH.</p>
<p><strong>31 pruebas aprobadas; compilación sin errores ni advertencias.</strong> Base restaurada local <code>127.0.0.1:55441/lims_planning_dev</code>. Producción sin cambios. Esta evidencia no acredita LAN, hardware mínimo ni escáner físico.</p>
<p><a href="persistence-after-restart.json">Persistencia después de reiniciar</a> · <a href="correction-audit.json">Corrección y estado final</a> · <a href="selection-scope.json">Alcance visible</a> · <a href="column-preferences.json">Preferencias de columnas</a> · <a href="volume.json">Virtualización</a></p><main>$gallery</main></html>
"@
[IO.File]::WriteAllText((Join-Path $out 'evidencia.html'),$html,[Text.UTF8Encoding]::new($false))
@{Passed=$true;Boundary='Authenticated native WinUI on restored local PostgreSQL only';FinalClientSha256=$hash;DesktopTestsPassed=31;ProductionChanged=$false;ClientRestartVerified=$true;OriginalPHUnchanged=$true;TestMarkReversedWithAudit=$true;PostRestartVerifiedUtc=$persistence.Utc;Utc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json|Set-Content (Join-Path $out 'acceptance.json') -Encoding UTF8
$sources=@(
 'Abrir-Report-List-Pruebas.cmd','docs/report-list-usabilidad.md','docs/report-list-horarios-entrega.md','docs/report-list-horarios-auditoria.md',
 'Frontend/Lims.Desktop/Planning/PlanningReportListView.cs','Frontend/Lims.Desktop/Planning/ReportListGrid.cs','Frontend/Lims.Desktop/Planning/ReportListTableModel.cs','Frontend/Lims.Desktop/Planning/ReportNativeTable.cs','Frontend/Lims.Desktop/Planning/PlanningApiClient.cs','Frontend/Lims.Desktop/Planning/PlanningSheetModel.cs','Frontend/Lims.Desktop/Planning/PlanningUi.cs','Frontend/Lims.Desktop/Planning/PlanningJobStatus.cs','Frontend/Lims.Desktop/Planning/PlanningWorkspacePage.cs','Frontend/Lims.Desktop/Planning/WorkspaceNavigationService.cs',
 'Backend/Lims.Api/Planning/PlanningEndpoints.cs','Backend/Lims.Application/Planning/PlanningModels.cs','Backend/Lims.Infrastructure/Planning/PostgresReportList.cs','Backend/Lims.Infrastructure/Planning/PostgresReportSchedules.cs','Backend/Lims.Infrastructure/Planning/PostgresReportClockGroups.cs','Backend/Lims.Infrastructure/Planning/ReportScheduleSchema.cs','Backend/Lims.Infrastructure/Planning/PlanningColumnFilters.cs','Backend/Lims.Infrastructure/Planning/PostgresPlanningQueries.cs','Backend/Lims.Infrastructure/Persistence/Migrations/20261120120000_AddReportScheduleReview.cs',
 'Shared/Lims.Contracts/Planning/PlanningContracts.cs','Shared/Lims.Contracts/Planning/ReportListContracts.cs','Tests/Frontend/Lims.Desktop.Tests/ReportListTableTests.cs',
 'scripts/start-report-list-usability.ps1','scripts/start-report-schedules-development.ps1','scripts/start-planning-development.ps1','scripts/login-planning-targeted-validation.ps1','scripts/validate-report-list-usability.ps1','scripts/validate-report-list-correction.ps1','scripts/validate-planning-desktop.ps1','scripts/package-report-list-usability.ps1')
$evidence=@('acceptance.json','baseline.json','persistence-after-restart.json','correction-audit.json','selection-scope.json','column-preferences.json','volume.json','saved-session.json','evidencia.html','desktop-build.log','desktop-tests.log','tests/usability-desktop.trx')+@($steps|ForEach-Object{'native-'+$_+'.json'})
$files=$sources+@($evidence|ForEach-Object{'artifacts/report-list-usability/'+$_})+@($screens|ForEach-Object{'artifacts/report-list-usability/ui/'+$_[0]})
$manifest=@($files|ForEach-Object{$file=Get-Item -LiteralPath (Join-Path $workspace $_);@{Path=$_;Bytes=$file.Length;Sha256=(Get-FileHash $file.FullName).Hash}})
$manifest|ConvertTo-Json -Depth 5|Set-Content (Join-Path $out 'manifest.json') -Encoding UTF8
Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create)
try{foreach($file in $files){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $workspace $file),$file,[IO.Compression.CompressionLevel]::Optimal)|Out-Null};[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $out 'manifest.json'),'manifest.json',[IO.Compression.CompressionLevel]::Optimal)|Out-Null}finally{$zip.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead($archive);$sha=[Security.Cryptography.SHA256]::Create()
try{if($zip.Entries.Count -ne $files.Count+1){throw 'Wrong ZIP count'};foreach($item in $manifest){$entry=$zip.Entries|Where-Object{$_.FullName.Replace('\','/') -eq $item.Path}|Select-Object -First 1;$stream=$entry.Open();try{$actual=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}finally{$stream.Dispose()};if($actual -ne $item.Sha256){throw ('Wrong ZIP hash: '+$item.Path)}}}finally{$sha.Dispose();$zip.Dispose()}
@{Passed=$true;Entries=$files.Count+1;Archive=$ArchiveName;Sha256=(Get-FileHash $archive).Hash;Excluded='Credentials, tokens, runtime settings, backups, binaries, prior-build evidence and unrelated local configuration';Utc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json|Set-Content (Join-Path $out 'package-verification.json') -Encoding UTF8
Write-Output ('PASS: '+($files.Count+1)+' ZIP entries verified by SHA256')
