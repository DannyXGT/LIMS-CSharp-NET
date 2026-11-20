$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$relative=@(
'Backend/Lims.Api/Planning/PlanningEndpoints.cs',
'Backend/Lims.Application/Planning/PlanningModels.cs','Backend/Lims.Application/Planning/PlanningReconciler.cs','Backend/Lims.Application/Planning/ReportListRules.cs',
'Backend/Lims.Infrastructure/Planning/PostgresPlanningStore.cs','Backend/Lims.Infrastructure/Planning/PostgresPlanningQueries.cs','Backend/Lims.Infrastructure/Planning/PostgresPlanningDecisions.cs','Backend/Lims.Infrastructure/Planning/PlanningColumnFilters.cs','Backend/Lims.Infrastructure/Planning/PostgresReportList.cs','Backend/Lims.Infrastructure/Planning/ReportListSchema.cs',
'Backend/Lims.Infrastructure/Persistence/Migrations/20261119180000_AddExecutionReportList.cs',
'Shared/Lims.Contracts/Planning/PlanningContracts.cs','Shared/Lims.Contracts/Planning/ReportListContracts.cs',
'Frontend/Lims.Desktop/Planning/PlanningApiClient.cs','Frontend/Lims.Desktop/Planning/PlanningWorkspacePage.cs','Frontend/Lims.Desktop/Planning/PlanningReportListView.cs','Frontend/Lims.Desktop/Planning/PlanningSheetModel.cs','Frontend/Lims.Desktop/Planning/PlanningJobsView.cs','Frontend/Lims.Desktop/Planning/PlanningJobStatus.cs',
'Tests/Backend/Lims.Application.Tests/ReportListRulesTests.cs','Tests/Backend/Lims.Infrastructure.Tests/PlanningPostgresTests.cs','Tests/Backend/Lims.Infrastructure.Tests/ReportListPostgresTests.cs','Tests/Backend/Lims.Api.Tests/PlanningAuthorizationTests.cs','Tests/Frontend/Lims.Desktop.Tests/PlanningSheetTests.cs',
'docs/report-list-identidad-ejecuciones.md','scripts/planning/upgrade-execution-report-list.sql',
'scripts/start-report-list-validation.ps1','scripts/validate-report-list-api.ps1','scripts/validate-report-list-desktop.ps1','scripts/validate-report-list-permissions.ps1','scripts/package-report-list-evidence.ps1',
'artifacts/report-list/real-api-inspection.json','artifacts/report-list/real-authorization.json','artifacts/report-list/real-mark-identity.json','artifacts/report-list/real-mark-persistence.json','artifacts/report-list/native-Mark.json','artifacts/report-list/native-Correct.json','artifacts/report-list/acceptance-final.json',
'artifacts/report-list/tests/report-authorization.trx','artifacts/report-list/tests/report-rules.trx','artifacts/report-list/tests/report-postgres.trx','artifacts/report-list/tests/report-desktop.trx',
'artifacts/report-list/ui/01-consulta-nombres-planificacion-dos-ph.png','artifacts/report-list/ui/02-report-list-dos-ph-reales.png','artifacts/report-list/ui/03-report-list-Mark.png','artifacts/report-list/ui/03-report-list-Correct.png'
)
$manifest=@($relative|ForEach-Object{
    $path=Join-Path $workspace $_
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw ('Missing evidence: '+$_)}
    [pscustomobject]@{File=$_;Bytes=(Get-Item -LiteralPath $path).Length;Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
})
$manifestPath=Join-Path $workspace 'artifacts/report-list/manifest.json'
$manifest|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $manifestPath -Encoding UTF8
$archive=Join-Path $workspace 'ReportList-Identidad-2026-11-19.zip'
Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
$stream=[IO.File]::Open($archive,[IO.FileMode]::CreateNew)
try{
    $zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$true)
    try{
        foreach($file in @($relative)+@('artifacts/report-list/manifest.json')){
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $workspace $file),$file,[IO.Compression.CompressionLevel]::Optimal)|Out-Null
        }
    }finally{$zip.Dispose()}
}finally{$stream.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try{
    if($zip.Entries.Count -ne $relative.Count+1){throw 'Unexpected ZIP entries.'}
    foreach($file in $manifest){
        $entry=$zip.Entries|Where-Object{$_.FullName.Replace('\','/') -eq $file.File}
        if(@($entry).Count -ne 1){throw ('Missing or duplicated ZIP entry: '+$file.File)}
        $inputStream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
        try{$hash=[BitConverter]::ToString($sha.ComputeHash($inputStream)).Replace('-','');if($hash -ne $file.Sha256){throw ('ZIP hash differs: '+$file.File)}}finally{$sha.Dispose();$inputStream.Dispose()}
    }
}finally{$zip.Dispose()}
Write-Output ('Verified ZIP: '+[IO.Path]::GetFileName($archive)+'; files='+($relative.Count+1)+'; SHA256='+(Get-FileHash $archive).Hash)
