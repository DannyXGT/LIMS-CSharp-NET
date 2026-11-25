param([switch]$SkipBuild, [switch]$SkipStart)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$driver = Join-Path $PSScriptRoot 'validate-standards-visual.ps1'
$folder = 'workspace-menu-20261125'
$evidence = Join-Path $workspace ('artifacts/validation/' + $folder)
if (-not $SkipBuild) {
    dotnet build (Join-Path $workspace 'Tests/Frontend/Lims.Desktop.VisualHarness/Lims.Desktop.VisualHarness.csproj') --no-restore -p:Platform=x64 -m:1 -p:BuildInParallel=false
    if ($LASTEXITCODE -ne 0) { throw 'Visual harness build failed.' }
}
function Send($data) { & $driver -Action Command -EvidenceFolder $folder -Command ($data | ConvertTo-Json -Compress) }
if(-not $SkipStart){
    $evidenceRoot = [IO.Path]::GetFullPath($evidence) + [IO.Path]::DirectorySeparatorChar
    Get-Process -Name ('Lims.VisualValidation.' + $folder) -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($evidenceRoot, [StringComparison]::OrdinalIgnoreCase) } | Stop-Process
    & $driver -Action Start -SkipBuild -IsolatedIdentity -EvidenceFolder $folder
}
$deadline = [DateTime]::UtcNow.AddSeconds(30)
while (-not (Test-Path -LiteralPath (Join-Path $evidence 'window.json'))) {
    if ([DateTime]::UtcNow -gt $deadline) { throw 'The isolated WinUI harness did not start.' }
    Start-Sleep -Milliseconds 100
}
$results = @()
foreach ($case in @(
    @{ name='planning-admin'; width=1366; height=900; theme='dark'; workspace='planning'; expanded=$true; expected=7 },
    @{ name='planning-small'; width=1164; height=712; theme='dark'; workspace='planning'; expanded=$true; expected=7 },
    @{ name='planning-restricted'; width=1164; height=712; theme='dark'; workspace='planning'; expanded=$true; restricted=$true; expected=4 },
    @{ name='supplies-admin'; width=1164; height=712; theme='dark'; workspace='laboratory'; expanded=$true; expected=4 },
    @{ name='administration-admin'; width=1164; height=712; theme='dark'; workspace='administration'; expanded=$true; expected=1 },
    @{ name='administration-restricted'; width=1164; height=712; theme='dark'; workspace='administration'; expanded=$true; restricted=$true; expected=0 },
    @{ name='planning-revoked'; width=1164; height=712; theme='dark'; workspace='planning'; expanded=$true; planningAccess=$false; expected=0 },
    @{ name='planning-return'; width=1164; height=712; theme='dark'; workspace='planning'; expanded=$true; expected=7 },
    @{ name='light-supplies'; width=1366; height=768; theme='light'; workspace='laboratory'; expanded=$true; expected=4 },
    @{ name='compact-planning'; width=1366; height=768; theme='dark'; workspace='planning'; expanded=$false; expected=7 }
)) {
    Send @{action='size'; width=$case.width; height=$case.height}
    Send @{action=$case.theme}
    Start-Sleep -Milliseconds 400
    $request=@{action='shellNavigation';workspace=$case.workspace;expanded=$case.expanded;restricted=($case.restricted -eq $true);resetGroups=$true}
    if($case.ContainsKey('planningAccess')){$request.planningAccess=$case.planningAccess}
    Send $request
    $report = Get-Content -LiteralPath (Join-Path $evidence 'shell-navigation.json') -Raw | ConvertFrom-Json
    if (-not $report.Passed -or -not $report.FooterFits) { throw ('Navigation validation failed: ' + $case.name) }
    if($report.Workspace -ne $case.workspace -or @($report.Items).Count -ne $case.expected){throw ('Incorrect workspace modules: '+$case.name)}
    if($case.expanded -and $case.expected -gt 0 -and @($report.Groups).Count -ne 1){throw ('Workspace must expose only its own group: '+$case.name)}
    if($case.restricted -and ($report.Items.Title -contains 'Accesos' -or $report.Items.Title -contains 'Importaciones')){throw 'Restricted planning permissions were bypassed.'}
    Copy-Item -LiteralPath (Join-Path $evidence 'shell-navigation.json') -Destination (Join-Path $evidence ($case.name + '.json'))
    & $driver -Action Capture -EvidenceFolder $folder -Name $case.name
    $results += @{name=$case.name; report=$report}
}
foreach($group in @(
    @{workspace='laboratory';title='Suministros';toggle='SuppliesToggle';open=4;closed=1},
    @{workspace='planning';title='Planificación';toggle='PlanningToggle';open=7;closed=0},
    @{workspace='administration';title='Administración';toggle='AdministrationToggle';open=1;closed=0}
)){
    Send @{action='shellNavigation';workspace=$group.workspace;expanded=$true;resetGroups=$true}
    & $driver -Action Click -EvidenceFolder $folder -Name $group.title
    Send @{action='shellNavigation';reportOnly=$true}
    $closed=Get-Content -LiteralPath (Join-Path $evidence 'shell-navigation.json') -Raw|ConvertFrom-Json
    if($closed.Groups[0].Expanded -or @($closed.Items).Count -ne $group.closed){throw ('Group did not collapse vertically: '+$group.title)}
    & $driver -Action Capture -EvidenceFolder $folder -Name ($group.workspace+'-closed')
    # Exercise keyboard activation of the same native toggle.
    Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
    $state=Get-Content -LiteralPath (Join-Path $evidence 'window.json') -Raw|ConvertFrom-Json
    $windowElement=[System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$state.hwnd)
    $nameCondition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$group.title)
    $groupElement=$windowElement.FindAll([System.Windows.Automation.TreeScope]::Descendants,$nameCondition)|Where-Object{$_.Current.IsKeyboardFocusable -and -not $_.Current.IsOffscreen}|Select-Object -First 1
    if($null -eq $groupElement){throw ('Group toggle is not keyboard accessible: '+$group.title)}
    $groupElement.SetFocus()
    & $driver -Action Key -EvidenceFolder $folder -Keys ' '
    Send @{action='shellNavigation';reportOnly=$true}
    $opened=Get-Content -LiteralPath (Join-Path $evidence 'shell-navigation.json') -Raw|ConvertFrom-Json
    if(-not $opened.Groups[0].Expanded -or @($opened.Items).Count -ne $group.open){throw ('Keyboard did not reopen group: '+$group.title)}
    & $driver -Action Capture -EvidenceFolder $folder -Name ($group.workspace+'-opened')
    $results+=@{name=($group.workspace+'-mouse-keyboard');closed=$closed;report=$opened}
}
Send @{action='icons'; open=$true}
Send @{action='iconChecks'}
$icons = Get-Content -LiteralPath (Join-Path $evidence 'icon-checks.json') -Raw | ConvertFrom-Json
if (-not $icons.allVectorsLoaded -or -not $icons.squareBoxes) { throw 'Icon catalog validation failed.' }
Send @{action='icons'; open=$false}
@{Passed=$true; Boundary='Isolated WinUI with synthetic session and permissions; no live authentication, API or database'; Cases=$results; Icons=$icons} |
    ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $evidence 'acceptance.json') -Encoding utf8
Send @{action='exit'}
Write-Output (Join-Path $evidence 'acceptance.json')
