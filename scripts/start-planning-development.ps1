param([switch]$Desktop,[string]$DesktopOutput)
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$local=Join-Path $workspace 'artifacts\planificacion-dev'
$runtime=Get-Content -LiteralPath (Join-Path $local 'runtime.json') -Raw|ConvertFrom-Json
if($runtime.Host -ne '127.0.0.1' -or $runtime.Port -ne 55441 -or $runtime.Database -ne 'lims_planning_dev' -or -not $runtime.Restored){throw 'Destino de desarrollo inesperado.'}
$names=@('ASPNETCORE_ENVIRONMENT','DOTNET_ENVIRONMENT','ConnectionStrings__Lims','Authentication__Jwt__SigningKey','ASPNETCORE_URLS','Desktop__ApiBaseUrl','Desktop__EnvironmentName','Desktop__RequestTimeoutSeconds')
$saved=@{};foreach($name in $names){$saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
try{
    $env:ASPNETCORE_ENVIRONMENT='Development';$env:DOTNET_ENVIRONMENT='Development'
    $env:ConnectionStrings__Lims="Host=127.0.0.1;Port=55441;Database=lims_planning_dev;Username=$($runtime.Username);Password=$($runtime.Password)"
    $env:Authentication__Jwt__SigningKey=$runtime.SigningKey;$env:ASPNETCORE_URLS='https://localhost:7441'
    $listener=Get-NetTCPConnection -LocalPort 7441 -State Listen -ErrorAction SilentlyContinue|Select-Object -First 1
    if($listener){
        $known=Get-Content -LiteralPath (Join-Path $local 'processes.json') -Raw|ConvertFrom-Json
        if($listener.OwningProcess -ne $known.ApiPid){throw 'El puerto 7441 pertenece a otro proceso. No se reutiliza ni se detiene.'}
    }else{
        $process=Start-Process -FilePath 'dotnet.exe' -ArgumentList @(('"'+(Join-Path $workspace 'Backend\Lims.Api\bin\Debug\net10.0\Lims.Api.dll')+'"')) -WorkingDirectory (Join-Path $workspace 'Backend\Lims.Api') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $local 'api.log') -RedirectStandardError (Join-Path $local 'api-error.log')
        @{ApiPid=$process.Id;Url='https://localhost:7441';StartedUtc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $local 'processes.json') -Encoding UTF8
    }
    if($Desktop){
        $env:Desktop__ApiBaseUrl='https://localhost:7441/';$env:Desktop__EnvironmentName='Development';$env:Desktop__RequestTimeoutSeconds='120'
        $candidates=@('Frontend\Lims.Desktop\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64','Frontend\Lims.Desktop\bin\Debug\net10.0-windows10.0.26100.0\win-x64','Frontend\Lims.Desktop\bin\Release\net10.0-windows10.0.26100.0\win-x64')
        if($DesktopOutput){
            $requested=[IO.Path]::GetFullPath((Join-Path $workspace $DesktopOutput))
            $allowedOutputs=@($candidates|ForEach-Object{[IO.Path]::GetFullPath((Join-Path $workspace $_))})
            if($allowedOutputs -notcontains $requested){throw 'La salida del cliente debe ser una compilación conocida dentro del repositorio.'}
            $latest=Get-Item -LiteralPath (Join-Path $requested 'Lims.Desktop.dll') -ErrorAction Stop
        }else{
            $latest=$candidates|ForEach-Object{Get-Item -LiteralPath (Join-Path (Join-Path $workspace $_) 'Lims.Desktop.dll') -ErrorAction SilentlyContinue}|Sort-Object LastWriteTimeUtc -Descending|Select-Object -First 1
        }
        if(-not $latest){throw 'Primero compile el cliente x64.'}
        $output=$latest.DirectoryName
        $stage=Join-Path $local ('desktop-validation-'+[Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $stage|Out-Null
        Get-ChildItem -LiteralPath $output|Where-Object{$_.Name -notlike '*.appxrecipe'}|Copy-Item -Destination $stage -Recurse
        $manifest=Join-Path $stage 'AppxManifest.xml'
        $manifestText=[IO.File]::ReadAllText($manifest).Replace('Name="Lims.NextGeneration.Desktop"','Name="Lims.Planning.Validation"').Replace('Executable="Lims.Desktop.exe"','Executable="Lims.Planning.Validation.exe"').Replace('9F02D8E0-C3AB-45A9-82F8-691AC2F7CFA1','2C81CF5A-E180-42D5-B69F-4092572A9D1A')
        [IO.File]::WriteAllText($manifest,$manifestText,[Text.UTF8Encoding]::new($false))
        Copy-Item -LiteralPath (Join-Path $stage 'Lims.Desktop.exe') -Destination (Join-Path $stage 'Lims.Planning.Validation.exe')
        foreach($settingsFile in @('appsettings.json','appsettings.Development.json')){
            $settingsPath=Join-Path $stage $settingsFile
            $settings=Get-Content -LiteralPath $settingsPath -Raw|ConvertFrom-Json
            $settings.Desktop.ApiBaseUrl='https://localhost:7441/'
            $settings|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $settingsPath -Encoding UTF8
        }
        @{Stage=$stage;Package='Lims.Planning.Validation';Executable='Lims.Planning.Validation.exe';ApiUrl='https://localhost:7441';OriginalAssemblySha256=(Get-FileHash -LiteralPath (Join-Path $output 'Lims.Desktop.dll')).Hash}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $local 'desktop-validation.json') -Encoding UTF8
        Start-Process -FilePath 'C:\Tools\WinAppCLI\winapp.exe' -ArgumentList @('run',('"'+$stage+'"'),'--manifest',('"'+$manifest+'"'),'--exe','Lims.Planning.Validation.exe','--with-alias') -WorkingDirectory $workspace -WindowStyle Hidden -RedirectStandardOutput (Join-Path $local 'desktop.log') -RedirectStandardError (Join-Path $local 'desktop-error.log')
    }
    Write-Output 'API local: https://localhost:7441. Base: lims_planning_dev. Configuración productiva intacta.'
}finally{foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')}}
