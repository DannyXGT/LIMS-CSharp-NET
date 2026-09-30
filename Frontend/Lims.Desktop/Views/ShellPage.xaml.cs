using Lims.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Views;

public sealed partial class ShellPage : Page
{
    private readonly ReferenceMaterialsPage _referenceMaterialsPage;

    public ShellPage(ShellViewModel viewModel, ReferenceMaterialsPage referenceMaterialsPage)
    {
        ViewModel = viewModel;
        _referenceMaterialsPage = referenceMaterialsPage;
        InitializeComponent();
        StandardsHost.Content = referenceMaterialsPage;
        MainNavigation.SelectedItem = HomeItem;
    }

    public ShellViewModel ViewModel { get; }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag)
        {
            return;
        }

        HideAllSections();
        switch (tag)
        {
            case "home": Show(HomePanel, "Inicio", "Panel principal"); break;
            case "standards":
                Show(StandardsHost, "Estándares", "Materiales de Referencia");
                _ = _referenceMaterialsPage.LoadAsync();
                break;
        }
    }

    private void HideAllSections()
    {
        HomePanel.Visibility = Visibility.Collapsed;
        StandardsHost.Visibility = Visibility.Collapsed;
    }

    private void Show(FrameworkElement element, string title, string subtitle)
    {
        element.Visibility = Visibility.Visible;
        SectionTitle.Text = title;
        SectionSubtitle.Text = subtitle;
    }

}
