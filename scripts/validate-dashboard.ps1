param([switch]$SkipStart,[switch]$NavigationOnly)
$ErrorActionPreference='Stop'
$driver=Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$folder='dashboard'
$evidence=Join-Path $PSScriptRoot '..\artifacts\validation\dashboard'
function Send($value) { & $driver -Action Command -Command ($value|ConvertTo-Json -Compress) -EvidenceFolder $folder }
function Capture($name) { Start-Sleep -Milliseconds 300; & $driver -Action Capture -Name $name -EvidenceFolder $folder | Out-Null }
function Report { Send @{action='dashboardReport'}; Get-Content (Join-Path $evidence 'dashboard-layout.json') -Raw | ConvertFrom-Json }
function Check($condition,$message) { if(-not $condition){throw $message} }
if(-not $SkipStart) {
    $resolvedEvidence=[IO.Path]::GetFullPath($evidence)
    foreach($fixture in Get-Process -Name 'Lims.VisualValidation.dashboard','Lims.Desktop.VisualHarness' -ErrorAction SilentlyContinue) {
        if($fixture.Path -and $fixture.Path.StartsWith($resolvedEvidence+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { Stop-Process -Id $fixture.Id }
    }
    & $driver -Action Start -SkipBuild -IsolatedIdentity -EvidenceFolder $folder
}
$deadline=[DateTime]::UtcNow.AddSeconds(40)
while(-not (Test-Path (Join-Path $evidence 'window.json'))) { if([DateTime]::UtcNow -gt $deadline){throw 'WinUI no inició'};Start-Sleep -Milliseconds 100 }
if(-not $NavigationOnly) {
foreach($theme in @('dark','light')) {
    Send @{action=$theme}
    foreach($size in @(@{width=1920;height=1080},@{width=1600;height=900},@{width=1366;height=768})) {
        Send @{action='size';width=$size.width;height=$size.height}
        foreach($scenario in @('Administrator','Laboratory','Purchasing','Limited','Empty','Error','Restricted')) {
            Send @{action='dashboard';scenario=$scenario}
            Send @{action='dashboardScroll';offset=0}
            $name="$theme-$scenario-$($size.width)x$($size.height)"
            Capture $name
            $report=Report
            Check ($report.extentWidth -le $report.scrollWidth+2) "Recorte horizontal: $name"
            if($scenario -eq 'Error') { Check ($null -eq $report.data -and $report.Error.Length -gt 0) 'Error no debe mostrar ceros ni datos antiguos' }
            if($scenario -eq 'Empty') { Check ($report.data.Standards.Registered -eq 0 -and $report.Error.Length -eq 0) 'Cero real debe ser consulta exitosa' }
            if($scenario -eq 'Limited') { Check ($null -eq $report.data.Standards -and $null -eq $report.data.Intermediate -and $null -ne $report.data.Stock) 'Datos limitados deben respetar permisos' }
            if($scenario -eq 'Purchasing') { Check ($null -eq $report.data.Standards -and $report.data.Activity.Count -eq 0) 'Compras debe permanecer pendiente sin inventario' }
            $report|ConvertTo-Json -Depth 15|Set-Content (Join-Path $evidence ($name+'.json')) -Encoding UTF8
            if($scenario -in @('Administrator','Laboratory')) {
                Send @{action='dashboardScroll';offset=10000}
                Capture ($name+'-scroll')
                $bottom=Report
                Check ([Math]::Abs($bottom.VerticalOffset-$bottom.ScrollableHeight) -lt 2) "Scroll inaccesible: $name"
            }
        }
    }
}
}
# Native UI Automation exercises actionable cards, supported state filters, and actual sibling preparation tabs.
Send @{action='dark'}
Send @{action='dashboard';scenario='Administrator'}
Send @{action='dashboardScroll';offset=0}
& $driver -Action Invoke -Name 'Estándares vencidos: 3. Abrir módulo' -EvidenceFolder $folder
Start-Sleep -Milliseconds 500
$route=Report
Check ($route.module -eq 'standards' -and $route.filter -eq 'Expired') 'Tarjeta vencidos debe abrir Estándares con filtro real'
Capture 'navigation-expired-filter'
Send @{action='dashboard';scenario='Administrator'}
Send @{action='dashboardScroll';offset=0}
& $driver -Action Invoke -Name 'Naphthol AS' -EvidenceFolder $folder
$detailDeadline=[DateTime]::UtcNow.AddSeconds(8)
do { Start-Sleep -Milliseconds 100; $detail=Report } while (-not $detail.detailOpen -and [DateTime]::UtcNow -lt $detailDeadline)
Check ($detail.detailOpen) 'Enlace de vencimiento debe abrir la ficha real por ID'
Capture 'navigation-standard-detail'
& $driver -Action Invoke -Name 'Cerrar ficha' -EvidenceFolder $folder
Start-Sleep -Milliseconds 250
foreach($label in @('Stock','Intermedia','Gestión de permisos')) {
    Send @{action='dashboard';scenario='Administrator'}
    Send @{action='dashboardScroll';offset=10000}
    & $driver -Action Invoke -Name $label -EvidenceFolder $folder
    Start-Sleep -Milliseconds 500
    $navigationReport=Report
    if($label -eq 'Stock') { Check ($navigationReport.module -eq 'preparations' -and $navigationReport.preparationTab -eq 0) 'Acceso Stock debe activar tab Stock' }
    if($label -eq 'Intermedia') { Check ($navigationReport.module -eq 'preparations' -and $navigationReport.preparationTab -eq 1) 'Acceso Intermedia debe activar tab Intermedia' }
    if($label -eq 'Gestión de permisos') { Check ($navigationReport.module -eq 'permissions') 'Acceso Permisos debe abrir módulo administrativo' }
    Capture ('navigation-'+$label)
}
Send @{action='dashboard';scenario='Limited'}
& $driver -Action Inspect -Name 'limited-uia' -EvidenceFolder $folder | Out-Null
$uia=Get-Content (Join-Path $evidence 'limited-uia.json') -Raw | ConvertFrom-Json
Check (-not ($uia | Where-Object { $_.Name -eq 'Gestión de permisos' -and $_.Type -eq 'ControlType.Button' })) 'Usuario limitado no debe tener acceso administrativo'
$matrixCount=0
foreach($matrixTheme in @('dark','light')) {
    foreach($matrixResolution in @('1920x1080','1600x900','1366x768')) {
        foreach($matrixScenario in @('Administrator','Laboratory','Purchasing','Limited','Empty','Error','Restricted')) {
            $matrixName="$matrixTheme-$matrixScenario-$matrixResolution"
            $matrixPath=Join-Path $evidence ($matrixName+'.json')
            Check ((Test-Path $matrixPath) -and (Test-Path (Join-Path $evidence ($matrixName+'.png')))) "Falta evidencia de matriz: $matrixName"
            $matrix=Get-Content $matrixPath -Raw | ConvertFrom-Json
            Check ($matrix.fixtureOnly -and $matrix.extentWidth -le $matrix.scrollWidth+2) "Matriz sin validar: $matrixName"
            $matrixCount++
        }
    }
}
@{passed=$true;matrixScenarios=$matrixCount;fixtureOnly=$true;themes=@('Dark','Light');resolutions=@('1920x1080','1600x900','1366x768');scenarios=@('Administrador','Laboratorio','Compras pendiente','Limitado','Vacío','Error API','Sin permisos');scope='Pantalla y shell reales compilados con datos ficticios aislados, sin API remota ni InterDB'} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $evidence 'validation.json') -Encoding UTF8
Write-Output 'Validación del dashboard completada.'
