param(
    [string]$WorkerDirectory,
    [string]$ApiDirectory,
    [string]$LogDirectory,
    [switch]$SkipTaskQuery
)
# Read-only: never starts tasks, runs the worker, queries SQL, changes settings or sends mail.
# Only selected fields are returned; task arguments, configuration values and raw errors are omitted.
$ErrorActionPreference = 'Stop'
$report = [ordered]@{
    CollectedAt = [DateTimeOffset]::Now.ToString('o')
    Computer = $env:COMPUTERNAME
    Boundary = 'Read-only OS/files; database and task-account effective configuration are not verified'
    TaskQuery = 'Skipped'
    Tasks = @()
    Files = @()
    WorkerConfiguration = $null
    LogStatus = 'PathNotProvided'
    RecentEvents = @()
}
if (-not $SkipTaskQuery) {
    try {
        $tasks = @(Get-ScheduledTask -ErrorAction Stop | Where-Object {
            $_.TaskName -match '(?i)lims|alert' -or
            @($_.Actions | Where-Object { $_.Execute -match '(?i)Lims\.AlertWorker' }).Count -gt 0
        })
        $report.TaskQuery = 'Succeeded'
        foreach ($task in $tasks) {
            $info = $null
            try { $info = Get-ScheduledTaskInfo -TaskName $task.TaskName -TaskPath $task.TaskPath -ErrorAction Stop } catch { }
            $actions = @($task.Actions | ForEach-Object {
                if (-not $WorkerDirectory -and $_.Execute -match '(?i)(^|[\\/])Lims\.AlertWorker\.exe$') {
                    $WorkerDirectory = Split-Path -Parent ([Environment]::ExpandEnvironmentVariables($_.Execute.Trim('"')))
                }
                [ordered]@{ Executable=$_.Execute; WorkingDirectory=$_.WorkingDirectory; HasArguments=(-not [string]::IsNullOrWhiteSpace($_.Arguments)); EvaluateOnly=($_.Arguments -match '(^|\s)--evaluate-only(\s|$)') }
            })
            $report.Tasks += [ordered]@{
                Name=$task.TaskName; Path=$task.TaskPath; State=[string]$task.State
                Account=$task.Principal.UserId; LogonType=[string]$task.Principal.LogonType
                LastRun=if($info){$info.LastRunTime.ToString('o')}else{$null}
                LastResult=if($info){$info.LastTaskResult}else{$null}
                NextRun=if($info){$info.NextRunTime.ToString('o')}else{$null}
                MissedRuns=if($info){$info.NumberOfMissedRuns}else{$null}
                Actions=$actions
                Triggers=@($task.Triggers | ForEach-Object { [ordered]@{ Type=$_.CimClass.CimClassName; Enabled=$_.Enabled; Start=$_.StartBoundary; Interval=$_.Repetition.Interval; Delay=$_.Delay } })
                StartWhenAvailable=$task.Settings.StartWhenAvailable
                MultipleInstances=[string]$task.Settings.MultipleInstances
                ExecutionLimit=$task.Settings.ExecutionTimeLimit
            }
        }
    } catch { $report.TaskQuery = 'Unavailable:' + $_.Exception.GetType().Name }
}
foreach ($entry in @(@{Directory=$WorkerDirectory;Name='Lims.AlertWorker.exe'}, @{Directory=$WorkerDirectory;Name='Lims.Infrastructure.dll'}, @{Directory=$ApiDirectory;Name='Lims.Api.dll'}, @{Directory=$ApiDirectory;Name='Lims.Infrastructure.dll'})) {
    if ([string]::IsNullOrWhiteSpace($entry.Directory)) { continue }
    $path = Join-Path $entry.Directory $entry.Name
    try {
        $file = Get-Item -LiteralPath $path -ErrorAction Stop
        $report.Files += [ordered]@{Path=$file.FullName;Version=$file.VersionInfo.FileVersion;Modified=$file.LastWriteTimeUtc.ToString('o');SHA256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
    } catch { $report.Files += [ordered]@{Path=$path;Status='Unavailable:' + $_.Exception.GetType().Name} }
}
if ($WorkerDirectory) {
    try {
        $config = Get-Content -LiteralPath (Join-Path $WorkerDirectory 'appsettings.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $smtp = $config.Alerts.Smtp
        $report.WorkerConfiguration = [ordered]@{
            Source='appsettings.json only; environment/account overrides are not inspected'
            ConnectionPresent=(-not [string]::IsNullOrWhiteSpace($config.ConnectionStrings.Lims))
            SmtpEnabled=($smtp.Enabled -eq $true)
            SmtpHostPresent=(-not [string]::IsNullOrWhiteSpace($smtp.Host))
            SmtpSenderPresent=(-not [string]::IsNullOrWhiteSpace($smtp.Sender))
        }
        if (-not $LogDirectory -and $config.Alerts.LogDirectory) {
            $LogDirectory = $config.Alerts.LogDirectory
            if (-not [IO.Path]::IsPathRooted($LogDirectory)) { $LogDirectory = Join-Path $WorkerDirectory $LogDirectory }
        }
    } catch { $report.WorkerConfiguration = [ordered]@{Status='Unavailable:' + $_.Exception.GetType().Name} }
    if (-not $LogDirectory) { $LogDirectory = Join-Path $WorkerDirectory 'logs' }
}
if ($LogDirectory) {
    try {
        $logs = @(Get-ChildItem -LiteralPath $LogDirectory -Filter 'alerts-*.jsonl' -File -ErrorAction Stop | Sort-Object Name -Descending | Select-Object -First 2)
        $report.LogStatus = if($logs.Count){'Read'}else{'NoMatchingFiles'}
        foreach ($log in $logs) {
            foreach ($line in (Get-Content -LiteralPath $log.FullName -Tail 30 -Encoding UTF8)) {
                try {
                    $event = $line | ConvertFrom-Json
                    $report.RecentEvents += [ordered]@{At=$event.At;State=$event.State;Type=$event.Result.Type;SqlState=$event.Result.SqlState;Acquired=$event.Result.Acquired;Evaluated=$event.Result.Evaluated;Changed=$event.Result.Changed;Sent=$event.Result.Sent;RunId=$event.Result.RunId}
                } catch { $report.LogStatus = 'ContainsUnreadableLines' }
            }
        }
    } catch { $report.LogStatus = 'Unavailable:' + $_.Exception.GetType().Name }
}
$report | ConvertTo-Json -Depth 10
