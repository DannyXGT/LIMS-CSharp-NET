param([switch]$SkipStart)
$ErrorActionPreference = 'Stop'
$driver = Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$folder = 'permissions'
function Send-Command($data) { & $driver -Action Command -Command ($data | ConvertTo-Json -Compress) -EvidenceFolder $folder }
function Capture($name) { Start-Sleep -Milliseconds 350; & $driver -Action Capture -Name $name -EvidenceFolder $folder }
if (-not $SkipStart) { & $driver -Action Start -SkipBuild -IsolatedIdentity -EvidenceFolder $folder }
$evidence = Join-Path $PSScriptRoot '..\artifacts\validation\permissions'
$deadline = [DateTime]::UtcNow.AddSeconds(40)
while (-not (Test-Path -LiteralPath (Join-Path $evidence 'window.json'))) {
    if ([DateTime]::UtcNow -ge $deadline) { throw 'Visual harness did not launch. Inspect the launch logs.' }
    Start-Sleep -Milliseconds 200
}
Send-Command @{ action = 'size'; width = 1366; height = 768 }
Send-Command @{ action = 'permissions' }
Capture '01-listado-1366x768'
Send-Command @{ action = 'permissionSelect'; index = 0 }
Capture '02-usuario-matriz-1366x768'
Send-Command @{ action = 'permissionToggle'; code = 'stock.create' }
Send-Command @{ action = 'permissionScroll'; offset = 220 }
Capture '03-cambios-pendientes-1366x768'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$handle = [IntPtr](Get-Content -LiteralPath (Join-Path $evidence 'window.json') -Raw | ConvertFrom-Json).hwnd
$window = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
function Find-UserRow($name) {
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $element = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    while ($null -ne $element -and $element.Current.ControlType -ne [System.Windows.Automation.ControlType]::ListItem) {
        $element = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($element)
    }
    if ($null -eq $element) { throw "User row is not accessible: $name" }
    return $element
}
$carlos = Find-UserRow 'Carlos Méndez'
$selection = $carlos.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
$selection.Select()
Capture '08-aviso-cambios-sin-guardar'
& $driver -Action Invoke -Name 'Seguir editando' -EvidenceFolder $folder
Start-Sleep -Milliseconds 250
$ana = Find-UserRow 'Ana López'
if (-not $ana.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { throw 'Canceling discard did not preserve the selected user.' }
& $driver -Action Inspect -Name 'matriz-accesible-uia' -EvidenceFolder $folder
$matrixReport = Get-Content -LiteralPath (Join-Path $evidence 'matriz-accesible-uia.json') -Raw | ConvertFrom-Json
if ($matrixReport | Where-Object { $_.Name -match 'PermissionUser|Id =|Name =|Department =' }) { throw 'Accessible user rows expose technical identifiers.' }
Send-Command @{ action = 'permissionFailSave'; fail = $true }
Send-Command @{ action = 'permissionSave' }
Capture '04-error-red-cambios-preservados'
Send-Command @{ action = 'permissionFailSave'; fail = $false }
Send-Command @{ action = 'permissionSave' }
Capture '05-guardado-exitoso'
Send-Command @{ action = 'permissionScroll'; offset = 1000 }
Capture '06-historial-1366x768'
Send-Command @{ action = 'permissionCommon' }
Capture '07-usuario-comun-acceso-denegado'
& $driver -Action Inspect -Name 'usuario-comun-uia' -EvidenceFolder $folder
$common = Get-Content -LiteralPath (Join-Path $evidence 'usuario-comun-uia.json') -Raw | ConvertFrom-Json
if ($common | Where-Object { $_.Name -match 'Gestión de permisos|Ana López|Guardar cambios|Consultar Stock|Estándares|Preparaciones' -and -not $_.Offscreen }) { throw 'Common user still sees protected content.' }
