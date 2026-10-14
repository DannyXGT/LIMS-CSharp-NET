param([switch]$SkipStart)
$ErrorActionPreference = 'Stop'
$driver = Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$folder = 'permissions-console'
$evidence = Join-Path $PSScriptRoot '..\artifacts\validation\permissions-console'
function Send-Command($data) { & $driver -Action Command -Command ($data | ConvertTo-Json -Compress) -EvidenceFolder $folder }
function Capture($name) { Start-Sleep -Milliseconds 220; & $driver -Action Capture -Name $name -EvidenceFolder $folder | Out-Null }
if (-not $SkipStart) {
    $resolvedEvidence = [IO.Path]::GetFullPath($evidence)
    foreach ($fixtureProcess in Get-Process -Name 'Lims.Desktop.VisualHarness' -ErrorAction SilentlyContinue | Where-Object { $_.Path.StartsWith($resolvedEvidence + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) }) {
        Stop-Process -Id $fixtureProcess.Id
    }
    & $driver -Action Start -SkipBuild -IsolatedIdentity -EvidenceFolder $folder
}
$deadline = [DateTime]::UtcNow.AddSeconds(40)
while (-not (Test-Path -LiteralPath (Join-Path $evidence 'window.json'))) {
    if ([DateTime]::UtcNow -ge $deadline) { throw 'No inició la validación WinUI. Revisar crash.txt y launch.err.log.' }
    Start-Sleep -Milliseconds 100
}
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$handle = [IntPtr](Get-Content -LiteralPath (Join-Path $evidence 'window.json') -Raw | ConvertFrom-Json).hwnd
$window = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
function Find-Control($name, $type = $null) {
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    if ($null -ne $type) { $condition = [System.Windows.Automation.AndCondition]::new($condition, [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $type)) }
    $controls = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    $control = $controls | Sort-Object @{Expression={ $_.Current.IsKeyboardFocusable };Descending=$true} | Select-Object -First 1
    if ($null -eq $control) { throw "No se encontró el control: $name" }
    return $control
}
function Invoke-Control($name) { (Find-Control $name).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Toggle-Permission($name) { (Find-Control $name ([System.Windows.Automation.ControlType]::CheckBox)).GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }
function Read-Layout { Send-Command @{action='permissionReport'}; return Get-Content -LiteralPath (Join-Path $evidence 'permission-layout.json') -Raw | ConvertFrom-Json }
function Check($condition, $description) { if (-not $condition) { throw "Validación fallida: $description" } }
function Wait-For($condition) {
    $waitDeadline = [DateTime]::UtcNow.AddSeconds(8)
    do { if (& $condition) { return }; Start-Sleep -Milliseconds 40 } while ([DateTime]::UtcNow -lt $waitDeadline)
    throw 'La interfaz no alcanzó el estado esperado.'
}
function Verify-Layout($name) {
    $layout = Read-Layout
    foreach ($element in $layout.elements | Where-Object { $_.visibility -eq 'Visible' -and $_.name -in @('UsersPanel','UserSearch','UsersList','DetailPanel','UserHeader','PermissionTabs','TabContent','WorkspaceFooter') }) {
        # WinUI rounds nested rows to physical pixels at 125% DPI (up to 2 DIP).
        Check ($element.y -ge -2 -and $element.y + $element.height -le $layout.height + 2) "$name / $($element.name) debe quedar dentro de la ventana"
        Check ($element.x -ge -2 -and $element.x + $element.width -le $layout.width + 2) "$name / $($element.name) no debe recortarse horizontalmente"
    }
    $content = $layout.elements | Where-Object name -eq 'TabContent'
    $footer = $layout.elements | Where-Object name -eq 'WorkspaceFooter'
    Check ($content.height -gt 100 -and $content.y + $content.height -le $footer.y + 1) "$name / scroll independiente sin invadir las acciones"
    $layout | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence ($name + '-layout.json')) -Encoding utf8
}

Send-Command @{action='size';width=1366;height=768}
Send-Command @{action='permissions'}
Send-Command @{action='permissionSelect';index=0}
Wait-For { -not (Find-Control 'Guardar cambios' ([System.Windows.Automation.ControlType]::Button)).Current.IsEnabled }
Check (-not (Read-Layout).CanSave) 'Guardar deshabilitado sin cambios'
Capture '01-permisos-1366x768'
Verify-Layout '1366x768'

# Real pointer input and native checkbox accessibility, then TogglePattern for revocation.
$grant = Find-Control 'Registrar estándares' ([System.Windows.Automation.ControlType]::CheckBox)
Check ($grant.Current.IsKeyboardFocusable) 'El permiso debe conservar foco de teclado'
Check ($null -ne $grant.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)) 'El permiso debe conservar TogglePattern de checkbox'
Toggle-Permission 'Registrar estándares'
Wait-For { (Read-Layout).SelectedCount -eq 4 }
Check ((Read-Layout).SelectedCount -eq 4) 'Asignar actualiza el contador local'
Check ((Read-Layout).SavedCount -eq 3) 'La selección local no modifica el resumen guardado'
Toggle-Permission 'Consultar estándares'
Check ((Read-Layout).HasPendingChanges) 'Revocar conserva cambios pendientes'
Capture '02-cambios-locales'

