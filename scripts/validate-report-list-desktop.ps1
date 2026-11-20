param([ValidateSet('Consulta','Report','Mark','Correct','Inspect','Overview','Pending','Checked','Thousands','Dossier')][string]$Step='Consulta')
$ErrorActionPreference='Stop'
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'validate-planning-desktop.ps1'))
$reportScriptsRoot=$PSScriptRoot;$begin=$source.IndexOf('$ErrorActionPreference=');$end=$source.IndexOf("if(`$Action -eq 'Inspect')")
Invoke-Expression $source.Substring($begin,$end-$begin).Replace('$PSScriptRoot','$reportScriptsRoot')
$process=Get-Process -Id $window.Current.ProcessId
if(-not $process.Path.StartsWith((Join-Path $workspace 'artifacts/planificacion-dev/desktop-validation-'),[StringComparison]::OrdinalIgnoreCase)){throw 'Only isolated validation client.'}
$evidence=Join-Path $workspace 'artifacts/report-list/ui';New-Item -ItemType Directory -Path $evidence -Force|Out-Null
function Select-Combo([string]$name,[string]$item){
    $combo=Find-Named $name
    $combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $choice=Find-Named $item
    $choice.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
}
function Wait-Text([string]$text){Find-Named $text|Out-Null}
function Report-Items{
    try{
        $list=Find-Named 'Ejecuciones de Report List'
        @($list.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem)))
    }catch [Windows.Automation.ElementNotAvailableException]{return @()}
}
if($Step -eq 'Inspect'){Inspect-Window;Capture-Window $window 'current';exit}
if($Step -eq 'Consulta'){
    Invoke-Named 'Consulta de Jobs';Invoke-Named 'Filtros';Select-Combo 'Alcance de datos' 'Histórico completo';Invoke-Named 'Aplicar filtros'
    Set-NamedValue 'Buscar JOB' 'GUAT26022934';Invoke-Named 'Buscar'
    $deadline=[DateTime]::UtcNow.AddSeconds(35)
    do{
        $ph=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|Where-Object{$_.Current.Name -match '^F\d+ C5 .*PH$'})
        if($ph.Count -eq 2){break};Start-Sleep -Milliseconds 200
    }while([DateTime]::UtcNow -lt $deadline)
    if($ph.Count -ne 2){Inspect-Window;throw 'Consulta did not expose both real PH cells.'}
    Capture-Window $window '01-consulta-nombres-planificacion-dos-ph';Inspect-Window
}
if($Step -eq 'Report'){
    Invoke-Named 'Report List / Digitación';Set-NamedValue 'Buscar JOB en Report List' 'GUAT26022934'
    Invoke-Named 'Filtros de Report List';Wait-Text 'Prueba de Planificación';Set-NamedValue 'Prueba de Planificación' 'PH';Set-NamedValue 'Laboratorio' 'Chemical';Invoke-Named 'Aplicar filtros';Select-Combo 'Vista' 'Todas';Invoke-Named 'Buscar Report List'
    $deadline=[DateTime]::UtcNow.AddSeconds(30)
    do{$items=@(Report-Items);if($items.Count -eq 2){break};Start-Sleep -Milliseconds 200}while([DateTime]::UtcNow -lt $deadline)
    if($items.Count -ne 2){Inspect-Window;throw 'Expected two PH rows in connected Report List.'}
    Capture-Window $window '02-report-list-dos-ph-reales';Inspect-Window
}
if($Step -in @('Mark','Correct')){
    $openDialog=$window.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'Guardar marcado'))
    if($openDialog -and -not $openDialog.Current.IsOffscreen){Invoke-Named 'Cancelar';Start-Sleep -Milliseconds 200}
    $items=@(Report-Items);if($items.Count -ne 2){throw 'Load the real two PH rows first.'}
    $chosen=if($Step -eq 'Correct'){$items|Where-Object{$_.Current.Name -like '*Digitado*'}|Select-Object -First 1}else{$items|Where-Object{$_.Current.Name -notlike '*Digitado*'}|Select-Object -First 1}
    if(-not $chosen){Inspect-Window;throw 'Expected PH state was not found.'}
    $chosen.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    if($Step -eq 'Mark'){
        $selectedId=[regex]::Match($chosen.Current.Name,'[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}').Value
        $otherId=[regex]::Match(($items|Where-Object{$_.Current.Name -ne $chosen.Current.Name}|Select-Object -First 1).Current.Name,'[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}').Value
        $inspection=Get-Content (Join-Path (Split-Path $evidence) 'real-api-inspection.json') -Raw|ConvertFrom-Json
        @{Selected=$selectedId;Other=$otherId;Episode=$inspection.Ph[0].execution.episodeId;Job='GUAT26022934'}|ConvertTo-Json|Set-Content (Join-Path (Split-Path $evidence) 'real-mark-identity.json') -Encoding UTF8
    }
    if($Step -eq 'Correct'){Invoke-Named 'Corregir marcado'}else{
        $check=Find-Named ('CHK GUAT26022934 · PH · '+$selectedId)
        $check.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
    }
    Wait-Text 'Motivo obligatorio'
    Set-NamedValue 'Motivo obligatorio' 'Validacion WinUI local autorizada de una ejecucion PH real'
    Invoke-Named 'Guardar marcado'
    $deadline=[DateTime]::UtcNow.AddSeconds(30)
    do{
        $names=@(Report-Items|ForEach-Object{$_.Current.Name})
        $done=@($names|Where-Object{$_ -like '*Digitado*'}).Count
        if(($Step -eq 'Mark' -and $done -eq 1) -or ($Step -eq 'Correct' -and $done -eq 0)){break};Start-Sleep -Milliseconds 150
    }while([DateTime]::UtcNow -lt $deadline)
    if(($Step -eq 'Mark' -and $done -ne 1) -or ($Step -eq 'Correct' -and $done -ne 0)){Inspect-Window;throw 'Native marking did not reach expected state.'}
    Capture-Window $window ('03-report-list-'+$Step);Inspect-Window
    @{Step=$Step;Passed=$true;Boundary='Native WinUI interaction against authenticated API and restored real database';Rows=$names;Utc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json -Depth 4|Set-Content (Join-Path (Split-Path $evidence) ('native-'+$Step+'.json')) -Encoding UTF8
}
if($Step -eq 'Dossier'){
    $items=@(Report-Items);if($items.Count -ne 2){throw 'First load the two PH rows using -Step Report.'}
    $chosen=$items|Where-Object{$_.Current.Name -like '*Digitado*'}|Select-Object -First 1
    if(-not $chosen){throw 'Marked execution not visible after restart.'}
    $chosen.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select();Invoke-Named 'Expediente'
    $main=$window;$deadline=[DateTime]::UtcNow.AddSeconds(30);$detail=$null
    do{
        $detail=[Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id))|Where-Object{$_.Current.Name -eq 'Estado del JOB · GUAT26022934'}|Select-Object -First 1
        if($detail){break};Start-Sleep -Milliseconds 150
    }while([DateTime]::UtcNow -lt $deadline)
    if(-not $detail){throw 'Dossier window not opened.'};$window=$detail
    Invoke-Named 'Pruebas';Start-Sleep -Milliseconds 250
    $texts=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|ForEach-Object{$_.Current.Name})
    if(@($texts|Where-Object{$_ -eq 'PH'}).Count -ne 2 -or @($texts|Where-Object{$_ -match '^Digitad[oa]$'}).Count -ne 1){$texts|ConvertTo-Json|Set-Content (Join-Path (Split-Path $evidence) 'dossier-debug.json') -Encoding UTF8;throw 'Dossier did not show independent PH states.'}
    $technical=$window.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'Determination of pH for Leather'))
    $row=$technical
    while($row -and $row.Current.ControlType -ne [Windows.Automation.ControlType]::ListItem){$row=[Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($row)}
    if($row){$scroll=$null;if($row.TryGetCurrentPattern([Windows.Automation.ScrollItemPattern]::Pattern,[ref]$scroll)){$scroll.ScrollIntoView();Start-Sleep -Milliseconds 200}}
    Capture-Window $window 'recovery-Dossier-tests'
    Invoke-Named 'Historial';Start-Sleep -Milliseconds 250
    $identity=Get-Content (Join-Path (Split-Path $evidence) 'real-mark-identity.json') -Raw|ConvertFrom-Json
    $history=@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|Where-Object{-not $_.Current.IsOffscreen}|ForEach-Object{$_.Current.Name})
    if(-not($history|Where-Object{$_ -like ('*'+$identity.Selected+'*')})){throw 'Execution identity missing from visible history.'}
    Capture-Window $window 'recovery-Dossier-history'
    @{Passed=$true;Boundary='Native WinUI Release after restart, restored TIPS database';Job=$identity.Job;Execution=$identity.Selected;Utc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json|Set-Content (Join-Path (Split-Path $evidence) 'native-dossier.json') -Encoding UTF8
    Invoke-Named 'Cerrar';$window=$main
}
if($Step -in @('Overview','Pending','Checked','Thousands')){
    Invoke-Named 'Report List / Digitación'
    if($Step -in @('Overview','Thousands')){
        Set-NamedValue 'Buscar JOB en Report List' ''
        Invoke-Named 'Filtros de Report List';Wait-Text 'Prueba de Planificación';Invoke-Named 'Limpiar filtros'
        Select-Combo 'Vista' 'Todas';Invoke-Named 'Buscar Report List'
        $deadline=[DateTime]::UtcNow.AddSeconds(30)
        do{$items=@(Report-Items);if($items.Count -gt 3){break};Start-Sleep -Milliseconds 150}while([DateTime]::UtcNow -lt $deadline)
        if($items.Count -lt 4){throw 'Real overview rows not loaded.'}
    }else{
        Set-NamedValue 'Buscar JOB en Report List' 'GUAT26022934'
        Invoke-Named 'Filtros de Report List';Wait-Text 'Prueba de Planificación';Set-NamedValue 'Prueba de Planificación' 'PH';Set-NamedValue 'Laboratorio' 'Chemical';Invoke-Named 'Aplicar filtros'
        Select-Combo 'Vista' $(if($Step -eq 'Checked'){'Chequeados'}else{'Pendientes'});Invoke-Named 'Buscar Report List'
        $deadline=[DateTime]::UtcNow.AddSeconds(30)
        do{$items=@(Report-Items);if($items.Count -eq 1){break};Start-Sleep -Milliseconds 150}while([DateTime]::UtcNow -lt $deadline)
        if($items.Count -ne 1){throw 'Expected one independent PH execution in this view.'}
    }
    if($Step -eq 'Thousands'){
        $timer=[Diagnostics.Stopwatch]::StartNew();$loaded=0
        for($attempt=0;$attempt -lt 15 -and $loaded -lt 2000;$attempt++){
            $list=Find-Named 'Ejecuciones de Report List';$pattern=$null
            if(-not $list.TryGetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern,[ref]$pattern)){throw 'Native table has no scroll provider.'}
            $pattern.SetScrollPercent(-1,100);Start-Sleep -Milliseconds 650
            $all=$window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)
            $label=$all|Where-Object{$_.Current.Name -match '^([\d,]+) de ([\d,]+) ejecuciones'}|Select-Object -First 1
            if($label -and $label.Current.Name -match '^([\d,]+) de'){$loaded=[int]($matches[1] -replace ',','')}
        }
        $timer.Stop();if($loaded -lt 2000){throw "Incremental native loading stopped at $loaded rows."}
        $visible=@(Report-Items).Count;$native=Get-Process -Id $window.Current.ProcessId
        @{Boundary='Native WinUI Release, restored TIPS database';Loaded=$loaded;RealizedContainers=$visible;ElapsedMs=$timer.ElapsedMilliseconds;WorkingSetBytes=$native.WorkingSet64;PrivateBytes=$native.PrivateMemorySize64;Utc=[DateTime]::UtcNow.ToString('O')}|ConvertTo-Json|Set-Content (Join-Path (Split-Path $evidence) 'native-thousands.json') -Encoding UTF8
    }
    Capture-Window $window ('recovery-'+$Step);Inspect-Window
}
Write-Output ('PASS native '+$Step)
