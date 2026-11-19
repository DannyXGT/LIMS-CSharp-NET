$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$local=Join-Path $workspace 'artifacts/planificacion-dev';$out=Join-Path $local 'centro-definitivo'
$runtime=Get-Content (Join-Path $local 'runtime.json') -Raw|ConvertFrom-Json
if($runtime.Host -ne '127.0.0.1' -or $runtime.Port -ne 55441 -or $runtime.Database -ne 'lims_planning_dev'){throw 'Destino no autorizado'}
$account=Get-Content (Join-Path $local 'acceptance-account.json') -Raw|ConvertFrom-Json
$auth=Invoke-RestMethod https://localhost:7441/api/auth/login -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes((@{identifier=$account.email;password=$account.password;clientName='Centro definitivo API acceptance'}|ConvertTo-Json)))
$headers=@{Authorization='Bearer '+$auth.accessToken}
function Get-Api([string]$path){Invoke-RestMethod ('https://localhost:7441/api/planning/'+$path) -Headers $headers}
$batches=Get-Api 'imports';$batch=$batches|Where-Object{$_.status -eq 'Applied'}|Select-Object -First 1
$checks=[Collections.Generic.List[string]]::new();$scope='Scope=Batch&BatchId='+$batch.id
$page=Get-Api ('reviews/workspace?Category=SummaryWithoutReport&PageSize=2&'+$scope)
if($page.counters.Count -ne 10 -or $page.items.Count -ne 2 -or $page.total -lt 14000){throw 'Alcance o contadores incorrectos'}
$checks.Add('Siete bandejas operativas y tres categorías compatibles; conteos servidor en lote seleccionado')
$empty=Get-Api ('reviews/workspace?Category=SummaryWithoutReport&Scope=Batch&BatchId='+[guid]::NewGuid().ToString())
if($empty.total -ne 0 -or @($empty.counters|Where-Object{$_.total -ne 0}).Count -ne 0){throw 'El lote filtra incorrectamente los contadores'}
$checks.Add('Lote vacío no hereda cifras del histórico; página y contadores respetan el mismo alcance')
$single=Get-Api ('reviews/workspace?Category=SummaryWithoutReport&Situation=All&Search=GUAT25002016-REV4-REV5-RE&'+$scope)
if($single.total -ne 1 -or $single.items[0].state -ne 'Resolved'){throw 'Resolución manual perdida'}
$dossier=Get-Api ('jobs/'+$single.items[0].episodeId)
if(@($dossier.tests|Where-Object{$_.origin -eq 'Manual'}).Count -ne 1){throw 'Asignación manual duplicada o perdida'}
$checks.Add('Prueba manual original y resolución Solo Summary conservadas, con auditoría y origen')
$inspection=Get-Api ('imports/'+$batch.id+'/inspection')
if($inspection.processedRows -ne 25075 -or -not $inspection.actorName -or -not $inspection.appliedAt){throw 'Resumen o fuentes incompletos'}
if(@($inspection.items|Where-Object{$_.category -eq 'ReportWithoutSummary'}).Count -lt 1){throw 'Solo Report List no se conserva en inspección'}
$complemented=@($inspection.items|Where-Object{$_.category -eq 'PartyComplemented' -and $_.job -eq 'GUAT25038091-REV3'})
if($complemented.Count -ne 1 -or -not $complemented[0].vendor -or -not $complemented[0].buyer){throw 'Complementación Summary ausente'}
$checks.Add('Inspección del plan real: 15,675 Summary + 9,400 Report List, coberturas y WIP consolidado')
$missing=Get-Api ('reviews/workspace?Category=MissingParty&Situation=Pending&Search=GUAT26011849&'+$scope)
if($missing.total -ne 1 -or $missing.items[0].reason -ne 'Falta Vendor'){throw 'Buyer confundido con Vendor'}
$checks.Add('Buyer y Vendor distinguidos, sin usar Applicant como Vendor')
$unknown=Get-Api ('reviews/workspace?Category=MissingEquivalence&Situation=Pending&PageSize=1&'+$scope)
$noProgram=Get-Api ('reviews/workspace?Category=MissingEquivalence&Situation=Resolved&ColumnFilters='+[uri]::EscapeDataString('{"Reason":"NO PROGRAMAR"}')+'&'+$scope)
$catalogue=Get-Api 'equivalences';$noCatalogue=@($catalogue|Where-Object{$_.noProgram})
if($unknown.total -eq 0 -or $noCatalogue.Count -eq 0){throw 'Catálogo real y pendientes no disponibles'}
$checks.Add('Catálogo persistido consultable; equivalencias pendientes y NO PROGRAMAR son situaciones distintas')
$jobs=Get-Api ('jobs?Scope=History&BatchId='+$batch.id+'&PageSize=1')
$jobsEmpty=Get-Api ('jobs?Scope=History&BatchId='+[guid]::NewGuid().ToString())
if($jobs.total -eq 0 -or $jobsEmpty.total -ne 0){throw 'Consulta no filtra por lote'}
$checks.Add('Consulta de Jobs original filtra por membresía de origen del lote, sin crear otra tabla independiente')
$csvPath=Join-Path $out 'solo-summary-lote.csv'
Invoke-WebRequest ('https://localhost:7441/api/planning/reviews/export?Category=SummaryWithoutReport&Situation=Pending&'+$scope) -Headers $headers -OutFile $csvPath -UseBasicParsing
$csv=@(Import-Csv -LiteralPath $csvPath)
if($csv.Count -ne $page.total -or $csv.Count -le 100){throw 'Exportación truncada a la página'}
$checks.Add('Exportación completa de todas las páginas con lote, origen, fecha, situación y motivo')
$keys=ConvertTo-Json -InputObject @($page.items[0].key) -Compress
$selectionBody=@{Category='SummaryWithoutReport';Situation='Pending';Scope='Batch';BatchId=$batch.id;SelectedKeys=$keys}|ConvertTo-Json -Compress
$selection=Invoke-WebRequest 'https://localhost:7441/api/planning/reviews/export' -Headers $headers -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($selectionBody)) -UseBasicParsing
$selectionText=if($selection.Content -is [byte[]]){[Text.Encoding]::UTF8.GetString($selection.Content)}else{$selection.Content}
if(@(ConvertFrom-Csv $selectionText.TrimStart([char]0xFEFF)).Count -ne 1){throw 'Exportación de selección incorrecta'}
$checks.Add('Exportación de selección en servidor, sin incluir otras filas')
@{Boundary='Authenticated localhost API; original XLSX, restored real data and local operator decisions only';Database='127.0.0.1:55441/lims_planning_dev';BatchId=$batch.id;Checks=$checks.ToArray();Counters=$page.counters;ExportRows=$csv.Count;UnknownEquivalences=$unknown.total;NoProgramExecutions=$noProgram.total;NoProgramCatalogue=$noCatalogue.Count;ManualExecution=$dossier.tests|Where-Object{$_.origin -eq 'Manual'}|Select-Object executionId,originalTest,equivalence,laboratory,origin;Inspection=@{ProcessedRows=$inspection.processedRows;NewJobs=$inspection.preview.newJobs;NewTests=$inspection.preview.newTests;ExistingTests=$inspection.existingTests;NewEmergencies=$inspection.newEmergencies;PendingEquivalences=$inspection.pendingEquivalences;Laboratories=$inspection.laboratories}}|ConvertTo-Json -Depth 10|Set-Content (Join-Path $out 'acceptance-api.json') -Encoding UTF8
$checks|ForEach-Object{'PASS: '+$_}
