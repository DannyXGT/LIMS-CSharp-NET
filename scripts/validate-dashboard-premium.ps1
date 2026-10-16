param([switch]$SkipStart, [string]$EvidenceFolder = 'dashboard-premium')
$ErrorActionPreference='Stop'
$driver=Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$folder=$EvidenceFolder
$evidence=Join-Path $PSScriptRoot ('..\artifacts\validation\'+$folder)
function Send($data) { & $driver -Action Command -Command ($data|ConvertTo-Json -Compress) -EvidenceFolder $folder }
function Check($condition,$message) { if(-not $condition){throw $message} }
function Report { Send @{action='dashboardReport'}; Get-Content (Join-Path $evidence 'dashboard-layout.json') -Raw|ConvertFrom-Json }
if(-not $SkipStart){
    $root=[IO.Path]::GetFullPath($evidence)+[IO.Path]::DirectorySeparatorChar
    Get-Process -Name ('Lims.VisualValidation.'+($folder -replace '[^A-Za-z0-9.-]', '')) -ErrorAction SilentlyContinue|Where-Object {$_.Path -and $_.Path.StartsWith($root,[StringComparison]::OrdinalIgnoreCase)}|Stop-Process
    & $driver -Action Start -SkipBuild -IsolatedIdentity -EvidenceFolder $folder
}
$deadline=[DateTime]::UtcNow.AddSeconds(30)
while(-not (Test-Path (Join-Path $evidence 'window.json'))){if([DateTime]::UtcNow -gt $deadline){throw 'No abrió WinUI.'};Start-Sleep -Milliseconds 100}
$cases=@()
foreach($size in @(@{width=1920;height=1080},@{width=1600;height=900},@{width=1366;height=768})){
    Send @{action='size';width=$size.width;height=$size.height}
    foreach($scenario in @('Administrator','Healthy','Laboratory','Limited','Empty','Error','Purchasing','Restricted')){
        Send @{action='dashboard';scenario=$scenario}
        Send @{action='dashboardScroll';offset=0}
        $r=Report
        $name="$scenario-$($size.width)x$($size.height)"
        Check ($r.extentWidth -le $r.scrollWidth+2) "Desbordamiento horizontal: $name"
        Check (-not $r.shellHeaderVisible -and $r.theme -eq 'Dark') "Cabecera o tema incorrectos: $name"
        if($r.metricHeights.Count -gt 1){Check ((($r.metricHeights|Measure-Object -Maximum).Maximum-($r.metricHeights|Measure-Object -Minimum).Minimum) -lt 2) "KPI con alturas diferentes: $name"}
        if($scenario -eq 'Administrator'){Check ($r.analyticsPanelCount -eq 3) 'Faltan paneles analíticos.'}
        if($scenario -eq 'Limited'){Check ($null -eq $r.data.Standards -and $null -ne $r.data.Stock -and $null -eq $r.data.Intermediate) 'Datos no autorizados.'}
        if($scenario -eq 'Error'){Check ($null -eq $r.data -and $r.Error.Length -gt 0 -and $r.retryEnabled) 'Error sin recuperación.'}
        if($scenario -eq 'Empty'){Check ($r.data.Standards.Registered -eq 0) 'Ceros reales sustituidos.'}
        $capture=& $driver -Action Capture -Name $name -EvidenceFolder $folder
        $r|ConvertTo-Json -Depth 16|Set-Content (Join-Path $evidence ($name+'.json')) -Encoding utf8
        $cases+=@{name=$name;dpi=$capture.Dpi}
        if($scenario -eq 'Administrator'){
            Send @{action='dashboardScroll';offset=450}
            & $driver -Action Capture -Name ($name+'-charts') -EvidenceFolder $folder|Out-Null
            Send @{action='dashboardScroll';offset=10000}
            $bottom=Report
            Check ([Math]::Abs($bottom.VerticalOffset-$bottom.ScrollableHeight) -lt 2) 'El contenido inferior no es accesible.'
            & $driver -Action Capture -Name ($name+'-bottom') -EvidenceFolder $folder|Out-Null
        }
    }
}
Send @{action='light'}
Send @{action='dashboard';scenario='Administrator'}
Check ((Report).theme -eq 'Dark') 'Inicio perdió el tema oscuro al cambiar el tema heredado.'
Send @{action='dark'}
Send @{action='dashboardScroll';offset=0}
& $driver -Action Invoke -Name 'Estándares vencidos: 3. Abrir módulo' -EvidenceFolder $folder
Start-Sleep -Milliseconds 500
$route=Report
Check ($route.module -eq 'standards' -and $route.filter -eq 'Expired') 'Falló la navegación de vencidos.'
Send @{action='dashboard';scenario='Administrator'}
@{passed=$true;fixtureOnly=$true;matrix=$cases;limitations='DPI real del escritorio. No se cambió la escala de Windows. Esta evidencia aislada se complementa con capturas de una sesión real.'}|ConvertTo-Json -Depth 6|Set-Content (Join-Path $evidence 'validation.json') -Encoding utf8
Write-Output 'Dashboard completo verificado con datos aislados.'
