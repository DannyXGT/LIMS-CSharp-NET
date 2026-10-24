param()
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $projectRoot 'Frontend\Lims.Desktop\Lims.Desktop.csproj'
$publishPath = Join-Path $projectRoot 'artifacts\publish\LIMS-singlefile-win-x64'
$versionPath = Join-Path $PSScriptRoot 'portable-version.txt'
$publishRoot = Join-Path $projectRoot 'artifacts\publish'
[System.IO.Directory]::CreateDirectory($publishRoot) | Out-Null

# Keep concurrent publications from selecting the same version or sharing build output.
$lockPath = Join-Path $publishRoot '.portable-publish.lock'
$publishLock = [System.IO.File]::Open($lockPath, [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
try {
    $currentVersionText = (Get-Content -LiteralPath $versionPath -Raw).Trim()
    if ($currentVersionText -notmatch '^\d+\.\d+\.\d+$') {
        throw "Invalid portable version in ${versionPath}: expected major.minor.patch."
    }
    $currentVersion = [version]$currentVersionText
    if ($currentVersion.Major -gt 65534 -or $currentVersion.Minor -gt 65534 -or $currentVersion.Build -ge 65534) {
        throw 'Portable version exceeds assembly version limits. Update the major/minor version in scripts/portable-version.txt.'
    }
    $nextVersion = '{0}.{1}.{2}' -f $currentVersion.Major, $currentVersion.Minor, ($currentVersion.Build + 1)
    $assemblyVersion = "$nextVersion.0"
    # Compile the versioned identity so WinUI resources match the executable name.
    $executableName = "Lims.Desktop-$nextVersion"
    $portableExe = Join-Path $publishRoot "$executableName.exe"
    if (Test-Path -LiteralPath $portableExe) {
        throw "Release executable already exists: $portableExe. No files were overwritten."
    }
    Write-Host "Publishing portable version $nextVersion..."

    & dotnet publish $projectPath -c Release -r win-x64 --self-contained true -m:1 -p:Platform=x64 -p:RuntimeIdentifiers=win-x64 -p:PortableSingleFile=true "-p:PortableExecutableName=$executableName" "-p:Version=$nextVersion" "-p:AssemblyVersion=$assemblyVersion" "-p:FileVersion=$assemblyVersion" "-p:InformationalVersion=$nextVersion" -p:IncludeSourceRevisionInInformationalVersion=false -o $publishPath
    if ($LASTEXITCODE -ne 0) { throw 'Single-file portable publication failed.' }
    $publishedExe = Join-Path $publishPath "$executableName.exe"
    if (-not (Test-Path -LiteralPath $publishedExe)) { throw 'Published executable was not found.' }
    Copy-Item -LiteralPath $publishedExe -Destination $portableExe
    [System.IO.File]::WriteAllText($versionPath, "$nextVersion`r`n", [System.Text.Encoding]::ASCII)
    Write-Host "Portable EXE ($nextVersion): $portableExe"
}
finally {
    $publishLock.Dispose()
}
