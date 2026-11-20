param([switch]$Restart)
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$local=Join-Path $workspace 'artifacts/planificacion-dev'
$runtime=Get-Content (Join-Path $local 'runtime.json') -Raw|ConvertFrom-Json
if($runtime.Host -ne '127.0.0.1' -or $runtime.Port -ne 55441 -or $runtime.Database -ne 'lims_planning_dev' -or -not $runtime.Restored){throw 'Only the restored local development database is authorized.'}
$record=Join-Path $local 'processes.json'
if(Test-Path $record){
    $known=Get-Content $record -Raw|ConvertFrom-Json
    $process=Get-Process -Id $known.ApiPid -ErrorAction SilentlyContinue
    if($process){
        if(-not $Restart){throw 'The recorded development API is already running; use -Restart.'}
        if($known.Url -ne 'https://localhost:7441' -or $process.ProcessName -ne 'dotnet'){throw 'Unexpected process; not stopped.'}
        Stop-Process -Id $process.Id
        Wait-Process -Id $process.Id -Timeout 15 -ErrorAction SilentlyContinue
    }
}
$names=@('ASPNETCORE_ENVIRONMENT','DOTNET_ENVIRONMENT','ConnectionStrings__Lims','ConnectionStrings__Planning','Authentication__Jwt__SigningKey','ASPNETCORE_URLS')
$saved=@{};foreach($name in $names){$saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
try{
    $env:ASPNETCORE_ENVIRONMENT='Development';$env:DOTNET_ENVIRONMENT='Development'
    $env:ConnectionStrings__Lims="Host=127.0.0.1;Port=55441;Database=lims_planning_dev;Username=$($runtime.Username);Password=$($runtime.Password)"
    $env:ConnectionStrings__Planning=$env:ConnectionStrings__Lims
    $env:Authentication__Jwt__SigningKey=$runtime.SigningKey;$env:ASPNETCORE_URLS='https://localhost:7441'
    $out=Join-Path $workspace 'artifacts/report-list'
    $assembly=Join-Path $out 'api-final/Lims.Api.dll'
    if(-not(Test-Path $assembly)){throw 'Build the API into artifacts/report-list/api first.'}
    $process=Start-Process -FilePath 'dotnet.exe' -ArgumentList @(('"'+$assembly+'"')) -WorkingDirectory (Join-Path $workspace 'Backend/Lims.Api') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $out 'api.log') -RedirectStandardError (Join-Path $out 'api-error.log')
    @{ApiPid=$process.Id;Url='https://localhost:7441';StartedUtc=[DateTime]::UtcNow.ToString('O');Assembly=$assembly}|ConvertTo-Json|Set-Content $record -Encoding UTF8
    Write-Output 'Started existing LIMS API on https://localhost:7441 using 127.0.0.1:55441/lims_planning_dev.'
}finally{foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')}}
