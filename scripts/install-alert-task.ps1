param(
    [Parameter(Mandatory)][string]$WorkerExe,
    [string]$TaskName = 'LIMS Alertas',
    [ValidateRange(1,60)][int]$EveryMinutes = 5,
    [switch]$Install,
    [string]$ServiceAccount
)
$ErrorActionPreference = 'Stop'
$worker = (Resolve-Path -LiteralPath $WorkerExe).Path
if ([IO.Path]::GetFileName($worker) -ne 'Lims.AlertWorker.exe') { throw 'Seleccione el ejecutable publicado Lims.AlertWorker.exe.' }
if (-not $Install) {
    # A reviewable definition must not require CIM privileges or alter Task Scheduler.
    $command = [Security.SecurityElement]::Escape($worker)
    $workingDirectory = [Security.SecurityElement]::Escape([IO.Path]::GetDirectoryName($worker))
    $start = (Get-Date).AddMinutes(1).ToString('s')
    @"
<?xml version="1.0" encoding="UTF-8"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo><Description>Motor central LIMS y cola persistente SMTP. Configurar la cuenta empresarial al instalar.</Description></RegistrationInfo>
  <Triggers><BootTrigger><Enabled>true</Enabled><Delay>PT1M</Delay></BootTrigger><TimeTrigger><Repetition><Interval>PT$($EveryMinutes)M</Interval><StopAtDurationEnd>false</StopAtDurationEnd></Repetition><StartBoundary>$start</StartBoundary><Enabled>true</Enabled></TimeTrigger></Triggers>
  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><StartWhenAvailable>true</StartWhenAvailable><Enabled>true</Enabled><ExecutionTimeLimit>PT5M</ExecutionTimeLimit><RestartOnFailure><Interval>PT1M</Interval><Count>2</Count></RestartOnFailure></Settings>
  <Actions><Exec><Command>$command</Command><WorkingDirectory>$workingDirectory</WorkingDirectory></Exec></Actions>
</Task>
"@
    return
}
$action = New-ScheduledTaskAction -Execute $worker -WorkingDirectory ([IO.Path]::GetDirectoryName($worker))
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes $EveryMinutes)
$boot = New-ScheduledTaskTrigger -AtStartup
$settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Minutes 5) -RestartCount 2 -RestartInterval (New-TimeSpan -Minutes 1)
if ([string]::IsNullOrWhiteSpace($ServiceAccount)) { throw 'Indique la cuenta empresarial que ejecutará la tarea con su configuración segura de servidor.' }
# S4U does not grant network credentials. A managed service account provides unattended SMTP/DB access without embedding passwords here.
$principal = New-ScheduledTaskPrincipal -UserId $ServiceAccount -LogonType ServiceAccount -RunLevel Limited
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger @($trigger, $boot) -Settings $settings -Principal $principal
