using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Lims.Desktop.VisualHarness;

internal static class ReportScannerMessageProbe
{
    internal static DependencyObject? Find(Window window)=>
        VisualTreeHelper.GetOpenPopupsForXamlRoot(window.Content.XamlRoot).Select(p=>p.Child)
            .FirstOrDefault(p=>Descendants<FrameworkElement>(p).Any(e=>AutomationProperties.GetName(e)=="Mensaje del scanner"));
    internal static async Task<string> AcceptAsync(Window window,string expected,Func<UIElement,Task>? capture=null)
    {
        DependencyObject? popup=null;
        for(var i=0;i<200&&popup is null;i++){await Task.Delay(30);popup=Find(window);}
        if(popup is null)throw new InvalidOperationException("The LIMS scanner modal did not open.");
        await Task.Delay(180);
        var dialog=Descendants<ContentDialog>(popup).FirstOrDefault()??popup as ContentDialog??throw new InvalidOperationException("No styled ContentDialog.");
        var text=string.Join('\n',Descendants<TextBlock>(popup).Select(t=>t.Text));
        if(dialog.RequestedTheme!=ElementTheme.Dark||!text.Contains(expected,StringComparison.Ordinal))throw new InvalidOperationException("Scanner modal theme or message is incorrect: "+text);
        var accept=Descendants<Button>(popup).Single(b=>b.Content as string=="Aceptar");
        if(capture is not null)await capture((UIElement)popup);
        var closed=new TaskCompletionSource<bool>();
        var standalone=AutomationProperties.GetName(dialog)=="Mensaje del scanner";
        void OnClosed(ContentDialog sender,ContentDialogClosedEventArgs e)=>closed.TrySetResult(true);
        if(standalone)dialog.Closed+=OnClosed;
        ((IInvokeProvider)new ButtonAutomationPeer(accept).GetPattern(PatternInterface.Invoke)).Invoke();
        if(standalone){await closed.Task.WaitAsync(TimeSpan.FromSeconds(6));dialog.Closed-=OnClosed;}
        for(var i=0;i<200&&Find(window) is not null;i++)await Task.Delay(30);
        if(Find(window) is not null)throw new InvalidOperationException("Aceptar did not dismiss the scanner notice.");
        await Task.Delay(60);
        return text;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=VisualTreeHelper.GetChild(root,i);if(child is T value)yield return value;
            foreach(var descendant in Descendants<T>(child))yield return descendant;
        }
    }
}
