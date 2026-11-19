param([string]$DeliveryDate='2026-11-19')
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source=Join-Path $workspace 'artifacts/planificacion-dev/centro-definitivo'
$target=Join-Path $workspace ('artifacts/validation/centro-importaciones-definitivo-'+$DeliveryDate)
$archive=Join-Path $workspace ('Centro-Importaciones-Definitivo-'+$DeliveryDate+'.zip')
if(Test-Path -LiteralPath $target){throw 'La carpeta de entrega ya existe; no se sobrescribe'}
if(Test-Path -LiteralPath $archive){throw 'El archivo de entrega ya existe; no se sobrescribe'}
$final=Get-Content (Join-Path $source 'acceptance-final.json') -Raw|ConvertFrom-Json
if(-not $final.ActualAppXMatchesFinalBuild -or -not $final.NoDuplicateOnReimport){throw 'Falta verificacion final'}
$scenarios=@('Ready','Preview','Sections','Confirm','Reviews','Reimport','ImportedRows','Dossier','Interactions','Export','History')
foreach($scenario in $scenarios){$case=Get-Content (Join-Path $source ('native-'+$scenario+'.json')) -Raw|ConvertFrom-Json;if(-not $case.Passed){throw ('Falta escenario '+$scenario)}}
$logs=@('build-api-debug.log','build-api-release.log','build-desktop-debug.log','build-desktop-release.log','tests-backend-final.log','tests-real-emergency.log','tests-authorization.log','tests-frontend.log','tests-compatibility.log','api-checks.log')
New-Item -ItemType Directory -Path $target,(Join-Path $target 'ui'),(Join-Path $target 'checks'),(Join-Path $target 'prior-native-manual')|Out-Null
Copy-Item -LiteralPath (Join-Path $workspace 'docs/centro-importaciones-definitivo.md') -Destination $target
foreach($name in @('acceptance-api.json','acceptance-final.json')+$logs+@($scenarios|ForEach-Object{'native-'+$_+'.json'})){Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $target 'checks')}
# Explicit whitelist excludes diagnostic captures, raw exports, backups, credentials and baseline UUID inventories.
foreach($name in @('01-seleccionar-archivos','02-revision-emergencias','03-revision-solo-summary','04-revision-solo-report','05-revision-buyer-vendor','06-revision-equivalencias','07-resumen-importacion','08-bandejas-lote-actual','09-expediente-desde-bandeja','10-seleccion-celdas','11-historial','12-reimportacion-sin-duplicados','13-consulta-del-lote','14-filtro-orden-copia-filas','15-ventana-reducida','16-exportacion-completa')){Copy-Item -LiteralPath (Join-Path $source ('ui/'+$name+'.png')) -Destination (Join-Path $target 'ui')}
foreach($name in @('scenario-C.json','scenario-D.json')){Copy-Item -LiteralPath (Join-Path $workspace ('artifacts/planificacion-dev/centro-operativo/'+$name)) -Destination (Join-Path $target 'prior-native-manual')}
@'
# Centro de Importaciones: entrega de evidencias

Implementacion en el checkout actual LIMS-CSharp-NET. Este ZIP contiene la matriz, capturas reales y resultados de validacion; no es un instalador.

- Lea centro-importaciones-definitivo.md para resultados, limites y procedencia.
- ui contiene las 16 capturas nativas solicitadas y escenarios complementarios.
- checks contiene compilaciones, pruebas, API y aceptacion nativa.
- prior-native-manual documenta la adicion y resolucion real del 18 de noviembre, cuya persistencia se vuelve a comprobar en esta entrega.

Entorno: localhost:7441 y 127.0.0.1:55441/lims_planning_dev. El lote fue aplicado el 18 de noviembre; la verificacion final es del 19. No acredita produccion, LAN ni rendimiento en hardware objetivo.

No incluye credenciales, respaldos, inventarios privados ni capturas de otras aplicaciones.
'@|Set-Content (Join-Path $target 'README.md') -Encoding UTF8
$manifest=@{Date=$DeliveryDate;Boundary=$final.Boundary;Database=$final.Database;BatchId='0163be42-a10e-4705-b68f-a2c76b036bd1';AutomatedDistinctCases=72;ApiChecks=9;NativeScenarios=$scenarios;DesktopSha256=$final.DesktopSha256;Files=@(Get-ChildItem $target -Recurse -File|ForEach-Object{@{Path=$_.FullName.Substring($target.Length+1).Replace('\','/');Bytes=$_.Length;Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}})}
$manifest|ConvertTo-Json -Depth 6|Set-Content (Join-Path $target 'manifest.json') -Encoding UTF8
Compress-Archive -Path (Join-Path $target '*') -DestinationPath $archive -CompressionLevel Optimal
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try{
 $entries=@{};foreach($entry in $zip.Entries){$entries[$entry.FullName.Replace('\','/')]=$entry}
 foreach($file in $manifest.Files){$entry=$entries[$file.Path];if(-not $entry){throw ('Falta archivo en ZIP: '+$file.Path)};$stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create();try{$hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','');if($hash -ne $file.Sha256){throw ('Hash diferente: '+$file.Path)}}finally{$stream.Dispose();$sha.Dispose()}}
 if(@($zip.Entries|Where-Object{$_.Name}).Count -ne $manifest.Files.Count+1){throw 'El ZIP contiene entradas imprevistas'}
}finally{$zip.Dispose()}
Write-Output ('PASS: ZIP verificado, '+$manifest.Files.Count+' archivos con SHA256 y manifiesto')
Write-Output $archive
