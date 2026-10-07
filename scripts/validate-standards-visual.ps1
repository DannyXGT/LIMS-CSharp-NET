param(
    [ValidateSet('Start', 'Command', 'Capture', 'Inspect', 'Invoke', 'Click', 'DoubleClick', 'Key', 'PressCapture', 'HoverCapture')]
    [string]$Action = 'Inspect',
    [string]$Command,
    [string]$Name = 'capture',
    [string]$Keys,
    [switch]$SkipBuild,
    [switch]$PopupOnly,
    [switch]$IncludePopups,
    [switch]$IsolatedIdentity,
    [ValidateSet('HeaderGrid', 'SummarySurface', 'GeneralSection', 'StorageSection', 'AuditSection', 'KpiGrid', 'AvailabilityCard', 'ExpiryCard', 'StateCard')]
    [string]$Section,
    [string]$EvidenceFolder = 'standards-controls'
)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$evidence = Join-Path $workspace ('artifacts\validation\' + $EvidenceFolder)
New-Item -ItemType Directory -Path $evidence -Force | Out-Null

if ($Action -eq 'Start') {
    foreach ($coordinationFile in @('command.json', 'ack.json', 'window.json', 'error.txt', 'crash.txt')) {
        $coordinationPath = Join-Path $evidence $coordinationFile
        if (Test-Path -LiteralPath $coordinationPath) { Remove-Item -LiteralPath $coordinationPath -Force }
    }
    if (-not $SkipBuild) {
        dotnet build (Join-Path $workspace 'Tests\Frontend\Lims.Desktop.VisualHarness\Lims.Desktop.VisualHarness.csproj') --no-restore -c Debug -p:Platform=x64 -m:1
        if ($LASTEXITCODE -ne 0) { throw 'Visual harness build failed.' }
    }
    $output = Join-Path $workspace 'Tests\Frontend\Lims.Desktop.VisualHarness\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64'
    $stage = Join-Path $evidence ('stage-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    Get-ChildItem -LiteralPath $output | Where-Object { $_.Name -notlike '*.appxrecipe' } | Copy-Item -Destination $stage -Recurse
    $env:LIMS_VISUAL_DIRECTORY = $evidence
    $winapp = 'C:\Tools\WinAppCLI\winapp.exe'
    if (-not (Test-Path -LiteralPath $winapp)) { $winapp = (Get-Command winapp.exe).Source }
    $manifest = Join-Path $stage 'AppxManifest.xml'
    $fixtureExecutable = 'Lims.Desktop.VisualHarness.exe'
    if ($IsolatedIdentity) {
        $identity = 'Lims.VisualValidation.' + ($EvidenceFolder -replace '[^A-Za-z0-9.-]', '')
        $manifestText = [IO.File]::ReadAllText($manifest).Replace('Name="Lims.VisualValidation"', ('Name="' + $identity + '"'))
        [IO.File]::WriteAllText($manifest, $manifestText, [Text.UTF8Encoding]::new($false))
        $fixtureExecutable = $identity + '.exe'
        Copy-Item -LiteralPath (Join-Path $stage 'Lims.Desktop.VisualHarness.exe') -Destination (Join-Path $stage $fixtureExecutable)
    }
    $arguments = @('run', ('"' + $stage + '"'), '--manifest', ('"' + $manifest + '"'), '--exe', $fixtureExecutable, '--with-alias')
    Start-Process -FilePath $winapp -ArgumentList $arguments -WindowStyle Hidden -RedirectStandardOutput (Join-Path $stage 'launch.out.log') -RedirectStandardError (Join-Path $stage 'launch.err.log')
    Write-Output $evidence
    return
}

if ($Action -eq 'Command') {
    $data = $Command | ConvertFrom-Json
    $data | Add-Member -NotePropertyName sequence -NotePropertyValue ([Guid]::NewGuid().ToString('N')) -Force
    $json = $data | ConvertTo-Json -Compress
    $writeDeadline = [DateTime]::UtcNow.AddSeconds(2)
    do {
        try {
            [IO.File]::WriteAllText((Join-Path $evidence 'command.json'), $json, [Text.UTF8Encoding]::new($false))
            break
        } catch [IO.IOException] {
            if ([DateTime]::UtcNow -ge $writeDeadline) { throw }
            Start-Sleep -Milliseconds 20
        }
    } while ($true)
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        $ackPath = Join-Path $evidence 'ack.json'
        try {
            if (Test-Path -LiteralPath $ackPath) {
                $ackStream = [IO.FileStream]::new($ackPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
                $ackReader = [IO.StreamReader]::new($ackStream)
                try { if ($ackReader.ReadToEnd() -eq $json) { return } }
                finally { $ackReader.Dispose() }
            }
        } catch [IO.IOException] { }
        Start-Sleep -Milliseconds 50
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'The visual harness did not acknowledge the command. Inspect error.txt/crash.txt.'
}

$state = Get-Content -LiteralPath (Join-Path $evidence 'window.json') | ConvertFrom-Json
$handle = [IntPtr]$state.hwnd
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
$windowElement = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
$processCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $windowElement.Current.ProcessId)
if ($Action -eq 'Inspect') {
    $elements = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $processCondition)
    $elements | ForEach-Object { [PSCustomObject]@{ Name=$_.Current.Name; Type=$_.Current.ControlType.ProgrammaticName; Enabled=$_.Current.IsEnabled; Focused=$_.Current.HasKeyboardFocus; Offscreen=$_.Current.IsOffscreen; Bounds=$_.Current.BoundingRectangle.ToString() } } |
        ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $evidence ($Name + '.json')) -Encoding UTF8
    Write-Output (Join-Path $evidence ($Name + '.json'))
    return
}
if ($Action -eq 'Invoke') {
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    $combinedCondition = [System.Windows.Automation.AndCondition]::new($processCondition, $condition)
    $buttonCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    $combinedCondition = [System.Windows.Automation.AndCondition]::new($combinedCondition, $buttonCondition)
    $element = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $combinedCondition)
    if ($null -eq $element) { throw "Control not found: $Name" }
    $pattern = $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
    return
}
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class LimsVisualCapture {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
 [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
 [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
 [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect rect, int size);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] public static extern bool SetPhysicalCursorPos(int x, int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
'@
if ($Action -in @('Click', 'DoubleClick')) {
    # No SetFocus or InvokePattern: exercise the actual pointer hit testing.
    $clickNameCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    $clickCondition = [System.Windows.Automation.AndCondition]::new($processCondition, $clickNameCondition)
    $clickTarget = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $clickCondition) |
        Where-Object { $_.Current.IsEnabled -and $_.Current.IsKeyboardFocusable -and -not $_.Current.IsOffscreen } | Select-Object -First 1
    if ($null -eq $clickTarget) { throw "Clickable control not found: $Name" }
    [LimsVisualCapture]::SetForegroundWindow($handle) | Out-Null
    Start-Sleep -Milliseconds 150
    $clickOwner = [uint32]0
    [LimsVisualCapture]::GetWindowThreadProcessId([LimsVisualCapture]::GetForegroundWindow(), [ref]$clickOwner) | Out-Null
    if ($clickOwner -ne $windowElement.Current.ProcessId) { throw 'The target does not own pointer input. No click was sent.' }
    $clickBounds = $clickTarget.Current.BoundingRectangle
    [LimsVisualCapture]::SetPhysicalCursorPos([int]($clickBounds.X + $clickBounds.Width / 2), [int]($clickBounds.Y + $clickBounds.Height / 2)) | Out-Null
    [LimsVisualCapture]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 40
    [LimsVisualCapture]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    if ($Action -eq 'DoubleClick') {
        Start-Sleep -Milliseconds 60
        [LimsVisualCapture]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 40
        [LimsVisualCapture]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    }
    return
}
if ($Action -eq 'Key') {
    $keyForegroundProcess = [uint32]0
    [LimsVisualCapture]::GetWindowThreadProcessId([LimsVisualCapture]::GetForegroundWindow(), [ref]$keyForegroundProcess) | Out-Null
    if ($keyForegroundProcess -ne $windowElement.Current.ProcessId) { [LimsVisualCapture]::SetForegroundWindow($handle) | Out-Null }
    $focusedCondition = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::HasKeyboardFocusProperty, $true),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::IsKeyboardFocusableProperty, $true))
    $focusedFixtureControl = $windowElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $focusedCondition)
    if ($null -ne $focusedFixtureControl) { $focusedFixtureControl.SetFocus() }
    Start-Sleep -Milliseconds 150
    [LimsVisualCapture]::GetWindowThreadProcessId([LimsVisualCapture]::GetForegroundWindow(), [ref]$keyForegroundProcess) | Out-Null
    if ($keyForegroundProcess -ne $windowElement.Current.ProcessId) { throw 'The fixture does not own keyboard focus. No keys were sent.' }
    [System.Windows.Forms.SendKeys]::SendWait($Keys)
    return
}
$rect = [LimsVisualCapture+Rect]::new()
[LimsVisualCapture]::GetWindowRect($handle, [ref]$rect) | Out-Null
if ($Section) {
    $layout = Get-Content -LiteralPath (Join-Path $evidence 'layout.json') | ConvertFrom-Json
    $sectionLayout = $layout.detailSections | Where-Object { $_.name -eq $Section } | Select-Object -First 1
    if (-not $layout.detailOpen -or $null -eq $sectionLayout.x -or $sectionLayout.width -le 0) { throw 'No visible detail section to capture. Run report first.' }
    $origin = [LimsVisualCapture+Point]::new()
    [LimsVisualCapture]::ClientToScreen($handle, [ref]$origin) | Out-Null
    $sectionLeft = $origin.X + $sectionLayout.x * $layout.rasterizationScale
    $sectionTop = $origin.Y + $sectionLayout.y * $layout.rasterizationScale
    $sectionRight = $sectionLeft + $sectionLayout.width * $layout.rasterizationScale
    $sectionBottom = $sectionTop + $sectionLayout.height * $layout.rasterizationScale
    if ($sectionLeft -lt $rect.Left -or $sectionTop -lt $rect.Top -or $sectionRight -gt $rect.Right -or $sectionBottom -gt $rect.Bottom) { throw 'Section is outside the fixture window.' }
    $rect.Left = [int][Math]::Floor($sectionLeft)
    $rect.Top = [int][Math]::Floor($sectionTop)
    $rect.Right = [int][Math]::Ceiling($sectionRight)
    $rect.Bottom = [int][Math]::Ceiling($sectionBottom)
}
# Keep the requested outer window dimensions in the evidence.
if ($PopupOnly) {
    $calendarCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Calendar)
    $ownedCalendarCondition = [System.Windows.Automation.AndCondition]::new($processCondition, $calendarCondition)
    $calendar = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $ownedCalendarCondition)
    if ($null -eq $calendar -or $calendar.Current.IsOffscreen) { throw 'No visible fixture calendar popup.' }
    $popupBounds = $calendar.Current.BoundingRectangle
    $rect.Left = [int][Math]::Floor($popupBounds.Left)
    $rect.Top = [int][Math]::Floor($popupBounds.Top)
    $rect.Right = [int][Math]::Ceiling($popupBounds.Right)
    $rect.Bottom = [int][Math]::Ceiling($popupBounds.Bottom)
}
$bitmap = [Drawing.Bitmap]::new($rect.Right - $rect.Left, $rect.Bottom - $rect.Top)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$foregroundProcess = [uint32]0
[LimsVisualCapture]::GetWindowThreadProcessId([LimsVisualCapture]::GetForegroundWindow(), [ref]$foregroundProcess) | Out-Null
if ($foregroundProcess -ne $windowElement.Current.ProcessId) { [LimsVisualCapture]::SetForegroundWindow($handle) | Out-Null }
Start-Sleep -Milliseconds 180
if ($Action -in @('PressCapture', 'HoverCapture')) {
    $nameCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    $buttonCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    $pressedCondition = [System.Windows.Automation.AndCondition]::new($processCondition, $nameCondition, $buttonCondition)
    $pressedButton = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $pressedCondition)
    if ($null -eq $pressedButton -or -not $pressedButton.Current.IsEnabled) { throw "Enabled button not found: $Name" }
    $pressedButton.SetFocus()
    $pressedForegroundProcess = [uint32]0
    [LimsVisualCapture]::GetWindowThreadProcessId([LimsVisualCapture]::GetForegroundWindow(), [ref]$pressedForegroundProcess) | Out-Null
    if ($pressedForegroundProcess -ne $windowElement.Current.ProcessId) { throw 'The fixture does not own pointer input. No click was sent.' }
    $pressedBounds = $pressedButton.Current.BoundingRectangle
    [LimsVisualCapture]::SetCursorPos([int]($pressedBounds.X + $pressedBounds.Width / 2), [int]($pressedBounds.Y + $pressedBounds.Height / 2)) | Out-Null
    if ($Action -eq 'HoverCapture') { [LimsVisualCapture]::mouse_event(1, 1, 0, 0, [UIntPtr]::Zero) }
    if ($Action -eq 'PressCapture') { [LimsVisualCapture]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds 100
}
try {
if ($PopupOnly -or $IncludePopups -or $Section) {
    $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
} else {
    $captureDc = $graphics.GetHdc()
    try {
        if (-not [LimsVisualCapture]::PrintWindow($handle, $captureDc, 2)) { throw 'The fixture window could not be captured.' }
    } finally { $graphics.ReleaseHdc($captureDc) }
}
$capturePath = Join-Path $evidence ($Name + '.png')
$bitmap.Save($capturePath, [Drawing.Imaging.ImageFormat]::Png)
} finally {
    if ($Action -eq 'PressCapture') {
        # Release away from the button so this evidence cannot submit/archive/cancel.
        [LimsVisualCapture]::SetCursorPos($rect.Left + 30, $rect.Top + 20) | Out-Null
        Start-Sleep -Milliseconds 150
        [LimsVisualCapture]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    }
}
$graphics.Dispose()
$bitmap.Dispose()
Write-Output ([PSCustomObject]@{ Path=$capturePath; Width=$rect.Right-$rect.Left; Height=$rect.Bottom-$rect.Top; Dpi=[LimsVisualCapture]::GetDpiForWindow($handle) })
