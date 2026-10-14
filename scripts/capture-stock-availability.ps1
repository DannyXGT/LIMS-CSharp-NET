param(
    [ValidateSet('Start','Inspect','Capture')][string]$Action = 'Capture',
    [string]$Name = 'stock-current',
    [int]$ExistingProcessId = 0,
    [int]$Width = 1920,
    [int]$Height = 1080
)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$evidence = Join-Path $workspace 'artifacts\validation\stock-availability-real'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$stateFile = Join-Path $evidence 'process.json'
if ($Action -eq 'Start') {
    $output = Join-Path $workspace 'Frontend\Lims.Desktop\bin\Debug\net10.0-windows10.0.26100.0\win-x64'
    $stage = Join-Path $evidence ('stage-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    Get-ChildItem -LiteralPath $output | Where-Object { $_.Name -notin @('AppX','AppPackages') -and $_.Name -notlike '*.appxrecipe' } | Copy-Item -Destination $stage -Recurse
    $manifest = Join-Path $stage 'AppxManifest.xml'
    $identity = 'Lims.StockAvailability.Review'
    $executable = $identity + '.exe'
    $text = [IO.File]::ReadAllText($manifest).Replace('Name="Lims.NextGeneration.Desktop"', ('Name="' + $identity + '"'))
    [IO.File]::WriteAllText($manifest, $text, [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath (Join-Path $stage 'Lims.Desktop.exe') -Destination (Join-Path $stage $executable)
    $winapp = 'C:\Tools\WinAppCLI\winapp.exe'
    Start-Process -FilePath $winapp -ArgumentList @('run', ('"' + $stage + '"'), '--manifest', ('"' + $manifest + '"'), '--exe', $executable, '--with-alias') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $stage 'launch.out.log') -RedirectStandardError (Join-Path $stage 'launch.err.log')
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        $process = Get-Process -Name 'Lims.Desktop' -ErrorAction SilentlyContinue | Where-Object { $_.Path.StartsWith($stage + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and $_.MainWindowHandle -ne 0 } | Select-Object -First 1
        if ($null -ne $process) { break }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($null -eq $process) { throw 'The updated application did not launch. Inspect launch logs in the evidence folder.' }
    @{ pid = $process.Id; path = $process.Path; hwnd = $process.MainWindowHandle.ToInt64() } | ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding UTF8
    Write-Output 'Updated application launched against the configured real environment. Sign in inside its window; no fixture data is used.'
    return
}
if ($ExistingProcessId -gt 0) {
    $existing = Get-Process -Id $ExistingProcessId
    $desktopOutput = Join-Path $workspace 'Frontend\Lims.Desktop\bin\'
    if ($existing.ProcessName -ne 'Lims.Desktop' -or -not $existing.Path.StartsWith($desktopOutput, [StringComparison]::OrdinalIgnoreCase)) { throw 'Expected an existing LIMS Desktop process from this workspace.' }
    $state = @{ pid=$existing.Id; path=$existing.Path; hwnd=$existing.MainWindowHandle.ToInt64() }
} else {
    $state = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
}
$process = Get-Process -Id $state.pid
if ($process.Path -ne $state.path) { throw 'Unexpected process identity.' }
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing
$handle = [IntPtr]$state.hwnd
$window = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
$windowPattern = [System.Windows.Automation.WindowPattern]$window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
if ($windowPattern.Current.WindowVisualState -ne [System.Windows.Automation.WindowVisualState]::Normal) {
    $windowPattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal)
}
if ($Action -eq 'Inspect') {
    $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) |
        Where-Object { -not $_.Current.IsOffscreen } |
        ForEach-Object { [pscustomobject]@{ Name=$_.Current.Name; Type=$_.Current.ControlType.ProgrammaticName; Enabled=$_.Current.IsEnabled; Bounds=$_.Current.BoundingRectangle.ToString() } } |
        ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $evidence ($Name + '.json')) -Encoding UTF8
    return
}
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class StockRealCapture {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
'@
[StockRealCapture]::SetWindowPos($handle,[IntPtr]::Zero,0,0,$Width,$Height,0x0014) | Out-Null
Start-Sleep -Milliseconds 300
$rect = [StockRealCapture+Rect]::new()
[StockRealCapture]::GetWindowRect($handle,[ref]$rect) | Out-Null
$bitmap = [Drawing.Bitmap]::new($rect.Right-$rect.Left,$rect.Bottom-$rect.Top)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$dc = $graphics.GetHdc()
try {
    if (-not [StockRealCapture]::PrintWindow($handle,$dc,2)) { throw 'The real application window could not be captured.' }
} finally { $graphics.ReleaseHdc($dc) }
try { $bitmap.Save((Join-Path $evidence ($Name + '.png')),[Drawing.Imaging.ImageFormat]::Png) }
finally { $graphics.Dispose(); $bitmap.Dispose() }
Write-Output (Join-Path $evidence ($Name + '.png'))
