param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath
)
$ErrorActionPreference = 'Stop'
$exe = Get-Item -LiteralPath $ExePath
if ($exe.Extension -ne '.exe') { throw 'Selecciona el archivo EXE de LIMS.' }
$reportRoot = Join-Path $env:LOCALAPPDATA ('LIMS\Diagnostics\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
[System.IO.Directory]::CreateDirectory($reportRoot) | Out-Null
$report = Join-Path $reportRoot 'diagnostico.txt'
function Add-Report($value) { $value | Out-String -Width 240 | Add-Content -LiteralPath $report -Encoding UTF8 }
Add-Report ('Fecha: ' + (Get-Date -Format o))
Add-Report (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' |
    Select-Object ProductName, DisplayVersion, CurrentBuildNumber, UBR, InstallationType)
Add-Report ([pscustomobject]@{ Windows64Bits=[Environment]::Is64BitOperatingSystem; PowerShell64Bits=[Environment]::Is64BitProcess; Arquitectura=$env:PROCESSOR_ARCHITECTURE; ArquitecturaNativa=$env:PROCESSOR_ARCHITEW6432; Temporal=$env:TEMP })
Add-Report ($exe | Select-Object FullName, Length, LastWriteTime, @{n='Version';e={$_.VersionInfo.FileVersion}})
Add-Report (Get-FileHash -LiteralPath $exe.FullName -Algorithm SHA256)
Add-Report (Get-AuthenticodeSignature -LiteralPath $exe.FullName | Select-Object Status, StatusMessage)
Add-Report (Get-Content -LiteralPath $exe.FullName -Stream Zone.Identifier -ErrorAction SilentlyContinue)
Add-Report (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64' -ErrorAction SilentlyContinue |
    Select-Object Installed, Version)
$started = Get-Date
try {
    # Redirect native host errors too: these can occur before managed logging starts.
    $process = Start-Process -FilePath $exe.FullName -WorkingDirectory $exe.DirectoryName -PassThru `
        -RedirectStandardOutput (Join-Path $reportRoot 'stdout.txt') `
        -RedirectStandardError (Join-Path $reportRoot 'stderr.txt')
    Write-Host 'Esperando 15 segundos. Si aparece un error de LIMS, cierralo para continuar.'
    Start-Sleep -Seconds 15
    $process.Refresh()
    Add-Report ([pscustomobject]@{ Proceso=$process.Id; Cerro=$process.HasExited; Ventana=$process.MainWindowTitle })
    if ($process.HasExited) { Add-Report ('Codigo de salida: ' + $process.ExitCode) }
}
catch { Add-Report $_.Exception.ToString() }
try {
    Add-Report (Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=$started.AddMinutes(-2)} -ErrorAction Stop |
        Where-Object { $_.ProviderName -in @('Application Error', '.NET Runtime', 'Windows Error Reporting', 'SideBySide') -and $_.Message -match 'Lims.Desktop' } |
        Select-Object TimeCreated, Id, ProviderName, Message | Format-List)
}
catch { Add-Report ('Eventos no disponibles: ' + $_.Exception.Message) }
$logRoot = Join-Path $env:LOCALAPPDATA 'LIMS\Logs'
if (Test-Path -LiteralPath $logRoot) {
    Get-ChildItem -LiteralPath $logRoot -Filter 'startup-*.log' |
        Where-Object { $_.LastWriteTime -ge $started.AddSeconds(-2) } |
        Copy-Item -Destination $reportRoot
}
Write-Host "Diagnostico guardado en: $reportRoot"
Write-Host 'Comparte diagnostico.txt, stderr.txt y los startup-*.log si existen.'
