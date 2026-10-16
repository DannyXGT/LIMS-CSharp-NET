param([ValidatePattern('^[a-zA-Z0-9-]+$')][string]$Name = 'live')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$evidence = Join-Path $workspace 'artifacts\validation\standards-live'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$candidates = @(Get-Process -Name Lims.Desktop -ErrorAction Stop | Where-Object {
    $_.Path -and $_.Path.StartsWith($workspace + '\', [StringComparison]::OrdinalIgnoreCase)
})
if ($candidates.Count -ne 1) { throw 'Expected exactly one LIMS desktop process from this workspace.' }
$process = $candidates[0]
$condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$process.Id)
$deadline = [DateTime]::UtcNow.AddSeconds(5)
do {
    try {
        $window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
        if ($null -eq $window) { throw 'LIMS desktop window was not found.' }
        $elements = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        break
    } catch {
        if ([DateTime]::UtcNow -ge $deadline) { throw }
        Start-Sleep -Milliseconds 200
    }
} while ($true)
@{ hwnd = [long]$window.Current.NativeWindowHandle } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'window.json') -Encoding UTF8
$list = $elements | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.List' -and $_.Current.Name -like 'Est?ndares' } | Select-Object -First 1
$viewport = if ($list) { $list.Current.BoundingRectangle } else { $null }
$rows = @($elements | Where-Object {
    -not $_.Current.IsOffscreen -and $_.Current.ControlType.ProgrammaticName -eq 'ControlType.ListItem' -and $_.Current.Name.EndsWith(' disponibles')
} | ForEach-Object {
    $row = $_
    $children = $row.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $bars = @($children | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.ControlType.ProgrammaticName -eq 'ControlType.ProgressBar' })
    $bounds = $row.Current.BoundingRectangle
    [pscustomobject]@{
        name = $row.Current.Name
        height = $bounds.Height
        fullyVisible = $null -ne $viewport -and $bounds.Height -gt 0 -and $bounds.Top -ge $viewport.Top - 1 -and $bounds.Bottom -le $viewport.Bottom + 1
        bars = $bars.Count
        percentages = @($bars | ForEach-Object { $_.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current.Value })
    }
})
# UI Automation clips item bounds to the viewport; a 4 px sliver at the bottom
# is reported as on-screen even though its progress bar is outside the viewport.
$fullRowHeight = ($rows | Measure-Object height -Maximum).Maximum
foreach ($row in $rows) {
    $row.fullyVisible = $row.fullyVisible -and $row.height -ge $fullRowHeight - 1
}
$assemblyPath = Join-Path (Split-Path $process.Path) 'Lims.Desktop.dll'
$sourcePath = Join-Path $workspace 'Frontend\Lims.Desktop\bin\Debug\net10.0-windows10.0.26100.0\win-x64\Lims.Desktop.dll'
$hash = (Get-FileHash -LiteralPath $assemblyPath).Hash
[pscustomobject]@{
    fixtureOnly = $false
    pid = $process.Id
    executable = $process.Path
    started = $process.StartTime.ToString('o')
    sha256 = $hash
    matchesBuild = $hash -eq (Get-FileHash -LiteralPath $sourcePath).Hash
    hasAvailabilityFix = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($assemblyPath)).Contains('AvailabilityVisibility')
    viewport = [string]$viewport
    visibleRows = $rows.Count
    fullyVisibleRows = @($rows | Where-Object fullyVisible).Count
    allFullyVisibleRowsHaveBars = @($rows | Where-Object fullyVisible).Count -gt 0 -and @($rows | Where-Object { $_.fullyVisible -and $_.bars -ne 1 }).Count -eq 0
    rows = $rows
} | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $evidence ($Name + '.json')) -Encoding UTF8
& (Join-Path $PSScriptRoot 'validate-standards-visual.ps1') -Action Capture -EvidenceFolder 'standards-live' -Name $Name
Get-Content -LiteralPath (Join-Path $evidence ($Name + '.json'))
