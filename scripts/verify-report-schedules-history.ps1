$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'));$out=Join-Path $workspace 'artifacts/report-schedules'
$r=Get-Content (Join-Path $workspace 'artifacts/planificacion-dev/runtime.json') -Raw|ConvertFrom-Json
if($r.Host -ne '127.0.0.1' -or $r.Port -ne 55441 -or $r.Database -ne 'lims_planning_dev'){throw 'Only restored local database.'}
$backup=Get-ChildItem $out -Filter 'before-schedules-*.backup'|Sort-Object LastWriteTimeUtc|Select-Object -First 1
if(-not $backup){throw 'Pre-migration backup missing'}
$prior=Join-Path $out 'before-report-checks.sql'
& (Join-Path $r.PostgresBin 'pg_restore.exe') --data-only --schema planning --table report_checks --file $prior $backup.FullName
if($LASTEXITCODE -ne 0){throw 'Could not read backup'}
$records=@([IO.File]::ReadAllLines($prior)|Where-Object{$_ -match '^[0-9a-f]{8}-'})
if($records.Count -lt 1){throw 'No actual historical report checks found in backup'}
$saved=$env:PGPASSWORD;$env:PGPASSWORD=$r.Password;$verified=@()
try{
 foreach($line in $records){
  $f=$line.Split("`t");$id=[Guid]::Parse($f[0]);$block=[long]$f[2];$at=[DateTimeOffset]::Parse($f[4],[Globalization.CultureInfo]::InvariantCulture).ToString('O')
  $sql="SELECT count(*) FROM planning.history WHERE action='ReportMarked' AND details->>'executionId'='$id' AND (details->>'blockNumber')::bigint=$block AND (details->'after'->>'digitizedAt')::timestamptz='$at'::timestamptz"
  $count=& (Join-Path $r.PostgresBin 'psql.exe') -h 127.0.0.1 -p 55441 -U $r.Username -d lims_planning_dev -X -At -v ON_ERROR_STOP=1 -c $sql
  if($LASTEXITCODE -ne 0 -or [int]$count -lt 1){throw 'Original historical clock or block no longer traceable'}
  $verified+=@{ExecutionId=$id;OriginalBlock=$block;OriginalClock=$at;OriginalAuditPreserved=$true}
 }
 $schema=[IO.File]::ReadAllText((Join-Path $workspace 'Backend/Lims.Infrastructure/Planning/ReportScheduleSchema.cs'))
 if($schema -match '(?i)\b(UPDATE|DELETE|DROP|TRUNCATE|RENAME)\b'){throw 'Migration contains a non-additive statement'}
 @{Passed=$true;Boundary='Actual pre-migration pg_dump compared with persisted original audit; earlier local acceptance mark was later corrected explicitly, not converted by migration';Backup=$backup.Name;BackupSha256=(Get-FileHash $backup.FullName).Hash;OriginalRecords=$verified;NoHistoricalBackfill=$true;Utc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json -Depth 7|Set-Content (Join-Path $out 'real-historical-compatibility.json') -Encoding UTF8
 Write-Output 'PASS: original real block and clock remain traceable; additive migration performs no historical conversion.'
}finally{$env:PGPASSWORD=$saved}
