using Lims.DesignSystem.Controls;
using Lims.DesignSystem.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace Lims.Desktop.Planning;

internal sealed class PlanningMultiSearchWindow : Window,IDisposable
{
    private readonly CancellationTokenSource _lifetime=new();
    private bool _disposed;
    public PlanningMultiSearchWindow(PlanningApiClient api,Func<string, Task> search)
    {
        Title="LIMS · Búsqueda múltiple";
        var text=PlanningUi.Field("Lista de JOBs",max:20000);
        text.AcceptsReturn=true;text.TextWrapping=TextWrapping.NoWrap;text.Height=140;
        text.PlaceholderText="Pegue una columna desde Excel con Ctrl+V…";
        AutomationProperties.SetName(text,"Lista de JOBs");ScrollViewer.SetVerticalScrollBarVisibility(text,ScrollBarVisibility.Auto);
        var message=PlanningUi.Text("Hasta 500 JOBs únicos · búsqueda exacta en histórico completo",13,true);
        var count=PlanningUi.Text("0 JOBs introducidos",12,true);
        var found=new ListView{SelectionMode=ListViewSelectionMode.None,MaxHeight=140};
        var missing=new ListView{SelectionMode=ListViewSelectionMode.None,MaxHeight=140};
        var results=PlanningUi.Rows(GridLength.Auto);results.Visibility=Visibility.Collapsed;
        var lists=ConsultationVisuals.Split(PlanningUi.Stack(PlanningUi.Text("Encontrados",14),found),PlanningUi.Stack(PlanningUi.Text("No encontrados",14),missing));
        lists.ColumnDefinitions[1].Width=new(1,GridUnitType.Star);PlanningUi.Place(results,lists,0);
        var progress=new ProgressBar{IsIndeterminate=false,Visibility=Visibility.Collapsed,Height=3};
        var title=PlanningUi.Text("Búsqueda múltiple",22);title.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;
        var layout=PlanningUi.Rows(GridLength.Auto,new(1,GridUnitType.Star),GridLength.Auto);
        Button? clear=null;
        layout.Padding=new Thickness(24);layout.Background=ConsultationVisuals.Navy;
        PlanningUi.Place(layout,ConsultationVisuals.Surface(PlanningUi.Stack(title,PlanningUi.Text("Pegue JOBs desde Excel. Use líneas, comas, punto y coma o tabulaciones; los duplicados de entrada se omiten.",13,true))),0);
        PlanningUi.Place(layout,new ScrollViewer{Content=PlanningUi.Stack(text,results),VerticalScrollBarVisibility=ScrollBarVisibility.Auto},1);
        text.TextChanged+=(_,_)=>{try{var list=PlanningJobList.Parse(text.Text);count.Text=$"{list.Jobs.Count:N0} JOBs únicos · {list.Entered:N0} introducidos · {list.Duplicates:N0} duplicados omitidos";}catch(FormatException e){count.Text=e.Message;}};
        var apply=ConsultationVisuals.Action("Buscar JOBs",LimsIconKind.Search,async()=>
        {
            try
            {
                var list=PlanningJobList.Parse(text.Text);text.IsEnabled=false;if(clear is not null)clear.IsEnabled=false;progress.Visibility=Visibility.Visible;progress.IsIndeterminate=true;message.Text="Consultando la lista completa en la API…";results.Visibility=Visibility.Collapsed;
                var match=await PlanningMultipleLookup.Find(api,list,_lifetime.Token);
                if(_lifetime.IsCancellationRequested)return;
                found.ItemsSource=match.Found;missing.ItemsSource=match.Missing;results.Visibility=Visibility.Visible;
                message.Text=$"{match.Found.Count:N0} encontrados · {match.Missing.Count:N0} no encontrados · {match.Executions:N0} ejecuciones";
                await search(list.Search);
            }
            catch(Exception e)when(e is FormatException or InvalidOperationException){message.Text=e.Message;}
            catch(OperationCanceledException)when(_lifetime.IsCancellationRequested){}
            finally{text.IsEnabled=true;if(clear is not null)clear.IsEnabled=true;progress.IsIndeterminate=false;progress.Visibility=Visibility.Collapsed;}
        },true);
        clear=ConsultationVisuals.Action("Limpiar lista",LimsIconKind.ClearFilters,()=>{text.Text="";found.ItemsSource=missing.ItemsSource=null;results.Visibility=Visibility.Collapsed;message.Text="Hasta 500 JOBs únicos · búsqueda exacta en servidor";text.Focus(FocusState.Programmatic);return Task.CompletedTask;});
        var cancel=ConsultationVisuals.Action("Cancelar",LimsIconKind.Cancel,()=>{Close();return Task.CompletedTask;});
        PlanningUi.Place(layout,PlanningUi.Stack(progress,count,message,ConsultationVisuals.Split(PlanningUi.Row(clear,cancel),apply)),2);
        Content=layout;ConsultationVisuals.FitWindow(this,820,730);AppWindow.SetIcon("Assets/Branding/LIMS.ico");
        layout.Loaded+=(_,_)=>{text.Focus(FocusState.Programmatic);if(Motion.Enabled)Motion.Enter(layout,y:4,milliseconds:120);};
        Closed+=(_,_)=>Dispose();
    }
    public void Dispose(){if(_disposed)return;_disposed=true;_lifetime.Cancel();_lifetime.Dispose();}
}
