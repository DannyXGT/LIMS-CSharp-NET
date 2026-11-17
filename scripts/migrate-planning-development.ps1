$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runtime=Get-Content -LiteralPath (Join-Path $workspace 'artifacts\planificacion-dev\runtime.json') -Raw|ConvertFrom-Json
if($runtime.Host -ne '127.0.0.1' -or $runtime.Port -ne 55441 -or $runtime.Database -ne 'lims_planning_dev' -or -not $runtime.Restored){throw 'Solo se permite la copia local creada por prepare-planning-development.ps1.'}
$previous=$env:LIMS_DB_CONNECTION_STRING
$env:LIMS_DB_CONNECTION_STRING="Host=127.0.0.1;Port=55441;Database=lims_planning_dev;Username=$($runtime.Username);Password=$($runtime.Password)"
try {
    $savedPassword=$env:PGPASSWORD
    $env:PGPASSWORD=$runtime.Password
    try {
        & (Join-Path $runtime.PostgresBin 'psql.exe') -h 127.0.0.1 -p 55441 -U $runtime.Username -d lims_planning_dev -X -v ON_ERROR_STOP=1 -f (Join-Path $PSScriptRoot 'planning\development-legacy-prerequisites.sql')
        if($LASTEXITCODE -ne 0){throw 'No se pudo preparar la dependencia legacy exclusiva de la restauración local.'}
    }finally{$env:PGPASSWORD=$savedPassword}
    dotnet tool run dotnet-ef database update --project Backend/Lims.Infrastructure --startup-project Backend/Lims.Infrastructure --no-build
    if($LASTEXITCODE -ne 0){throw 'No se pudieron aplicar las migraciones en la copia aislada.'}
}finally{$env:LIMS_DB_CONNECTION_STRING=$previous}