Send-Command @{action='permissionFailSave';fail=$true}
Invoke-Control 'Guardar cambios'
Start-Sleep -Milliseconds 100
Check (-not (Find-Control 'Guardar cambios' ([System.Windows.Automation.ControlType]::Button)).Current.IsEnabled) 'Bloqueo de doble envío'
Capture '03-guardando'
Wait-For { (Find-Control 'Guardar cambios' ([System.Windows.Automation.ControlType]::Button)).Current.IsEnabled }
$failed = Read-Layout
Check ($failed.HasPendingChanges -and $failed.SavedCount -eq 3 -and $failed.SaveCalls -eq 1) 'Error conserva las selecciones y no simula un guardado'
Capture '04-error-cambios-preservados'

(Find-Control 'Carlos Méndez' ([System.Windows.Automation.ControlType]::ListItem)).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Capture '05-aviso-cambios-pendientes'
Invoke-Control 'Seguir editando'
Wait-For { (Find-Control 'Ana López' ([System.Windows.Automation.ControlType]::ListItem)).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected }
Check ((Read-Layout).HasPendingChanges) 'Cancelar cambio de usuario conserva la matriz'

Send-Command @{action='permissionFailSave';fail=$false}
Invoke-Control 'Guardar cambios'
Wait-For { -not (Read-Layout).IsSaving }
$saved = Read-Layout
Check (-not $saved.HasPendingChanges -and -not $saved.CanSave -and $saved.SaveCalls -eq 2) 'Solo la respuesta exitosa de la API aislada confirma el guardado'
Capture '06-guardado-confirmado-fixture'
Toggle-Permission 'Editar estándares'
Invoke-Control 'Descartar'
Check (-not (Read-Layout).HasPendingChanges -and (Read-Layout).SaveCalls -eq 2) 'Descartar restaura lo guardado sin enviar una petición'

(Find-Control 'Historial de cambios' ([System.Windows.Automation.ControlType]::TabItem)).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Wait-For { (Read-Layout).HistoryTotal -eq 38 }
Capture '07-historial-1366x768'
$history = Read-Layout
Check (($history.elements | Where-Object name -eq 'PermissionScroll').visibility -eq 'Collapsed') 'La matriz no se apila debajo del historial'
Invoke-Control 'Más historial'
Wait-For { (Read-Layout).HistoryPage -eq 2 }
Capture '08-historial-segunda-pagina'
Invoke-Control 'Página anterior'

foreach ($size in @(@{width=1600;height=900}, @{width=1920;height=1080})) {
    Send-Command @{action='size';width=$size.width;height=$size.height}
    Send-Command @{action='permissionTab';index=0}
    $resolution = "$($size.width)x$($size.height)"
    Verify-Layout $resolution
    Capture ('09-permisos-' + $resolution)
    Send-Command @{action='permissionTab';index=1}
    Capture ('10-historial-' + $resolution)
}

# All four states use fixture data in the existing real page, never InterDB.
Send-Command @{action='size';width=1366;height=768}
Send-Command @{action='permissionTab';index=0}
Send-Command @{action='permissionSelect';index=1}
$unconfigured = Read-Layout
Check (-not $unconfigured.IsConfigured -and -not $unconfigured.CanSave) 'Sin configuración individual no activa Guardar sin cambios'
Send-Command @{action='permissionScroll';offset=1200}
Capture '11-sin-configuracion-individual'
Invoke-Control 'Configurar permisos individuales vacíos'
Capture '12-confirmacion-matriz-vacia'
Invoke-Control 'Cancelar'
Check ((Read-Layout).SaveCalls -eq 2) 'Cancelar matriz vacía no guarda'
Send-Command @{action='permissionSelect';index=2}
Check ((Read-Layout).IsConfigured -and (Read-Layout).SavedCount -eq 0) 'Configuración existente con cero permisos'
Capture '13-configurado-sin-permisos'
Send-Command @{action='permissionSelect';index=3}
Check (-not (Read-Layout).CanSave) 'Administrador mantiene acceso por rol sin activar Guardar'
Capture '14-administrador-acceso-por-rol'
Send-Command @{action='permissionSelect';index=0}
& $driver -Action Inspect -Name 'console-uia' -EvidenceFolder $folder | Out-Null
@{fixtureOnly=$true;scope='Pantalla real compilada con datos aislados; sin SQL ni cambios en usuarios reales';checks='Selección, foco, TogglePattern, concesión, revocación, doble envío, error, descarte, aviso, tabs, paginación, cuatro estados y geometría';resolutions=@('1366x768','1600x900','1920x1080');passed=$true} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'console-validation.json') -Encoding utf8
Write-Output 'Validación de consola aprobada. Capturas y reportes en artifacts/validation/permissions-console.'
