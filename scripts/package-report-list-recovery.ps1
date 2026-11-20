$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$original=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'package-report-list-evidence.ps1'))
$section=$original.Substring($original.IndexOf('$relative=@('));$section=$section.Substring(0,$section.IndexOf(')'))
$sources=@([regex]::Matches($section,"'([^']+)'")|ForEach-Object{$_.Groups[1].Value}|Where-Object{$_ -notlike 'artifacts/*'})
$sources+=@(
 'Frontend/Lims.Desktop/Planning/ReportListGrid.cs','Frontend/Lims.Desktop/Planning/ReportListRows.cs',
 'Tests/Frontend/Lims.Desktop.Tests/PlanningReportRowsTests.cs','Tests/Frontend/Lims.Desktop.Tests/PlanningCompatibilityTests.cs',
 'scripts/test-report-list-recovery.ps1','scripts/validate-report-list-volume.ps1','scripts/package-report-list-recovery.ps1',
 'scripts/start-planning-development.ps1','scripts/planning/preflight-execution-report-list.sql'
)
$checks=@(
 'artifacts/report-list/recovery/acceptance-final.json','artifacts/report-list/recovery/real-volume.json','artifacts/report-list/recovery/remote-api-status.json',
 'artifacts/report-list/real-mark-identity.json','artifacts/report-list/real-mark-persistence.json','artifacts/report-list/real-authorization.json',
 'artifacts/report-list/native-Mark.json','artifacts/report-list/native-Correct.json','artifacts/report-list/native-dossier.json','artifacts/report-list/native-thousands.json',
 'artifacts/report-list/recovery/tests/rules.trx','artifacts/report-list/recovery/tests/postgres.trx','artifacts/report-list/recovery/tests/authorization.trx','artifacts/report-list/recovery/tests/desktop.trx',
 'artifacts/report-list/ui/recovery-Overview.png','artifacts/report-list/ui/recovery-Pending.png','artifacts/report-list/ui/recovery-Checked.png',
 'artifacts/report-list/ui/02-report-list-dos-ph-reales.png','artifacts/report-list/ui/recovery-Dossier-tests.png','artifacts/report-list/ui/recovery-Dossier-history.png',
 'artifacts/report-list/ui/01-consulta-nombres-planificacion-dos-ph.png','artifacts/report-list/ui/recovery-Thousands.png'
)
$entries=@();foreach($file in @($sources)+@($checks)|Select-Object -Unique){$entries+=@{Source=(Join-Path $workspace $file);Entry=$file}}
$publish=Join-Path $workspace 'artifacts/report-list-server/publish'
foreach($file in Get-ChildItem -LiteralPath $publish -File -Recurse){
    if($file.Name -like 'appsettings*.json' -or $file.Name -eq 'web.config'){continue}
    $entries+=@{Source=$file.FullName;Entry=('api/'+$file.FullName.Substring($publish.Length+1).Replace('\','/'))}
}
$entries+=@{Source=(Join-Path $workspace 'artifacts/report-list-server/README.txt');Entry='SERVER-README.txt'}
$manifest=@($entries|ForEach-Object{
    if(-not(Test-Path -LiteralPath $_.Source -PathType Leaf)){throw ('Missing delivery file: '+$_.Entry)}
    @{File=$_.Entry;Bytes=(Get-Item -LiteralPath $_.Source).Length;Sha256=(Get-FileHash -LiteralPath $_.Source).Hash}
})
$manifestPath=Join-Path $workspace 'artifacts/report-list/recovery/manifest.json'
$manifest|ConvertTo-Json -Depth 4|Set-Content $manifestPath -Encoding UTF8
$entries+=@{Source=$manifestPath;Entry='manifest.json'}
$archive=Join-Path $workspace 'ReportList-Recuperado-2026-11-20.zip'
Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
$stream=[IO.File]::Open($archive,[IO.FileMode]::CreateNew)
try{
    $zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$true)
    try{foreach($entry in $entries){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$entry.Source,$entry.Entry,[IO.Compression.CompressionLevel]::Optimal)|Out-Null}}finally{$zip.Dispose()}
}finally{$stream.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try{
    $index=@{};foreach($entry in $zip.Entries){$index[$entry.FullName.Replace('\','/')]=$entry}
    if($index.Count -ne $entries.Count){throw 'Unexpected archive entry count.'}
    foreach($file in $manifest){
        $entry=$index[$file.File];if(-not $entry){throw ('Missing ZIP entry: '+$file.File)}
        $input=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
        try{$actual=[BitConverter]::ToString($sha.ComputeHash($input)).Replace('-','')}finally{$input.Dispose();$sha.Dispose()}
        if($actual -ne $file.Sha256){throw ('ZIP hash mismatch: '+$file.File)}
    }
}finally{$zip.Dispose()}
@{Archive=$archive;Files=$entries.Count;Bytes=(Get-Item $archive).Length;Sha256=(Get-FileHash $archive).Hash}|ConvertTo-Json
