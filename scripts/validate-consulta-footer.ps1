param([int]$Width=1920,[int]$Height=1080)
$ErrorActionPreference='Stop'
# Reuse authenticated, isolated-runtime guards and real input helpers.
$Step='Footer';$EvidenceName=('consulta-footer-'+$Width+'x'+$Height)
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'validate-consulta-jobs.ps1'))
$footerScriptsRoot=$PSScriptRoot
$start=$source.IndexOf('$ErrorActionPreference=')
$end=$source.IndexOf("if(`$Step -eq 'Inspect')")
Invoke-Expression $source.Substring($start,$end-$start).Replace('(Join-Path $PSScriptRoot ','(Join-Path $footerScriptsRoot ').Replace('$consultaScriptsRoot=$PSScriptRoot','$consultaScriptsRoot=$footerScriptsRoot')
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class FooterWindow {
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int v,uint f);
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
}
'@
[FooterWindow]::SetThreadDpiAwarenessContext([IntPtr](-4))|Out-Null
$window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState([Windows.Automation.WindowVisualState]::Normal)
[FooterWindow]::SetWindowPos([IntPtr]$window.Current.NativeWindowHandle,[IntPtr]::Zero,0,0,$Width,$Height,0x40)|Out-Null
Start-Sleep -Milliseconds 800
$dpi=[FooterWindow]::GetDpiForWindow([IntPtr]$window.Current.NativeWindowHandle)
function Visible-Named([string]$name){@($window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$name))|Where-Object{-not $_.Current.IsOffscreen})}
function Assert-Footer {
    Start-Sleep -Milliseconds 250
    $footer=Find-Named 'Pie de Consulta de Jobs'
    $r=$footer.Current.BoundingRectangle
    if([Math]::Abs($r.Height-42*$dpi/96) -gt 2){throw "Altura del pie incorrecta: $($r.Height)."}
    $children=$footer.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)|Where-Object{-not $_.Current.IsOffscreen -and $_.Current.ControlType -in @([Windows.Automation.ControlType]::Text,[Windows.Automation.ControlType]::Button)}
    foreach($child in $children){$b=$child.Current.BoundingRectangle;if($b.Left -lt $r.Left -or $b.Right -gt $r.Right -or $b.Top -lt $r.Top -or $b.Bottom -gt $r.Bottom){throw ('Elemento fuera del pie: '+$child.Current.Name)}}
    $leaves=@($children)
    for($i=0;$i -lt $leaves.Count;$i++){for($j=$i+1;$j -lt $leaves.Count;$j++){if($leaves[$i].Current.BoundingRectangle.IntersectsWith($leaves[$j].Current.BoundingRectangle)){throw 'Elementos superpuestos en el pie.'}}}
    if((Visible-Named 'Ver expediente').Count){throw 'El acceso al expediente sigue duplicado en el pie.'}
    if((Visible-Named 'Resultados completos filtrados en servidor · selección sobre la página cargada.').Count){throw 'El mensaje técnico sigue en la interfaz.'}
    return $r
}
Search-Job $job
$page=Api-Jobs ('Search='+[uri]::EscapeDataString($job))
if($page.counts.executions -ne 24 -or $page.counts.jobs -ne 1){throw 'El JOB fuente no coincide con el caso de 24 ejecuciones.'}
if((Visible-Named 'Anterior').Count -or (Visible-Named 'Siguiente').Count){throw 'Hay paginación innecesaria para un JOB.'}
$singleFooter=Assert-Footer
Capture-Window $window '01-un-job'
Passed '24 ejecuciones · 1 JOB, una barra de 42 DIP, sin paginación ni expediente duplicado'
Select-First;Keys '+({RIGHT}{RIGHT}{DOWN}{DOWN}{DOWN}{DOWN})'
Wait-Text '5 filas × 3 columnas'|Out-Null
Keys '^c'
$expected=($page.items|Select-Object -First 5|ForEach-Object{$_.job+"`t"+$_.inDate+"`t"+$_.dueDate}) -join "`r`n"
if([Windows.Forms.Clipboard]::GetText() -ne $expected){throw 'Cambió el contenido del rango 5x3.'}
Start-Sleep -Milliseconds 5100
Wait-Text '5 filas × 3 columnas'|Out-Null
Capture-Window $window '02-rango-5x3'
Passed 'Indicador compacto 5 filas × 3 columnas; Ctrl+C conserva TSV y rango exactos'
Invoke-Named 'Atajos de teclado';Wait-Text 'Atajos de la tabla'|Out-Null
Capture-Window $window '03-ayuda-contextual'
Keys '{ESC}';(Find-Named 'Tabla de Consulta de Jobs').SetFocus();Keys '^c'
if([Windows.Forms.Clipboard]::GetText() -ne $expected){throw 'La ayuda modificó la selección.'}
Passed 'Ayuda de teclado contextual sin alterar selección ni copia'
Invoke-Named 'Restablecer filtros';$all=Api-Jobs 'PageSize=100';Wait-Text (Count-Text $all)|Out-Null
Wait-Text ('1–100 de '+$all.total.ToString('N0',[Globalization.CultureInfo]::InvariantCulture))|Out-Null
if((Find-Named 'Anterior').Current.IsEnabled -or -not (Find-Named 'Siguiente').Current.IsEnabled){throw 'Estado inicial de paginación incorrecto.'}
$multiFooter=Assert-Footer
Capture-Window $window '04-miles-primera-pagina'
Invoke-Named 'Siguiente';Wait-Text ('101–200 de '+$all.total.ToString('N0',[Globalization.CultureInfo]::InvariantCulture))|Out-Null
if(-not (Find-Named 'Anterior').Current.IsEnabled){throw 'Anterior no está habilitado en la segunda página.'}
Capture-Window $window '05-miles-segunda-pagina'
Invoke-Named 'Anterior';Wait-Text '1–100 de *'|Out-Null
Passed 'Totales del servidor filtrado, rango de página y Anterior/Siguiente funcionales'
$equivalence=($page.items|Where-Object{$_.equivalence}|Select-Object -First 1).equivalence
Invoke-Named 'Filtros';Set-NamedValue 'Equivalencia' $equivalence;Invoke-Named 'Aplicar filtros'
$filtered=Api-Jobs ('Equivalence='+[uri]::EscapeDataString($equivalence));Wait-Text (Count-Text $filtered)|Out-Null
if($filtered.total -le 100 -or $filtered.total -gt 200){throw 'El filtro fuente debe tener dos páginas para comprobar el extremo final.'}
Invoke-Named 'Siguiente';Wait-Text ('101–'+$filtered.total+' de '+$filtered.total)|Out-Null
if((Find-Named 'Siguiente').Current.IsEnabled -or -not (Find-Named 'Anterior').Current.IsEnabled){throw 'Estado final de paginación incorrecto.'}
Assert-Footer|Out-Null;Capture-Window $window '07-ultima-pagina-filtrada'
Passed 'Filtros activos actualizan contadores; última página deshabilita Siguiente'
Invoke-Named 'Restablecer filtros';Wait-Text (Count-Text $all)|Out-Null
Set-NamedValue 'Buscar JOB' 'UI-FOOTER-SIN-RESULTADOS';Invoke-Named 'Buscar';Wait-Text '0 ejecuciones · 0 JOBs'|Out-Null
if((Visible-Named 'Anterior').Count -or (Visible-Named 'Siguiente').Count){throw 'El estado vacío muestra paginación.'}
Assert-Footer|Out-Null
Passed 'Estado vacío sin paginación ni totales de una consulta anterior'
Search-Job $job;Select-First
Capture-Window $window '06-final'
@{Width=$Width;Height=$Height;Dpi=$dpi;SingleJobCounts=$page.counts;AllCounts=$all.counts;AllRecords=$all.total;FooterHeight=$singleFooter.Height;MultiFooterHeight=$multiFooter.Height;TableHeight=(Find-Named 'Tabla de Consulta de Jobs').Current.BoundingRectangle.Height;Passed=$checks.ToArray();Boundary='Native WinUI, Windows clipboard, authenticated isolated local PostgreSQL API'}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $evidence 'footer.json') -Encoding UTF8
