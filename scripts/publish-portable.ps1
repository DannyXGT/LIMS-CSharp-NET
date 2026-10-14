param()
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $projectRoot 'Frontend\Lims.Desktop\Lims.Desktop.csproj'
$publishPath = Join-Path $projectRoot 'artifacts\publish\LIMS-singlefile-win-x64'
$portableExe = Join-Path $projectRoot 'artifacts\publish\Lims.Desktop.exe'

& dotnet publish $projectPath -c Release -r win-x64 --self-contained true -m:1 -p:Platform=x64 -p:PortableSingleFile=true -o $publishPath
if ($LASTEXITCODE -ne 0) { throw 'Single-file portable publication failed.' }
$publishedExe = Join-Path $publishPath 'Lims.Desktop.exe'
if (-not (Test-Path -LiteralPath $publishedExe)) { throw 'Published executable was not found.' }
Copy-Item -LiteralPath $publishedExe -Destination $portableExe -Force
Write-Host "Portable EXE: $portableExe"
