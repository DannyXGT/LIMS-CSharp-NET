$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$out=Join-Path $workspace 'artifacts/report-schedules'
Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
$sources=@(
 'Backend/Lims.Api/Planning/PlanningEndpoints.cs',
 'Backend/Lims.Application/Planning/PlanningModels.cs',
 'Backend/Lims.Infrastructure/Planning/PlanningColumnFilters.cs',
 'Backend/Lims.Infrastructure/Planning/PostgresPlanningQueries.cs',
 'Backend/Lims.Infrastructure/Planning/PostgresReportList.cs',
 'Backend/Lims.Infrastructure/Planning/PostgresReportSchedules.cs',
 'Backend/Lims.Infrastructure/Planning/ReportScheduleSchema.cs',
 'Backend/Lims.Infrastructure/Persistence/Migrations/20261120120000_AddReportScheduleReview.cs',
 'Frontend/Lims.Desktop/Planning/PlanningApiClient.cs',
 'Frontend/Lims.Desktop/Planning/PlanningReportListView.cs',
 'Frontend/Lims.Desktop/Planning/PlanningSheetModel.cs',
 'Frontend/Lims.Desktop/Planning/PlanningUi.cs',
 'Frontend/Lims.Desktop/Planning/PlanningJobStatus.cs',
 'Frontend/Lims.Desktop/Planning/ReportListGrid.cs',
 'Frontend/Lims.Desktop/Planning/ReportNativeTable.cs',
 'Shared/Lims.Contracts/Planning/PlanningContracts.cs',
 'Shared/Lims.Contracts/Planning/ReportListContracts.cs',
 'Tests/Backend/Lims.Api.Tests/PlanningAuthorizationTests.cs',
 'Tests/Backend/Lims.Infrastructure.Tests/PlanningPostgresTests.cs',
 'Tests/Backend/Lims.Infrastructure.Tests/ReportSchedulePostgresTests.cs',
 'Tests/Frontend/Lims.Desktop.Tests/PlanningCompatibilityTests.cs',
 'docs/report-list-horarios-auditoria.md','docs/report-list-horarios-entrega.md',
 'scripts/start-report-schedules-development.ps1','scripts/validate-report-schedules-api.ps1',
 'scripts/validate-report-schedules-desktop.ps1','scripts/validate-report-schedules-volume.ps1',
 'scripts/validate-report-schedules-concurrency.ps1','scripts/verify-report-schedules-history.ps1',
 'scripts/package-report-schedules-evidence.ps1')
$evidence=@('acceptance-final.json','real-baseline.json','real-Verify.json','real-AfterRestart.json',
 'postgres-AfterRestart.json','real-exercise-performance.json','real-two-user-concurrency.json','real-volume.json',
 'real-historical-compatibility.json','native-scroll-volume.json','native-checked-export.json',
 'native-checked.csv','api-checked.csv','upgrade-report-schedules.sql',
 'tests/rules.trx','tests/postgres-final.trx','tests/api-final.trx','tests/desktop-final.trx','tests/filters-export-final.trx')
$screens=@('01-report-list.png','02-seleccion-fecha-hora.png','03-confirmacion.png','04-digitacion-guardada.png',
 '05-chequeo-horarios.png','06-detalle-horario.png','07-horario-chequeado.png','08-historial.png',
 '09-cierres.png','10-consulta-chequeo.png','11-busqueda-multiple.png','12-volumen-nativo.png','13-cambio-rapido-horarios.png','14-exportacion-chequeados.png')
$files=@($sources)+@($evidence|ForEach-Object{'artifacts/report-schedules/'+$_})+@($screens|ForEach-Object{'artifacts/report-schedules/ui/'+$_})
$manifest=@();foreach($relative in $files){$file=Get-Item -LiteralPath (Join-Path $workspace $relative) -ErrorAction Stop;$manifest+=@{Path=$relative;Length=$file.Length;Sha256=(Get-FileHash -LiteralPath $file.FullName).Hash}}
$manifestPath=Join-Path $out 'manifest.json';$manifest|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $manifestPath -Encoding UTF8
$zipPath=Join-Path $workspace 'ReportList-Horarios-2026-11-20.zip'
if(Test-Path -LiteralPath $zipPath){throw 'Existing delivery is preserved. Choose a new delivery name before repackaging.'}
$zip=[IO.Compression.ZipFile]::Open($zipPath,[IO.Compression.ZipArchiveMode]::Create)
try{foreach($relative in $files){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $workspace $relative),$relative,[IO.Compression.CompressionLevel]::Optimal)|Out-Null};[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$manifestPath,'manifest.json',[IO.Compression.CompressionLevel]::Optimal)|Out-Null}finally{$zip.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead($zipPath);$sha=[Security.Cryptography.SHA256]::Create()
try{
 if($zip.Entries.Count -ne $files.Count+1){throw 'Unexpected ZIP entry count'}
 foreach($item in $manifest){$entry=$zip.Entries|Where-Object{$_.FullName.Replace('\','/') -eq $item.Path}|Select-Object -First 1;if(-not $entry){throw 'ZIP member missing'};$stream=$entry.Open();try{$hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}finally{$stream.Dispose()};if($hash -ne $item.Sha256){throw ('ZIP hash mismatch: '+$item.Path)}}
}finally{$sha.Dispose();$zip.Dispose()}
@{Passed=$true;Entries=$files.Count+1;Archive=$zipPath;Sha256=(Get-FileHash -LiteralPath $zipPath).Hash;Excluded='Full database backup, credentials, tokens, runtime configuration, unrelated pre-existing edits';Utc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json|Set-Content (Join-Path $out 'package-verification.json') -Encoding UTF8
Write-Output ('PASS: delivery ZIP verified, '+($files.Count+1)+' files with SHA256 manifest.')
