using System.Collections.ObjectModel;
using System.Globalization;
using Lims.Contracts.Planning;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Lims.Desktop.Planning;

/// <summary>A reviewable set of executions, independent of the main table's filter and page.</summary>
public sealed class ReportFixedTimeBatchDialog : ContentDialog,IDisposable
{
    private readonly PlanningApiClient _api;
    private readonly bool _administrator;
    private readonly Dictionary<Guid,ReportListItem> _chosen=[];
    private readonly ObservableCollection<ReportListItem> _selected=[];
    private readonly ListView _results=new(){SelectionMode=ListViewSelectionMode.Multiple,IsMultiSelectCheckBoxEnabled=true};
    private readonly ListView _selection=new(){SelectionMode=ListViewSelectionMode.Single};
    private readonly TextBox _search=PlanningUi.Field("",max:20000),_date=PlanningUi.Field("Fecha"),_time=PlanningUi.Field("Hora");
    private readonly TextBox _reason=PlanningUi.Field("Motivo","Digitación por lote con hora fija",1000);
    private readonly CheckBox _override=new(){Content="Excepción administrativa del modo de captura"};
    private readonly TextBlock _count=PlanningUi.Text("0 pruebas en el lote",12,true),_found=PlanningUi.Text("Busca un JOB para elegir sus pruebas pendientes.",12,true);
    private readonly InfoBar _message=new(){IsOpen=false,IsClosable=false,Severity=InfoBarSeverity.Error,Visibility=Visibility.Collapsed};
    private readonly DispatcherTimer _delay=new(){Interval=TimeSpan.FromMilliseconds(280)};
    private readonly Button _previous,_next;
    private readonly ContentControl _form=new(){IsTabStop=false,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
    private readonly Border _noticeOverlay=new(){Visibility=Visibility.Collapsed,Padding=new(18),CornerRadius=new(8),Background=new SolidColorBrush(Windows.UI.Color.FromArgb(160,0,0,0))};
    private readonly CancellationTokenSource _lifetime=new();
    private CancellationTokenSource? _query;
    private ReportDigitization? _retry;
    private bool _restoring,_saving,_closed,_closeRequested;
    private TaskCompletionSource<bool>? _scannerNotice;
    private int _page=1,_total;
    public ReportListResult? Saved {get;private set;}
    public ReportFixedTimeBatchDialog(PlanningApiClient api,XamlRoot root,DateTimeOffset at,bool administrator,IReadOnlyList<ReportListItem> initial)
    {
        _api=api;_administrator=administrator;XamlRoot=root;Style=(Style)Application.Current.Resources["LimsEditorDialogStyle"];
        Title="Digitar con hora fija";PrimaryButtonText="Confirmar y guardar lote";CloseButtonText="Cancelar";DefaultButton=ContentDialogButton.None;
        PrimaryButtonStyle=(Style)Application.Current.Resources["LimsPrimaryButtonStyle"];CloseButtonStyle=(Style)Application.Current.Resources["LimsStandardsSecondaryButtonStyle"];
        Resources["ContentDialogMaxWidth"]=1040d;
        _date.Text=at.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);_time.Text=at.ToString("HH:mm",CultureInfo.InvariantCulture);
        _date.Width=150;_time.Width=95;_time.MinWidth=80;_date.MaxLength=10;_time.MaxLength=5;
        AutomationProperties.SetName(_date,"Fecha del lote");AutomationProperties.SetName(_time,"Hora fija del lote");
        _search.Header=null;_search.PlaceholderText="Buscar JOB o prueba · también admite varios JOBs";AutomationProperties.SetName(_search,"Buscar JOB para lote de hora fija");
        AutomationProperties.SetName(_results,"Pruebas pendientes para agregar al lote");AutomationProperties.SetName(_selection,"Pruebas del lote de hora fija");
        var template=(DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Grid Padding="0,6" ColumnSpacing="8">
                <Grid.ColumnDefinitions><ColumnDefinition Width="132"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                <TextBlock Text="{Binding Execution.Job}" FontSize="12" TextTrimming="CharacterEllipsis"/>
                <StackPanel Grid.Column="1" Spacing="2">
                  <TextBlock Text="{Binding Execution.PlanningTest}" FontSize="13" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/>
                  <TextBlock Text="{Binding Execution.Laboratory}" FontSize="11" Opacity="0.7" TextTrimming="CharacterEllipsis"/>
                </StackPanel>
              </Grid>
            </DataTemplate>
            """);
        _results.ItemTemplate=_selection.ItemTemplate=template;_selection.ItemsSource=_selected;
        _selection.ContainerContentChanging+=(_,e)=>{if(!e.InRecycleQueue&&e.Item is ReportListItem item)ToolTipService.SetToolTip(e.ItemContainer,"Prueba de origen: "+item.Execution.OriginalTest);};
        _results.ContainerContentChanging+=(_,e)=>{if(e.InRecycleQueue)return;if(e.Item is ReportListItem item){e.ItemContainer.IsEnabled=Eligible(item);ToolTipService.SetToolTip(e.ItemContainer,Eligible(item)?item.Execution.OriginalTest:"Esta prueba se marca con el lector o requiere resolver su estado antes de digitar.");}};
        _results.SelectionChanged+=(_,_)=>
        {
            if(_restoring||_saving)return;
            foreach(var item in _results.Items.OfType<ReportListItem>())
            {
                var id=item.Execution.ExecutionId!.Value;
                if(_results.SelectedItems.Contains(item)&&Eligible(item)){if(_chosen.Count<500||_chosen.ContainsKey(id))_chosen.TryAdd(id,item);else ShowError("El lote admite hasta 500 ejecuciones. Guarda este lote antes de agregar más.");}
                else _chosen.Remove(id);
            }
            RefreshSelection();RestoreResults();
        };
        var remove=PlanningUi.Button("Quitar del lote",()=>{if(!_saving&&_selection.SelectedItem is ReportListItem item){_chosen.Remove(item.Execution.ExecutionId!.Value);RefreshSelection();RestoreResults();}return Task.CompletedTask;});
        _previous=PlanningUi.Button("Anterior",()=>Search(_page-1),enabledAfter:()=>!_saving&&_page>1);_next=PlanningUi.Button("Siguiente",()=>Search(_page+1),enabledAfter:()=>!_saving&&_page*100<_total);
        _search.TextChanged+=(_,_)=>{_delay.Stop();_query?.Cancel();_delay.Start();};
        _search.AddHandler(KeyDownEvent,new KeyEventHandler(async(_,e)=>{if(e.Key==VirtualKey.Enter){e.Handled=true;_delay.Stop();await Search(1);}}),true);
        _delay.Tick+=async(_,_)=>{_delay.Stop();await Search(1);};
        _override.Visibility=administrator?Visibility.Visible:Visibility.Collapsed;
        _override.Click+=(_,_)=>{foreach(var id in _chosen.Where(p=>!Eligible(p.Value)).Select(p=>p.Key).ToArray())_chosen.Remove(id);RefreshSelection();_ = Search(1);};
        var left=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star),GridLength.Auto);PlanningUi.Place(left,_search,0);PlanningUi.Place(left,_results,1);PlanningUi.Place(left,PlanningUi.Split(_found,PlanningUi.Row(_previous,_next)),2);
        var right=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star),GridLength.Auto);PlanningUi.Place(right,PlanningUi.Text("Pruebas que se guardarán",14),0);PlanningUi.Place(right,_selection,1);PlanningUi.Place(right,PlanningUi.Split(_count,remove),2);
        var columns=new Grid{ColumnSpacing=16};columns.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});columns.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        columns.Children.Add(left);Grid.SetColumn(right,1);columns.Children.Add(right);
        var help=PlanningUi.Text("Agrega pruebas de distintos JOBs y búsquedas. Revisa el lote antes de confirmar: todas se guardarán con esta fecha y hora. Un JOB pasa a Cierres solo cuando todas sus pruebas requeridas estén digitadas.",12,true);
        var fields=new Grid{ColumnSpacing=12};fields.ColumnDefinitions.Add(new(){Width=GridLength.Auto});fields.ColumnDefinitions.Add(new(){Width=GridLength.Auto});fields.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        fields.Children.Add(_date);Grid.SetColumn(_time,1);fields.Children.Add(_time);Grid.SetColumn(_reason,2);fields.Children.Add(_reason);
        var layout=PlanningUi.Rows(GridLength.Auto,GridLength.Auto,new(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto);layout.RowSpacing=10;
        layout.Width=Math.Max(600,Math.Min(950,root.Size.Width-96));layout.Height=Math.Max(310,Math.Min(540,root.Size.Height-180));
        PlanningUi.Place(layout,fields,0);PlanningUi.Place(layout,help,1);PlanningUi.Place(layout,columns,2);PlanningUi.Place(layout,_override,3);PlanningUi.Place(layout,_message,4);
        _form.Content=layout;var host=new Grid();host.Children.Add(_form);host.Children.Add(_noticeOverlay);Content=host;
        foreach(var item in initial.Where(Eligible).Take(500))_chosen.TryAdd(item.Execution.ExecutionId!.Value,item);RefreshSelection();UpdatePager();
        PrimaryButtonClick+=Save;
        CloseButtonClick+=(_,e)=>{if(_saving||_scannerNotice is not null)e.Cancel=true;else _closeRequested=true;};
        Closing+=(_,e)=>
        {
            if(_closed)return;
            if(_scannerNotice is not null){e.Cancel=true;DismissScannerMessage();return;}
            // Search errors and scanner terminators must never discard an unfinished batch.
            if(_saving||!_closeRequested)e.Cancel=true;
        };
        Closed+=(_,_)=>Dispose();
    }
    private bool Eligible(ReportListItem item)=>ReportListTableModel.CanMark(item)&&(item.CheckMode=="manual"||(_administrator&&_override.IsChecked==true));
    private void RefreshSelection()
    {
        var ordered=_chosen.Values.OrderBy(x=>x.Execution.Job,StringComparer.OrdinalIgnoreCase).ThenBy(x=>x.Execution.PlanningTest,StringComparer.OrdinalIgnoreCase).ThenBy(x=>x.Execution.ExecutionId).ToArray();
        _selected.Clear();foreach(var item in ordered)_selected.Add(item);_count.Text=$"{_selected.Count:N0} pruebas · {_selected.Select(i=>i.Execution.Job).Distinct(StringComparer.OrdinalIgnoreCase).Count():N0} JOBs";IsPrimaryButtonEnabled=_selected.Count>0&&!_saving&&_scannerNotice is null;
    }
    private void RestoreResults(){_restoring=true;try{_results.SelectedItems.Clear();foreach(var item in _results.Items.OfType<ReportListItem>())if(_chosen.ContainsKey(item.Execution.ExecutionId!.Value))_results.SelectedItems.Add(item);}finally{_restoring=false;}}
    private void UpdatePager(){_previous.IsEnabled=!_saving&&_page>1;_next.IsEnabled=!_saving&&_page*100<_total;}
    private async Task Search(int page)
    {
        if(_saving||_closed)return;_query?.Cancel();_query?.Dispose();_query=CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);var ct=_query.Token;
        if(string.IsNullOrWhiteSpace(_search.Text)){_restoring=true;_results.ItemsSource=Array.Empty<ReportListItem>();_restoring=false;_total=0;_page=1;_found.Text="Busca un JOB para elegir sus pruebas pendientes.";UpdatePager();return;}
        _message.IsOpen=false;_message.Visibility=Visibility.Collapsed;_found.Text="Buscando…";_previous.IsEnabled=_next.IsEnabled=false;
        try
        {
            var response=await _api.ReportList(new(Search:PlanningJobList.Parse(_search.Text).Search,View:"Pending",Page:page,PageSize:100),ct);
            if(ct.IsCancellationRequested)return;
            if(!response.IsSuccess||response.Value is not{} data){SearchFailed(response.Error??"No se pudieron consultar las pruebas.");return;}
            _page=page;_total=data.Total;_restoring=true;_results.ItemsSource=data.Items;_restoring=false;RestoreResults();
            _found.Text=data.Total==0?"No hay pruebas pendientes para esta búsqueda. Las pruebas de tu lote se conservan.":$"{data.Total:N0} pendientes · página {page:N0} de {Math.Max(1,(data.Total+99)/100):N0}";UpdatePager();
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){}
        catch(Exception e){if(!ct.IsCancellationRequested)SearchFailed(e.Message);}
    }
    private void SearchFailed(string message)
    {
        _restoring=true;try{_results.ItemsSource=Array.Empty<ReportListItem>();}finally{_restoring=false;}
        _total=0;_page=1;_found.Text="No se pudo completar la búsqueda. Tu lote se conserva.";UpdatePager();ShowError(message);
    }
    // Keep the original form mounted: the notice blocks interaction without replacing the batch.
    internal async Task ShowScannerMessage(string title,string message,bool warning)
    {
        if(_closed||_scannerNotice is not null)return;
        var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);_scannerNotice=completion;
        var accept=PlanningUi.Button("Aceptar",()=>{DismissScannerMessage();return Task.CompletedTask;},true);accept.HorizontalAlignment=HorizontalAlignment.Right;accept.MinWidth=120;
        var notice=PlanningUi.Stack((FrameworkElement)ReportScannerDialog.Heading(title,warning),ReportScannerDialog.Body(message),accept);notice.Spacing=18;
        var card=PlanningUi.Surface(notice);card.MaxWidth=520;card.HorizontalAlignment=HorizontalAlignment.Center;card.VerticalAlignment=VerticalAlignment.Center;card.Padding=new(24);
        _noticeOverlay.Child=card;_noticeOverlay.Visibility=Visibility.Visible;_form.IsEnabled=false;
        IsPrimaryButtonEnabled=false;SetCloseEnabled(false);accept.Focus(FocusState.Programmatic);
        try{await completion.Task;}
        finally
        {
            _noticeOverlay.Visibility=Visibility.Collapsed;_noticeOverlay.Child=null;_form.IsEnabled=true;_scannerNotice=null;
            IsPrimaryButtonEnabled=_selected.Count>0&&!_saving;SetCloseEnabled(!_saving);
            if(!_closed)_search.Focus(FocusState.Programmatic);
        }
    }
    private void DismissScannerMessage()=>_scannerNotice?.TrySetResult(true);
    private void SetCloseEnabled(bool enabled){if(GetTemplateChild("CloseButton") is Button close)close.IsEnabled=enabled;}
    public void Dispose(){if(_closed)return;_closed=true;_scannerNotice?.TrySetResult(true);_delay.Stop();_query?.Cancel();_lifetime.Cancel();_query?.Dispose();_lifetime.Dispose();}
    private void ShowError(string message){_message.Message=message;_message.IsOpen=true;_message.Visibility=Visibility.Visible;}
    private async void Save(ContentDialog sender,ContentDialogButtonClickEventArgs args)
    {
        args.Cancel=true;
        if(_saving||_selected.Count==0)return;
        if(!DateTime.TryParseExact(_date.Text+" "+_time.Text,"yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var local)){ShowError("Use fecha yyyy-MM-dd y hora HH:mm.");return;}
        if(string.IsNullOrWhiteSpace(_reason.Text)){ShowError("Escriba el motivo de la digitación.");return;}
        var at=new DateTimeOffset(DateTime.SpecifyKind(local,DateTimeKind.Unspecified),TimeSpan.FromHours(-6));
        var versions=_selected.Select(i=>new ReportExecutionVersion(i.Execution.ExecutionId!.Value,i.Version)).OrderBy(i=>i.ExecutionId).ToArray();
        if(_retry is null||_retry.OperationalAt!=at||_retry.Reason!=_reason.Text||_retry.AdminOverride!=(_override.IsChecked==true)||!_retry.Executions.SequenceEqual(versions))_retry=new(Guid.NewGuid(),versions,at,_reason.Text,_override.IsChecked==true);
        var deferral=args.GetDeferral();_saving=true;IsPrimaryButtonEnabled=false;IsSecondaryButtonEnabled=false;SetCloseEnabled(false);_search.IsEnabled=_results.IsEnabled=_selection.IsEnabled=_date.IsEnabled=_time.IsEnabled=_reason.IsEnabled=_override.IsEnabled=false;UpdatePager();
        try
        {
            var response=await _api.DigitizeReport(_retry,_lifetime.Token);
            if(!response.IsSuccess||response.Value is null){ShowError(response.Error??"No se confirmó el guardado. Tu lote se conserva; intenta nuevamente.");return;}
            Saved=response.Value;_closeRequested=true;args.Cancel=false;
        }
        catch(OperationCanceledException){ShowError("La operación se interrumpió. Revise la lista antes de reintentar.");}
        catch(Exception e){ShowError(e.Message);}
        finally{_saving=false;SetCloseEnabled(_scannerNotice is null);_search.IsEnabled=_results.IsEnabled=_selection.IsEnabled=_date.IsEnabled=_time.IsEnabled=_reason.IsEnabled=_override.IsEnabled=true;RefreshSelection();UpdatePager();deferral.Complete();}
    }
}
