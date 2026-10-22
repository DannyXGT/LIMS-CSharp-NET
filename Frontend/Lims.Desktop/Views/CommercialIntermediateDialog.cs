using Lims.DesignSystem.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Views;

/// <summary>Hosts the existing editor in WinUI's modal layer, preserving the list behind it.</summary>
public sealed class CommercialIntermediateDialog : ContentDialog
{
    private readonly CommercialIntermediateWorkspace _editor;
    private bool _expanded;
    public CommercialIntermediateDialog(CommercialIntermediateWorkspace editor)
    {
        _editor = editor;
        Style = (Style)Application.Current.Resources["LimsEditorDialogStyle"];
        Resources["ContentDialogMaxWidth"] = 1800d;
        Resources["ContentDialogMaxHeight"] = 1400d;
        Content = editor;
        editor.ExpandRequested += (_, _) => { _expanded = !_expanded; Resize(); };
        Opened += (_, _) => { XamlRoot.Changed += OnRootChanged; Resize(); };
        Closing += (_, args) => { args.Cancel = editor.ViewModel.IsBusy; if (!args.Cancel) LimsPopupField.CloseForRoot(XamlRoot); };
        Closed += (_, _) => { XamlRoot.Changed -= OnRootChanged; editor.Release(); };
    }
    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Resize();
    private void Resize()
    {
        var size = PreparationEditorHeader.SizeFor(XamlRoot, _expanded);
        _editor.Width = size.Width;
        _editor.Height = size.Height;
        _editor.SetExpanded(_expanded);
    }
}
