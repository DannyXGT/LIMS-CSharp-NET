$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$local=Join-Path $root 'artifacts/planificacion-dev';$out=Join-Path $local 'centro-operativo'
$runtime=Get-Content (Join-Path $local 'runtime.json') -Raw|ConvertFrom-Json
if($runtime.Host -ne '127.0.0.1' -or $runtime.Port -ne 55441 -or $runtime.Database -ne 'lims_planning_dev'){throw 'Destino no autorizado'}
$account=Get-Content (Join-Path $local 'acceptance-account.json') -Raw|ConvertFrom-Json
$auth=Invoke-RestMethod https://localhost:7441/api/auth/login -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes((@{identifier=$account.email;password=$account.password;clientName='Centro operativo API acceptance'}|ConvertTo-Json)))
$headers=@{Authorization='Bearer '+$auth.accessToken}
function Review([string]$query){Invoke-RestMethod ('https://localhost:7441/api/planning/reviews/workspace?'+$query) -Headers $headers}
$checks=[Collections.Generic.List[string]]::new();$watch=[Diagnostics.Stopwatch]::StartNew();$page=Review 'Category=SummaryWithoutReport&PageSize=2';$watch.Stop()
if($page.counters.Count -ne 6 -or $page.total -lt 14000 -or $page.items.Count -ne 2){throw 'Faltan bandejas o paginación'}
$checks.Add('Seis categorías con situaciones independientes y total histórico del servidor')
$next=Review 'Category=SummaryWithoutReport&PageSize=2&Page=2'
if($next.items[0].key -eq $page.items[0].key){throw 'Paginación repetida'}
$checks.Add('Páginas distintas; los contadores globales no dependen de la página')
$single=Review 'Category=SummaryWithoutReport&Search=GUAT25002016-REV4-REV5-RE&Situation=All'
if($single.total -ne 1 -or -not $single.items[0].buyer -or -not $single.items[0].officialState){throw 'Faltan campos originales Summary'}
$checks.Add('Solo Summary enriquecido con fechas, estados y WIP reales')
$filter=[uri]::EscapeDataString((@{Job='DOES_NOT_EXIST'}|ConvertTo-Json -Compress));$empty=Review ('Category=SummaryWithoutReport&ColumnFilters='+$filter)
if($empty.total -ne 0){throw 'El filtro de columna no opera en servidor'}
$dateFilter=[uri]::EscapeDataString((@{InDateFrom='2040-01-01'}|ConvertTo-Json -Compress));if((Review ('Category=SummaryWithoutReport&ColumnFilters='+$dateFilter)).total -ne 0){throw 'Filtro fecha incorrecto'}
$checks.Add('Filtros por columna y rangos de calendario aplicados a todos los resultados')
$missingFilter=[uri]::EscapeDataString('{"Vendor":"No informado"}');if((Review ('Category=MissingVendor&Search=GUAT26011849&ColumnFilters='+$missingFilter)).total -ne 1){throw 'No se puede filtrar el valor visible No informado'}
$checks.Add('Filtro No informado coincide con los campos realmente ausentes de la fuente')
$resolved=Review 'Category=SummaryWithoutReport&Situation=Resolved&PageSize=1';if($resolved.total -lt 952){throw 'Histórico resuelto ausente'}
$checks.Add('Resueltos consultables sin reaparecer en pendientes')
$export=Invoke-WebRequest 'https://localhost:7441/api/planning/reviews/export?Category=SummaryWithoutReport&Situation=All&Search=GUAT25002016-REV4-REV5-RE' -Headers $headers -UseBasicParsing
$text=if($export.Content -is [byte[]]){[Text.Encoding]::UTF8.GetString($export.Content)}else{$export.Content};$csv=@(ConvertFrom-Csv $text.TrimStart([char]0xFEFF));if($csv.Count -ne 1 -or $csv[0].JOB -ne $single.items[0].job){throw 'Exportación incoherente'}
$checks.Add('Exportación completa respeta los mismos filtros y situaciones')
foreach($category in @('ReportWithoutSummary','MissingVendor','MissingEquivalence','ExecutionIdentity','ImportError')){$result=Review ('Category='+$category+'&PageSize=1');if($result.counters.Count -ne 6){throw $category}}
$checks.Add('Todas las bandejas responden con datos persistidos y sin mezclar avisos de cobertura')
$batches=Invoke-RestMethod https://localhost:7441/api/planning/imports -Headers $headers
if(-not $batches[0].actorName -or -not ($batches[0].PSObject.Properties.Name -contains 'wipChanges')){throw 'Historial incompleto'}
$checks.Add('Historial con usuario, resultados, fechas y procedencia reales')
@{Boundary='Authenticated localhost API and actual persisted TIPS data in lims_planning_dev';Checks=$checks.ToArray();Counters=$page.counters;InitialTwoRowsMs=$watch.ElapsedMilliseconds;ResolvedSummary=$resolved.total}|ConvertTo-Json -Depth 8|Set-Content (Join-Path $out 'acceptance-api.json') -Encoding UTF8
$checks|ForEach-Object{'PASS: '+$_};'First page milliseconds: '+$watch.ElapsedMilliseconds
