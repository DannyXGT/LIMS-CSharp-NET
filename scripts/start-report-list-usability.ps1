param([switch]$NoBuild)
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runtime=Get-Content (Join-Path $workspace 'artifacts/planificacion-dev/runtime.json') -Raw|ConvertFrom-Json
if($runtime.Host -ne '127.0.0.1' -or $runtime.Port -ne 55441 -or $runtime.Database -ne 'lims_planning_dev' -or -not $runtime.Restored){throw 'Este arranque solo permite la base restaurada local.'}
Push-Location $workspace
try{
 if(-not $NoBuild){dotnet build Frontend/Lims.Desktop/Lims.Desktop.csproj --no-restore -p:Platform=x64 -m:1 -v:minimal;if($LASTEXITCODE -ne 0){throw 'No se abrira una compilacion anterior si falla el build.'}}
 $listener=Get-NetTCPConnection -LocalPort 7441 -State Listen -ErrorAction SilentlyContinue|Select-Object -First 1
 if(-not $listener){& (Join-Path $PSScriptRoot 'start-report-schedules-development.ps1') -ApiOutput api-correction}
 $record=Join-Path $workspace 'artifacts/planificacion-dev/desktop-validation.json'
 if(Test-Path $record){$known=Get-Content $record -Raw|ConvertFrom-Json;foreach($process in Get-Process -Name Lims.Planning.Validation -ErrorAction SilentlyContinue){if(-not $process.Path.StartsWith($known.Stage,[StringComparison]::OrdinalIgnoreCase)){throw 'Hay otro cliente de validacion; no se cerrara automaticamente.'};Stop-Process -Id $process.Id}}
 & (Join-Path $PSScriptRoot 'start-planning-development.ps1') -Desktop -DesktopOutput 'Frontend\Lims.Desktop\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64'
 $current=Get-Content $record -Raw|ConvertFrom-Json
 $hash=(Get-FileHash (Join-Path $workspace 'Frontend/Lims.Desktop/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/Lims.Desktop.dll')).Hash
 if($current.OriginalAssemblySha256 -ne $hash){throw 'El cliente de pruebas no coincide con la compilacion actual.'}
 Write-Output 'Cliente actual preparado para la base local de pruebas. La instalacion habitual y produccion no se modificaron.'
}finally{Pop-Location}
