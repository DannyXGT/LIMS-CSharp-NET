$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$evidence = Join-Path $workspace 'artifacts\validation\theme-login'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$existing = @(Get-Process Lims.Desktop -ErrorAction SilentlyContinue | ForEach-Object Id)
Start-Process -FilePath 'C:\Tools\WinAppCLI\winapp.exe' -ArgumentList @('run', ('"' + (Join-Path $workspace 'Frontend\Lims.Desktop\Lims.Desktop.csproj') + '"'), '--configuration', 'Debug', '--arch', 'x64', '--with-alias', '--no-build', '--no-restore') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $evidence 'launch.out.log') -RedirectStandardError (Join-Path $evidence 'launch.err.log')
$deadline = [DateTime]::UtcNow.AddSeconds(90)
$actualWindow = $null
do {
    foreach ($candidate in Get-Process Lims.Desktop -ErrorAction SilentlyContinue | Where-Object { $_.Id -notin $existing }) {
        $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$candidate.Id)
        $actualWindow = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
        if ($null -ne $actualWindow) { break }
    }
    if ($null -eq $actualWindow) { Start-Sleep -Milliseconds 250 }
} while ($null -eq $actualWindow -and [DateTime]::UtcNow -lt $deadline)
if ($null -eq $actualWindow) { throw 'No new desktop window opened. Inspect theme-login/launch logs.' }
$handle = [IntPtr]$actualWindow.Current.NativeWindowHandle
@{hwnd=$handle.ToInt64()} | ConvertTo-Json | Set-Content (Join-Path $evidence 'window.json') -Encoding utf8
Start-Sleep -Milliseconds 500
& (Join-Path $PSScriptRoot 'validate-standards-visual.ps1') -Action Capture -Name 'login-initial' -EvidenceFolder 'theme-login'
$controls = $actualWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$controls | ForEach-Object { @{name=$_.Current.Name;type=$_.Current.ControlType.ProgrammaticName;offscreen=$_.Current.IsOffscreen;bounds=$_.Current.BoundingRectangle.ToString()} } |
    ConvertTo-Json -Depth 4 | Set-Content (Join-Path $evidence 'login-uia.json') -Encoding utf8
@{fixtureOnly=$false;authenticated=$false;process=$candidate.Id;executable=$candidate.Path;sha256=(Get-FileHash -LiteralPath (Join-Path (Split-Path $candidate.Path) 'Lims.Desktop.dll')).Hash;scope='Actual desktop executable at login; no credentials entered and no authentication performed.'} |
    ConvertTo-Json | Set-Content (Join-Path $evidence 'login-validation.json') -Encoding utf8
$transform = $actualWindow.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern)
$transform.Move(0,0)
$transform.Resize(1366,708)
Start-Sleep -Milliseconds 500
$capture = & (Join-Path $PSScriptRoot 'validate-standards-visual.ps1') -Action Capture -Name 'login-1366x708-workarea' -EvidenceFolder 'theme-login'
$capture | ConvertTo-Json | Set-Content (Join-Path $evidence 'workarea-capture.json') -Encoding utf8
