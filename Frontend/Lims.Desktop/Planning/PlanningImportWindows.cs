using System.Globalization;
using System.Text;
using Lims.Contracts.Planning;
using Lims.DesignSystem.Controls;
using Lims.DesignSystem.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Windows.ApplicationModel.DataTransfer;

namespace Lims.Desktop.Planning;

/// <summary>The mandatory review uses the persisted reconciliation plan. It never applies local simulated state.</summary>
public sealed class PlanningImportReviewWindow : Window
{
    private readonly PlanningApiClient _api;
    private readonly Func<Task<PlanningImportInspection?>> _reload;
    private readonly Func<PlanningImportInspection,Task<bool>> _confirm;
    private readonly Func<Guid,Task> _open;
    private readonly bool _canClassify;
    private PlanningImportInspection _data;
    private readonly Grid _layout=PlanningUi.Rows(GridLength.Auto,GridLength.Auto,GridLength.Auto,new(1,GridUnitType.Star),GridLength.Auto);
    private readonly TextBlock _metrics=PlanningUi.Text("",14),_message=PlanningUi.Text("",12,true),_footer=PlanningUi.Text("",12,true);
    private readonly CheckBox _defer=new(){Content="Importar registros válidos y dejar explícitamente las equivalencias pendientes"};
    private readonly ComboBox _section=new(){ItemsSource=new[]{"A · Emergencias nuevas","B · Summary sin Report List","C · Report List sin Summary","D · Buyer y Vendor","E · Pruebas sin nombre asignado"},SelectedIndex=0,MinWidth=310};
    private readonly TextBox _search=PlanningUi.Field("Buscar JOB o prueba",max:2000);
    private readonly ListView _rows=new(){SelectionMode=ListViewSelectionMode.Extended,IsMultiSelectCheckBoxEnabled=false};
    private readonly Grid _table=PlanningUi.Rows(new GridLength(36),new(1,GridUnitType.Star));
    private readonly ContentControl _tableHeader=new(){HorizontalContentAlignment=HorizontalAlignment.Stretch};
    private string? _tableCategory;
    private readonly Button _save,_classify,_dossier;
    private readonly ProgressBar _progress=new(){Height=2,Visibility=Visibility.Collapsed,IsIndeterminate=true};
    private bool _busy;
    private PlanningImportInspectionRow[] _visible=[];
    public PlanningImportReviewWindow(PlanningApiClient api,PlanningImportInspection data,bool canClassify,Func<Task<PlanningImportInspection?>> reload,
        Func<PlanningImportInspection,Task<bool>> confirm,Func<Guid,Task> open)
    {
        _api=api;_data=data;_canClassify=canClassify;_reload=reload;_confirm=confirm;_open=open;
        Title="Revisa antes de agregar";_layout.Padding=new(22);_layout.Background=PlanningUi.Brush("LimsBackgroundBrush");_layout.RowSpacing=10;
        var title=PlanningUi.Stack(PlanningUi.Text("Nuevas del archivo a Planificación",12,true),PlanningUi.Text("Revisa antes de agregar",24),_metrics);title.Spacing=5;
        PlanningUi.Place(_layout,PlanningUi.Surface(title),0);
        _section.Style=(Style)Application.Current.Resources["LimsCompactComboBoxStyle"];AutomationProperties.SetName(_section,"Sección de revisión previa");
        _search.Header=null;_search.PlaceholderText="Buscar JOB o prueba";_search.Width=230;AutomationProperties.SetName(_search,"Buscar en revisión previa");
        Button export=PlanningUi.Button("Exportar",Export);
        _classify=PlanningUi.Button("Asignar equivalencia",Classify);_dossier=PlanningUi.Button("Completar datos / expediente",OpenDossier);
        var controls=PlanningUi.Row(_section,_search,export,_classify,_dossier);controls.Spacing=8;
        PlanningUi.Place(_layout,new ScrollViewer{Content=controls,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled},1);
        PlanningUi.Place(_layout,PlanningUi.Stack(_progress,_message),2);
        _table.RowSpacing=0;_table.HorizontalAlignment=HorizontalAlignment.Left;
        _rows.ItemContainerStyle=(Style)Application.Current.Resources["LimsDataRowStyle"];
        AutomationProperties.SetName(_rows,"Registros de revisión antes de agregar");PlanningUi.Place(_table,_tableHeader,0);PlanningUi.Place(_table,_rows,1);
        PlanningUi.Place(_layout,PlanningUi.Surface(new ScrollViewer{Content=_table,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalContentAlignment=VerticalAlignment.Stretch}),3);
        _save=PlanningUi.Button("Confirmar importación",Confirm,true,CanConfirm);AutomationProperties.SetName(_save,"Confirmar importación");
        var cancel=PlanningUi.Button("Cancelar",()=>{Close();return Task.CompletedTask;});
        var buttons=PlanningUi.Row(cancel,_save);buttons.HorizontalAlignment=HorizontalAlignment.Right;
        PlanningUi.Place(_layout,PlanningUi.Surface(PlanningUi.Stack(_footer,_defer,buttons)),4);Content=_layout;
        ConsultationVisuals.FitWindow(this,1500,900);AppWindow.SetIcon("Assets/Branding/LIMS.ico");
        _section.SelectionChanged+=(_,_)=>Filter();_search.TextChanged+=(_,_)=>Filter();_rows.SelectionChanged+=(_,_)=>Actions();
        _defer.Checked+=(_,_)=>Actions();_defer.Unchecked+=(_,_)=>Actions();
        _rows.KeyDown+=async(_,e)=>{if(Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)){if(e.Key==Windows.System.VirtualKey.C){await Copy();e.Handled=true;}else if(e.Key==Windows.System.VirtualKey.A){_rows.SelectAll();e.Handled=true;}}};
        _layout.Loaded+=(_,_)=>{if(Motion.Enabled)Motion.Enter(_layout,y:4,milliseconds:140);};Paint();
    }
    private void Paint()
    {
        var p=_data.Preview;_metrics.Text=$"{_data.NewCombinations:N0} combinaciones JOB + prueba nuevas · {p.NewJobs:N0} JOBs nuevos · Existentes: {p.UnchangedJobs+p.UpdatedJobs:N0} JOBs / {_data.ExistingTests:N0} pruebas · {_data.NewEmergencies:N0} emergencias nuevas";
        _footer.Text=$"{_data.ProcessedRows:N0} filas fuente a procesar · {p.NewTests:N0} ejecuciones nuevas · {_data.PendingEquivalences:N0} nombres por clasificar · {p.Issues.Count(i=>i.Blocking)} errores críticos";
        _defer.Visibility=_data.PendingEquivalences>0?Visibility.Visible:Visibility.Collapsed;
        _message.Text=string.Join(" · ",p.Issues.Where(i=>i.Blocking).Select(i=>$"{i.File}, fila {i.Row}: {i.Message}"));Filter();
    }
    private void Filter()
    {
        var category=_section.SelectedIndex switch{1=>"SummaryWithoutReport",2=>"ReportWithoutSummary",3=>"MissingParty",4=>"MissingEquivalence",_=>"Emergency"};
        BuildRows(category);
        _visible=_data.Items.Where(r=>(r.Category==category||category=="MissingParty"&&r.Category=="PartyComplemented")&&((r.Job+" "+r.OriginalTest+" "+r.Explanation).Contains(_search.Text,StringComparison.OrdinalIgnoreCase))).ToArray();
        _rows.ItemsSource=_visible;_message.Text=$"{_visible.Length:N0} registros en esta sección. "+(_section.SelectedIndex is 1 or 2?"Report List no contiene fechas de cobertura verificables; la ausencia no demuestra pérdida.":_section.SelectedIndex==4?"Agrupados por nombre original; conserva los JOBs relacionados.":"");Actions();
    }
    private void BuildRows(string category)
    {
        if(_tableCategory==category)return;_tableCategory=category;
        (string Key,string Label,int Width)[] columns=category switch
        {
            "MissingEquivalence"=>[("OriginalTest","Prueba original del archivo",320),("RelatedJobs","JOBs",65),("Job","JOBs relacionados",420),("Explanation","Decisión pendiente",470)],
            "Emergency"=>[("Job","JOB",210),("OriginalTest","Prueba",300),("Service","Servicio de emergencia",230),("InDateText","Ingreso",120),("DueDateText","Due Date",120),("Explanation","Procedencia",380)],
            "MissingParty"=>[("Job","JOB",205),("Buyer","Buyer",220),("Vendor","Vendor",220),("Service","Servicio",120),("Explanation","Faltante / procedencia verificada",520)],
            _=>[("Job","JOB",205),("InDateText","Fecha ingreso",115),("DueDateText","Due Date",115),("Buyer","Buyer",210),("Vendor","Vendor",210),("Service","Servicio",120),("Usd","USD",90),("OfficialState","Estado fuente",150),("Explanation","Cobertura / motivo",450)]
        };
        var header=new Grid{Background=PlanningUi.Brush("LimsSurfaceSecondaryBrush")};
        foreach(var (_,label,width) in columns){var i=header.ColumnDefinitions.Count;header.ColumnDefinitions.Add(new(){Width=new(width)});var text=PlanningUi.Text(label,12,true);text.Margin=new(8,0,0,0);text.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(text,i);header.Children.Add(text);}
        var defs=string.Concat(columns.Select(c=>$"<ColumnDefinition Width='{c.Width}'/>"));
        var cells=string.Concat(columns.Select((c,i)=>$"<TextBlock Grid.Column='{i}' Text=\"{{Binding {c.Key}, TargetNullValue='No informado'}}\" ToolTipService.ToolTip='{{Binding {c.Key}}}' TextTrimming='CharacterEllipsis' Margin='8,5' VerticalAlignment='Center' FontSize='13' Foreground='{{ThemeResource LimsTextPrimaryBrush}}'/>"));
        _rows.ItemTemplate=(DataTemplate)XamlReader.Load($"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid MinHeight='34'><Grid.ColumnDefinitions>{defs}</Grid.ColumnDefinitions>{cells}</Grid></DataTemplate>");
        _table.Width=columns.Sum(c=>c.Width);_tableHeader.Content=header;
    }
    private void Actions()
    {
        _save.IsEnabled=CanConfirm();
        _classify.Visibility=_section.SelectedIndex==4?Visibility.Visible:Visibility.Collapsed;_classify.IsEnabled=!_busy&&_canClassify&&_rows.SelectedItems.Count==1&&_rows.SelectedItem is PlanningImportInspectionRow{OriginalTest:not null};
        _dossier.IsEnabled=!_busy&&_rows.SelectedItems.Count==1&&_rows.SelectedItem is PlanningImportInspectionRow{EpisodeId:not null};
    }
    private bool CanConfirm()=>!_busy&&_data.Preview.Status=="Preview"&&!_data.Preview.Issues.Any(i=>i.Blocking)&&(_data.PendingEquivalences==0||_defer.IsChecked==true);
    private async Task Confirm()
    {
        if(!CanConfirm())return;_busy=true;_progress.Visibility=Visibility.Visible;Actions();
        try{if(await _confirm(_data))Close();else _message.Text="No se confirmó. Actualiza la validación antes de intentar de nuevo.";}
        finally{_busy=false;_progress.Visibility=Visibility.Collapsed;Actions();}
    }
    private Task Copy()
    {
        var selected=_rows.SelectedItems.Cast<PlanningImportInspectionRow>().ToArray();if(selected.Length==0)return Task.CompletedTask;
        var package=new DataPackage();package.SetText(string.Join("\r\n",selected.Select(r=>string.Join('\t',Values(r).Select(v=>(v??"").Replace('\t',' ').Replace('\r',' ').Replace('\n',' '))))));Clipboard.SetContent(package);Clipboard.Flush();return Task.CompletedTask;
    }
    private static string?[] Values(PlanningImportInspectionRow r)=>[r.Job,r.OriginalTest,r.Equivalence,r.Laboratory,r.Service,r.InDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),r.DueDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),r.Buyer,r.Vendor,r.Usd?.ToString(CultureInfo.InvariantCulture),r.OfficialState,r.Explanation];
    private async Task Export()
    {
        var csv=new StringBuilder("\uFEFFJOB,Prueba original,Equivalencia,Laboratorio,Servicio,Ingreso,Due Date,Buyer,Vendor,USD,Estado fuente,Motivo\r\n");
        foreach(var row in _visible)csv.AppendLine(string.Join(',',Values(row).Select(PlanningReviewExport.Cell)));
        try{var path=await PlanningUi.PickFile(_layout.XamlRoot,true);if(path is not null)await File.WriteAllTextAsync(path,csv.ToString(),new UTF8Encoding(false));}catch(Exception e)when(e is IOException or UnauthorizedAccessException){_message.Text=e.Message;}
    }
    private async Task OpenDossier()
    {
        if(_rows.SelectedItem is not PlanningImportInspectionRow{EpisodeId:{} id})return;
        var existing=await _api.Dossier(id,CancellationToken.None);
        if(existing.IsSuccess)await _open(id);else _message.Text="JOB nuevo del lote: el expediente y la edición auditada estarán disponibles después de confirmar la importación.";
    }
    private async Task Classify()
    {
        if(_rows.SelectedItem is not PlanningImportInspectionRow{OriginalTest:{} original}||!_canClassify)return;
        var result=await PlanningEquivalenceEditor.Show(_api,_layout.XamlRoot,original);if(!result)return;
        _busy=true;Actions();_progress.Visibility=Visibility.Visible;
        try{var refreshed=await _reload();if(refreshed is not null){_data=refreshed;_defer.IsChecked=false;Paint();}else _message.Text="Clasificación guardada. Cierra y vuelve a validar los archivos antes de confirmar.";}
        finally{_busy=false;_progress.Visibility=Visibility.Collapsed;Actions();}
    }
}

