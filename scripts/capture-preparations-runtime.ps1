$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = Join-Path $workspace 'Frontend\Lims.Desktop\bin\Debug\net10.0-windows10.0.26100.0\win-x64'
$evidence = Join-Path $workspace 'artifacts\validation\preparations-runtime'
$stage = Join-Path $evidence ('stage-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Get-ChildItem -LiteralPath $source | Where-Object { $_.Name -notin @('AppX','AppPackages') -and $_.Name -notlike '*.appxrecipe' } | Copy-Item -Destination $stage -Recurse
$manifest = [IO.File]::ReadAllText((Join-Path $source 'AppX\AppxManifest.xml'))
$manifest = $manifest.Replace('Lims.NextGeneration.Desktop', 'Lims.Preparations.RuntimeValidation').Replace('Lims.Desktop.exe','Lims.Preparations.RuntimeValidation.exe')
[IO.File]::WriteAllText((Join-Path $stage 'AppxManifest.xml'), $manifest, [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $stage 'Lims.Desktop.exe') -Destination (Join-Path $stage 'Lims.Preparations.RuntimeValidation.exe')
Start-Process -FilePath 'C:\Tools\WinAppCLI\winapp.exe' -ArgumentList @('run', ('"' + $stage + '"'), '--manifest', ('"' + (Join-Path $stage 'AppxManifest.xml') + '"'), '--exe', 'Lims.Preparations.RuntimeValidation.exe', '--without-alias') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $evidence 'launch.out.log') -RedirectStandardError (Join-Path $evidence 'launch.err.log')
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$deadline = [DateTime]::UtcNow.AddSeconds(45)
$actual = $null
do {
    $proc = Get-Process -Name 'Lims.Preparations.RuntimeValidation' -ErrorAction SilentlyContinue | Where-Object { $_.Path.StartsWith($stage, [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
    if ($proc -and $proc.MainWindowHandle -ne [IntPtr]::Zero) { $actual = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle) }
    if ($null -eq $actual) { Start-Sleep -Milliseconds 200 }
} while ($null -eq $actual -and [DateTime]::UtcNow -lt $deadline)
if ($null -eq $actual) { throw 'Updated real executable did not open. Inspect preparations-runtime launch logs.' }
$sourceHash = (Get-FileHash -LiteralPath (Join-Path $source 'Lims.Desktop.dll')).Hash
$loadedHash = (Get-FileHash -LiteralPath (Join-Path (Split-Path $proc.Path) 'Lims.Desktop.dll')).Hash
if ($loadedHash -ne $sourceHash) { throw 'Runtime does not match the latest compilation.' }
@{ hwnd = $proc.MainWindowHandle.ToInt64() } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'window.json') -Encoding utf8
@{ fixtureOnly = $false; authenticated = $false; executable = $proc.Path; sha256 = $loadedHash; matchesCurrentBuild = $true; scope = 'Real application at login, isolated package identity. Existing authenticated application left open.' } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'runtime-validation.json') -Encoding utf8
& (Join-Path $PSScriptRoot 'validate-standards-visual.ps1') -Action Capture -Name 'updated-executable-login' -EvidenceFolder 'preparations-runtime'
