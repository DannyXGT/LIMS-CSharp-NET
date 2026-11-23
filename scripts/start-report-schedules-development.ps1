param([switch]$Migrate,[switch]$Restart,[string]$ExistingBackup,[ValidateSet('api-current','api-correction')][string]$ApiOutput='api-current')
$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$local=Join-Path $workspace 'artifacts/planificacion-dev'
$out=Join-Path $workspace 'artifacts/report-schedules'
New-Item -ItemType Directory -Path $out -Force|Out-Null
$runtime=Get-Content (Join-Path $local 'runtime.json') -Raw|ConvertFrom-Json
if($runtime.Host -ne '127.0.0.1' -or $runtime.Port -ne 55441 -or $runtime.Database -ne 'lims_planning_dev' -or -not $runtime.Restored){throw 'Only the restored local development database is authorized.'}
$names=@('ASPNETCORE_ENVIRONMENT','DOTNET_ENVIRONMENT','ConnectionStrings__Lims','ConnectionStrings__Planning','Authentication__Jwt__SigningKey','ASPNETCORE_URLS','PGPASSWORD')
$saved=@{};foreach($name in $names){$saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
try{
    $env:PGPASSWORD=$runtime.Password
    if($Migrate){
        if($ExistingBackup){
            $backup=[IO.Path]::GetFullPath((Join-Path $workspace $ExistingBackup))
            if(-not $backup.StartsWith($out+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or -not(Test-Path -LiteralPath $backup)){throw 'Backup must be an existing artifact in report-schedules.'}
            & (Join-Path $runtime.PostgresBin 'pg_restore.exe') --list $backup | Out-Null
            if($LASTEXITCODE -ne 0){throw 'Existing backup is invalid.'}
        }else{
            $backup=Join-Path $out ('before-schedules-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'.backup')
            & (Join-Path $runtime.PostgresBin 'pg_dump.exe') -h 127.0.0.1 -p 55441 -U $runtime.Username -d lims_planning_dev -Fc -f $backup
            if($LASTEXITCODE -ne 0){throw 'Development backup failed.'}
        }
        $migration=Join-Path $out 'upgrade-report-schedules.sql'
        # Exact SQL source used by the EF migration; no dependency on a globally installed dotnet-ef.
        $source=[IO.File]::ReadAllText((Join-Path $workspace 'Backend/Lims.Infrastructure/Planning/ReportScheduleSchema.cs'))
        $match=[regex]::Match($source,'(?s)public const string Sql\s*=\s*"""(.*?)""";')
        if(-not $match.Success){throw 'Schedule schema source not recognized.'}
        $sql="BEGIN; SET LOCAL lock_timeout='5s'; DO `$migration`$ BEGIN IF NOT EXISTS(SELECT 1 FROM public.`"__EFMigrationsHistory`" WHERE `"MigrationId`"='20261120120000_AddReportScheduleReview') THEN "+$match.Groups[1].Value+" INSERT INTO public.`"__EFMigrationsHistory`"(`"MigrationId`",`"ProductVersion`") VALUES('20261120120000_AddReportScheduleReview','10.0.10'); END IF; END `$migration`$; COMMIT;"
        [IO.File]::WriteAllText($migration,$sql,[Text.UTF8Encoding]::new($false))
        & (Join-Path $runtime.PostgresBin 'psql.exe') -h 127.0.0.1 -p 55441 -U $runtime.Username -d lims_planning_dev -X -v ON_ERROR_STOP=1 -f $migration
        if($LASTEXITCODE -ne 0){throw 'Isolated migration failed.'}
    }
    $record=Join-Path $local 'processes.json'
    $listener=Get-NetTCPConnection -LocalPort 7441 -State Listen -ErrorAction SilentlyContinue|Select-Object -First 1
    if($listener){
        $known=Get-Content $record -Raw|ConvertFrom-Json
        $process=Get-Process -Id $listener.OwningProcess -ErrorAction Stop
        if(-not $Restart -or $known.ApiPid -ne $listener.OwningProcess -or $known.Url -ne 'https://localhost:7441' -or $process.ProcessName -ne 'dotnet'){throw 'An unverified API will not be stopped.'}
        Stop-Process -Id $process.Id
        Wait-Process -Id $process.Id -Timeout 15 -ErrorAction SilentlyContinue
    }
    $env:ASPNETCORE_ENVIRONMENT='Development';$env:DOTNET_ENVIRONMENT='Development'
    $env:ConnectionStrings__Lims="Host=127.0.0.1;Port=55441;Database=lims_planning_dev;Username=$($runtime.Username);Password=$($runtime.Password)"
    $env:ConnectionStrings__Planning=$env:ConnectionStrings__Lims
    $env:Authentication__Jwt__SigningKey=$runtime.SigningKey;$env:ASPNETCORE_URLS='https://localhost:7441'
    $assembly=Join-Path (Join-Path $out $ApiOutput) 'Lims.Api.dll'
    if(-not(Test-Path $assembly)){throw 'Build API into artifacts/report-schedules/api-current.'}
    $process=Start-Process -FilePath 'dotnet.exe' -ArgumentList @(('"'+$assembly+'"')) -WorkingDirectory (Join-Path $workspace 'Backend/Lims.Api') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $out 'api.log') -RedirectStandardError (Join-Path $out 'api-error.log')
    @{ApiPid=$process.Id;Url='https://localhost:7441';StartedUtc=[DateTime]::UtcNow.ToString('O');Assembly=$assembly}|ConvertTo-Json|Set-Content $record -Encoding UTF8
    # Wait for Kestrel before the desktop launcher checks the port; otherwise it can spawn a second API.
    $deadline=[DateTime]::UtcNow.AddSeconds(20);$ready=$false
    do{
        $process.Refresh();if($process.HasExited){throw 'The isolated API exited before binding HTTPS. Read api-error.log.'}
        $bound=Get-NetTCPConnection -LocalPort 7441 -State Listen -ErrorAction SilentlyContinue|Where-Object{$_.OwningProcess -eq $process.Id}|Select-Object -First 1
        if($bound){$ready=$true;break};Start-Sleep -Milliseconds 150
    }while([DateTime]::UtcNow -lt $deadline)
    if(-not $ready){throw 'The isolated API did not become ready on 7441.'}
    Write-Output 'Started existing LIMS API against restored local development database.'
}finally{foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')}}