internal static class PlanningReviewExport
{
    public static string Cell(string? value){var text=value??"";if(text.Length>0&&"=+-@\t\r".Contains(text[0],StringComparison.Ordinal))text="'"+text;return "\""+text.Replace("\"","\"\"",StringComparison.Ordinal)+"\"";}
}

internal static class PlanningEquivalenceEditor
{
    private sealed record Choice(PlanningEquivalence Map,string Label);
    public static async Task<bool> Show(PlanningApiClient api,XamlRoot root,string original)
    {
        var result=await api.Equivalences(CancellationToken.None);if(!result.IsSuccess||result.Value is null)return false;
        var current=result.Value.FirstOrDefault(m=>m.OriginalTest.Equals(original,StringComparison.OrdinalIgnoreCase));
        var search=new LimsSearchSelect{PlaceholderText="Buscar clasificación del catálogo",DisplayMemberPath=nameof(Choice.Label),ItemsSource=result.Value.Where(m=>!m.NoProgram).Select(m=>new Choice(m,$"{m.OriginalTest} · {m.Equivalence} · {m.Laboratory}")).ToArray()};
        TextBox eq=PlanningUi.Field("Nombre de Planificación",current?.Equivalence),lab=PlanningUi.Field("Laboratorio",current?.Laboratory),reason=PlanningUi.Field("Motivo y fuente verificable",max:1000);
        AutomationProperties.SetName(eq,"Nombre de Planificación");AutomationProperties.SetName(lab,"Laboratorio de equivalencia");AutomationProperties.SetName(reason,"Motivo de clasificación");
        var no=new CheckBox{Content="NO PROGRAMAR",IsChecked=current?.NoProgram==true};
        search.SelectionChanged+=(_,_)=>{if(search.SelectedItem is Choice{Map:{} map}){eq.Text=map.Equivalence??"";lab.Text=map.Laboratory??"";}};
        var message=PlanningUi.Text("",12,true);var body=PlanningUi.Stack(PlanningUi.Text(original,15),search,eq,lab,no,reason,message);
        var dialog=new ContentDialog{XamlRoot=root,Title="Asignar nombre de Planificación",Content=new ScrollViewer{Content=body,MaxHeight=470},PrimaryButtonText="Guardar clasificación",CloseButtonText="Dejar pendiente",DefaultButton=ContentDialogButton.Close};
        var saved=false;
        dialog.PrimaryButtonClick+=async(_,e)=>{e.Cancel=true;var deferral=e.GetDeferral();dialog.IsPrimaryButtonEnabled=false;try{var response=await api.SaveEquivalence(new(original,eq.Text,lab.Text,no.IsChecked==true,current?.Version,reason.Text),CancellationToken.None);if(response.IsSuccess){saved=true;dialog.Hide();}else message.Text=response.Error??"No se guardó.";}finally{dialog.IsPrimaryButtonEnabled=true;deferral.Complete();}};
        await dialog.ShowAsync();return saved;
    }
}

