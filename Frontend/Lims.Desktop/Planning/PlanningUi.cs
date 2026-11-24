using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;

namespace Lims.Desktop.Planning;

internal static class PlanningUi
{
    public static Grid Split(UIElement left,UIElement right)
    {
        var grid=new Grid{ColumnSpacing=16};grid.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        grid.Children.Add(left);Grid.SetColumn((FrameworkElement)right,1);grid.Children.Add(right);return grid;
    }
    public static TextBlock Text(string value,int size=14,bool muted=false)=>new(){Text=value,FontSize=size,TextWrapping=TextWrapping.Wrap,Foreground=Brush(muted?"LimsTextSecondaryBrush":"LimsTextPrimaryBrush")};
    public static Brush Brush(string name)=>(Brush)Application.Current.Resources[name];
    public static Button Button(string text,Func<Task> action,bool primary=false,Func<bool>? enabledAfter=null)
    {
        var button=new Button { Content=text,VerticalAlignment=VerticalAlignment.Bottom,Style=(Style)Application.Current.Resources[primary?"LimsPrimaryButtonStyle":"LimsStandardsSecondaryButtonStyle"] };
        button.Click+=async(_,_)=>{button.IsEnabled=false;try{await action();}catch(OperationCanceledException){ }finally{button.IsEnabled=enabledAfter?.Invoke()??true;}};
        return button;
    }
    public static TextBox Field(string header,string? value=null,int max=512)=>new(){Header=header,Text=value??"",MaxLength=max,MinWidth=150,Style=(Style)Application.Current.Resources["LimsCompactTextBoxStyle"]};
    public static Border Surface(UIElement child)=>new(){Child=child,Background=Brush("LimsSurfaceBrush"),BorderBrush=Brush("LimsBorderBrush"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Padding=new Thickness(16)};
    public static StackPanel Stack(params UIElement[] children)
    {var stack=new StackPanel{Spacing=12};foreach(var child in children)stack.Children.Add(child);return stack;}
    public static StackPanel Row(params UIElement[] children)
    {var row=Stack(children);row.Orientation=Orientation.Horizontal;return row;}
    public static Grid Rows(params GridLength[] heights)
    {var grid=new Grid{RowSpacing=12};foreach(var height in heights)grid.RowDefinitions.Add(new(){Height=height});return grid;}
    public static void Place(Grid grid,FrameworkElement child,int row){Grid.SetRow(child,row);grid.Children.Add(child);}
    public static async Task<string?> PickFile(XamlRoot root,bool save=false,string suggestedName="Consulta de Jobs")
    {
        var window=WindowFor(root);if(window is null)throw new InvalidOperationException("La ventana no está disponible.");
        var handle=WinRT.Interop.WindowNative.GetWindowHandle(window);
        if(save)
        {
            var picker=new FileSavePicker{SuggestedFileName=suggestedName};picker.FileTypeChoices.Add("CSV UTF-8",[".csv"]);WinRT.Interop.InitializeWithWindow.Initialize(picker,handle);
            return (await picker.PickSaveFileAsync())?.Path;
        }
        else
        {
            var picker=new FileOpenPicker();picker.FileTypeFilter.Add(".xlsx");WinRT.Interop.InitializeWithWindow.Initialize(picker,handle);
            return (await picker.PickSingleFileAsync())?.Path;
        }
    }
    private static readonly List<Window> Windows=[];
    public static void Track(Window window){Windows.Add(window);window.Closed+=(_,_)=>Windows.Remove(window);}
    private static Window? WindowFor(XamlRoot root)=>Windows.FirstOrDefault(w=>w.Content?.XamlRoot==root);
    public static async Task<bool> Confirm(XamlRoot root,string title,string message)
    {
        var dialog=new ContentDialog{XamlRoot=root,Title=title,Content=Text(message),PrimaryButtonText="Confirmar",CloseButtonText="Cancelar",DefaultButton=ContentDialogButton.Close};
        return await dialog.ShowAsync()==ContentDialogResult.Primary;
    }
    public static string? Optional(string? value)=>string.IsNullOrWhiteSpace(value)?null:value.Trim();
}
