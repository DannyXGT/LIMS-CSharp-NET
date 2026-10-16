param([switch]$SkipStart, [string]$EvidenceFolder = 'dashboard-design-system')
$ErrorActionPreference='Stop'
$driver=Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$evidence=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot ('../artifacts/validation/'+$EvidenceFolder)))
function Send($data){& $driver -Action Command -EvidenceFolder $EvidenceFolder -Command ($data|ConvertTo-Json -Compress)}
function Check($ok,$message){if(-not $ok){throw $message}}
function Report{Send @{action='dashboardReport'};Get-Content (Join-Path $evidence 'dashboard-layout.json') -Raw|ConvertFrom-Json}
function Capture($name){& $driver -Action Capture -EvidenceFolder $EvidenceFolder -Name $name}
if(-not $SkipStart){
 $prefix=$evidence+[IO.Path]::DirectorySeparatorChar
 Get-Process -Name ('Lims.VisualValidation.'+($EvidenceFolder -replace '[^A-Za-z0-9.-]','')) -ErrorAction SilentlyContinue|Where-Object{$_.Path -and $_.Path.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)}|Stop-Process
 & $driver -Action Start -SkipBuild -IsolatedIdentity -EvidenceFolder $EvidenceFolder
}
$deadline=[DateTime]::UtcNow.AddSeconds(30)
while(-not (Test-Path (Join-Path $evidence 'window.json'))){if([DateTime]::UtcNow -gt $deadline){throw 'WinUI did not open.'};Start-Sleep -Milliseconds 100}
Add-Type -AssemblyName System.Drawing,UIAutomationClient,UIAutomationTypes
$cases=@()
foreach($size in @(@{width=1600;height=900},@{width=1366;height=768})){
 foreach($theme in @('Dark','Light')){
  $suffix="$($theme.ToLowerInvariant())-$($size.width)x$($size.height)"
  Send @{action='size';width=$size.width;height=$size.height}
  Send @{action=$theme.ToLowerInvariant()}
  Send @{action='dashboard';scenario='Administrator'}
  Send @{action='pane';open=$false}
  Send @{action='standards'}
  $deadline=[DateTime]::UtcNow.AddSeconds(10)
  do{$r=Report;if($r.standardsBusy -or -not $r.standardsLoaded){Start-Sleep -Milliseconds 100}}while(($r.standardsBusy -or -not $r.standardsLoaded) -and [DateTime]::UtcNow -lt $deadline)
  Check ($r.module -eq 'standards' -and -not $r.standardsBusy) 'Standards did not load.'
  Send @{action='standardsDesignReference'}
  $standard=Capture "standards-$suffix"
  & $driver -Action HoverCapture -EvidenceFolder $EvidenceFolder -Name 'Aplicar filtros' -CaptureName "standards-hover-$suffix"|Out-Null
  & $driver -Action PressCapture -EvidenceFolder $EvidenceFolder -Name 'Aplicar filtros' -CaptureName "standards-pressed-$suffix"|Out-Null
  Send @{action='dashboard';scenario='Administrator'}
  Send @{action='dashboardScroll';offset=0}
  $r=Report
  Check ($r.theme -eq $theme) "Theme inheritance failed: $suffix"
  Check ($r.extentWidth -le $r.scrollWidth+2) "Horizontal overflow: $suffix"
  Check ($r.analyticsPanelCount -eq 3) 'Missing analytical charts.'
  $homeCapture=Capture "home-$suffix"
  Send @{action='dashboardDesignReport'}
  $design=Get-Content (Join-Path $evidence 'dashboard-design.json') -Raw|ConvertFrom-Json
  Check ($design.dashboardSurface -eq $design.standardsSurface) "Surfaces differ: $suffix"
  Check ($design.dashboardBorder -eq $design.standardsBorder) "Borders differ: $suffix"
  Check ($design.dashboardRadius -eq $design.standardsRadius) "Radii differ: $suffix"
  Check ($design.smallestText -ge 9) "Undersized text: $suffix"
  Check ($design.dashboardTitleSize -eq $design.standardsTitleSize) 'Page titles differ.'
  $design|ConvertTo-Json -Depth 6|Set-Content (Join-Path $evidence "design-$suffix.json") -Encoding utf8
  $overview=@($r.elements|Where-Object name -eq OverviewGrid)[0]
  Send @{action='dashboardScroll';offset=[Math]::Max(0,$overview.y-60)}
  & $driver -Action HoverCapture -EvidenceFolder $EvidenceFolder -Name 'Ver todos' -CaptureName "home-hover-$suffix"|Out-Null
  & $driver -Action PressCapture -EvidenceFolder $EvidenceFolder -Name 'Ver todos' -CaptureName "home-pressed-$suffix"|Out-Null
  # Only read-navigation actions are exercised. Restore the page if release navigates.
  $pointerRoute=(Report).module
  Send @{action='dashboard';scenario='Administrator'}
  Send @{action='dashboardScroll';offset=[Math]::Max(0,$overview.y-60)}
  $rowName=$design.rows|Select-Object -First 1
  Check ($rowName.Length -gt 0) 'Analytical row lacks an accessible name.'
  & $driver -Action HoverCapture -EvidenceFolder $EvidenceFolder -Name $rowName -CaptureName "row-hover-$suffix"|Out-Null
  & $driver -Action PressCapture -EvidenceFolder $EvidenceFolder -Name $rowName -CaptureName "row-pressed-$suffix"|Out-Null
  Start-Sleep -Milliseconds 250
  Send @{action='closeDetail'}
  Send @{action='dashboard';scenario='Administrator'}
  Send @{action='dashboardScroll';offset=[Math]::Max(0,$overview.y-60)}
  $state=Get-Content (Join-Path $evidence 'window.json') -Raw|ConvertFrom-Json
  $window=[System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$state.hwnd)
  $row=$window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.AndCondition]::new([System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$rowName),[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem)))
  $row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Capture "row-selected-$suffix"|Out-Null
  Check ((Report).module -eq 'home') 'Dashboard did not restore after read-navigation capture.'
  $analytics=@($r.elements|Where-Object name -eq AnalyticsGrid)[0]
  Send @{action='dashboardScroll';offset=[Math]::Max(0,$analytics.y-60)}
  Capture "charts-$suffix"|Out-Null
  Send @{action='dashboardScroll';offset=10000}
  $bottom=Report
  Check ([Math]::Abs($bottom.VerticalOffset-$bottom.ScrollableHeight) -lt 2) 'Bottom panels are unreachable.'
  Capture "bottom-$suffix"|Out-Null
  $left=[Drawing.Image]::FromFile($homeCapture.Path);$right=[Drawing.Image]::FromFile($standard.Path)
  $comparison=[Drawing.Bitmap]::new($left.Width+$right.Width,[Math]::Max($left.Height,$right.Height))
  $graphics=[Drawing.Graphics]::FromImage($comparison)
  try{$graphics.DrawImageUnscaled($left,0,0);$graphics.DrawImageUnscaled($right,$left.Width,0);$comparison.Save((Join-Path $evidence "comparison-$suffix.png"),[Drawing.Imaging.ImageFormat]::Png)}finally{$graphics.Dispose();$comparison.Dispose();$left.Dispose();$right.Dispose()}
  $cases+=@{theme=$theme;width=$size.width;height=$size.height;dpi=$homeCapture.Dpi;surface=$design.dashboardSurface;border=$design.dashboardBorder;radius=$design.dashboardRadius;smallestText=$design.smallestText;pointerRoute=$pointerRoute;passed=$true}
 }
}
Send @{action='dark'}
Send @{action='dashboard';scenario='Administrator'}
@{passed=$true;fixtureOnly=$true;cases=$cases;scope='Native WinUI shared design, themes, charts, tables and pointer states. Test records only; no remote deployment evidence.'}|ConvertTo-Json -Depth 6|Set-Content (Join-Path $evidence 'design-validation.json') -Encoding utf8