public sealed class PlanningImportSummaryWindow : Window
{
    public PlanningImportSummaryWindow(PlanningImportInspection data,Func<Guid,Task> jobs,Func<Guid,Task> reviews)
    {
        Title="Resumen de importación";var p=data.Preview;var layout=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star),GridLength.Auto);layout.Padding=new(24);layout.Background=PlanningUi.Brush("LimsBackgroundBrush");
        PlanningUi.Place(layout,PlanningUi.Surface(PlanningUi.Stack(PlanningUi.Text("Resumen de importación",24),PlanningUi.Text($"Lote {p.Id} · {data.AppliedAt?.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {data.ActorName??"Usuario no informado"}",12,true),PlanningUi.Text($"Corte {p.Cut.Window} · día {p.Cut.Day:yyyy-MM-dd} · Confirmado",13))),0);
        var body=PlanningUi.Stack(PlanningUi.Text($"{p.NewJobs:N0} JOBs nuevos · {p.NewTests:N0} pruebas nuevas",20),
            PlanningUi.Text($"{p.UpdatedJobs:N0} JOBs actualizados · {p.UnchangedJobs:N0} JOBs existentes sin cambios · {data.ExistingTests:N0} pruebas existentes observadas",14),
            PlanningUi.Text($"{p.Changes.Where(c=>c.Entity=="WIP"&&c.Action=="UpdateSource").Select(c=>c.EpisodeId).Distinct().Count():N0} cambios de WIP · {p.LinkedManualTests:N0} vínculos manuales · {p.PendingReview:N0} observaciones de revisión del lote",14),
            PlanningUi.Text("Las novedades pueden coincidir en un JOB; no son categorías sumables ni equivalen a pendientes actuales.",12,true),
            PlanningUi.Text($"Inventario de episodios: {data.JobsBefore:N0} antes → {data.JobsAfter:N0} después · ejecuciones: {data.TestsBefore:N0} → {data.TestsAfter:N0}",13),
            PlanningUi.Text("Pruebas del lote por laboratorio",17));body.Spacing=12;
        foreach(var lab in data.Laboratories)body.Children.Add(PlanningUi.Text($"{lab.Laboratory}: {lab.NewTests:N0} nuevas · {lab.ObservedTests:N0} existentes · {lab.NoProgram:N0} NO PROGRAMAR (incluidas)",13));
        body.Children.Add(PlanningUi.Text($"Anomalías y avisos: {p.Issues.Count(i=>!i.Blocking)} · errores críticos: {p.Issues.Count(i=>i.Blocking)}",16));
        foreach(var issue in p.Issues)body.Children.Add(PlanningUi.Text($"{issue.File} · {issue.Message}",12,true));
        PlanningUi.Place(layout,new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new(6,18,6,18)},1);
        Button open=PlanningUi.Button("Ver registros importados",async()=>{await jobs(p.Id);Close();},true),pending=PlanningUi.Button("Revisar pendientes",async()=>{await reviews(p.Id);Close();}),close=PlanningUi.Button("Cerrar",()=>{Close();return Task.CompletedTask;});
        PlanningUi.Place(layout,PlanningUi.Surface(PlanningUi.Row(open,pending,close)),2);Content=layout;ConsultationVisuals.FitWindow(this,1100,790);AppWindow.SetIcon("Assets/Branding/LIMS.ico");
        layout.Loaded+=(_,_)=>{if(Motion.Enabled)Motion.Enter(layout,y:4,milliseconds:140);};
    }
}
