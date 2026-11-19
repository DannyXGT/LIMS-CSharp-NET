$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'validate-planning-desktop.ps1'))
$targetedScriptsRoot=$PSScriptRoot;$start=$source.IndexOf('$ErrorActionPreference=');$end=$source.IndexOf("if(`$Action -eq 'Inspect')")
Invoke-Expression $source.Substring($start,$end-$start).Replace('$PSScriptRoot','$targetedScriptsRoot')
$process=Get-Process -Id $window.Current.ProcessId
if(-not $process.Path.StartsWith((Join-Path $workspace 'artifacts/planificacion-dev/desktop-validation-'),[StringComparison]::OrdinalIgnoreCase)){throw 'Solo cliente aislado autorizado'}
$settings=Get-Content (Join-Path $local 'desktop-validation.json') -Raw|ConvertFrom-Json
if($settings.ApiUrl -ne 'https://localhost:7441'){throw 'Solo API local autorizada'}
$account=Get-Content (Join-Path $local 'acceptance-account.json') -Raw|ConvertFrom-Json
# Targeted UI Automation values: no global keyboard injection, no password readback, no Windows login or screen-saver changes.
foreach($id in @('IdentifierBox','PasswordInput')){
    $control=$window.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$id))
    if(-not $control -or $control.Current.ProcessId -ne $process.Id){throw 'Control de login aislado no disponible'}
    $pattern=$control.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
    if($pattern.Current.IsReadOnly){throw 'Control de login no editable'}
    $value=if($id -eq 'IdentifierBox'){$account.email.Split('@')[0]}else{$account.password}
    $pattern.SetValue($value)
}
Invoke-Named 'Iniciar sesión';Find-Named 'Elegir espacio de trabajo'|Out-Null
Invoke-Named 'Planificación';Find-Named 'Consulta de Jobs'|Out-Null;Start-Sleep -Milliseconds 900
Capture-Window $window 'estado-consulta-inicial'
Write-Output 'PASS: login original mediante ValuePattern dirigido a los controles del cliente local; sin entrada global de credenciales'
