using Lims.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Lims.DesignSystem.Presentation;

namespace Lims.Desktop.Views;

public sealed partial class ShellPage : Page
{
    private readonly ReferenceMaterialsPage _referenceMaterialsPage;
    private readonly StockPage _stockPage;

    public ShellPage(ShellViewModel viewModel, ReferenceMaterialsPage referenceMaterialsPage, StockPage stockPage)
    {
        ViewModel = viewModel;
        _referenceMaterialsPage = referenceMaterialsPage;
        _stockPage = stockPage;
        InitializeComponent();
        StandardsHost.Content = referenceMaterialsPage;
        PreparationsHost.Content = stockPage;
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
        StandardsNewButton.Visibility = Visibility.Collapsed;
        switch (tag)
        {
            case "home":
                StandardsNewButton.Visibility = Visibility.Collapsed;
                Show(HomePanel, "Inicio", "Panel principal");
                break;
            case "standards":
                Show(StandardsHost, "Estándares", "Materiales de Referencia");
                StandardsNewButton.IsEnabled = _referenceMaterialsPage.ViewModel.CanCreate;
                StandardsNewButton.Visibility = Visibility.Visible;
                _ = _referenceMaterialsPage.LoadAsync();
                break;
            case "preparations":
                Show(PreparationsHost, "Preparaciones", "Materiales de Referencia");
                _ = _stockPage.LoadAsync();
                break;
        }
    }

    private void HideAllSections()
    {
        Lims.DesignSystem.Controls.LimsPopupField.CloseForRoot(XamlRoot);
        Motion.ConfigureVisibility(HomePanel);
        Motion.ConfigureVisibility(StandardsHost);
        Motion.ConfigureVisibility(PreparationsHost);
        HomePanel.Visibility = Visibility.Collapsed;
        StandardsHost.Visibility = Visibility.Collapsed;
        PreparationsHost.Visibility = Visibility.Collapsed;
    }

    private void Show(FrameworkElement element, string title, string subtitle)
    {
        element.Visibility = Visibility.Visible;
        SectionTitle.Text = title;
        SectionSubtitle.Text = subtitle;
        Motion.Enter(element, y: 5, milliseconds: 150);
    }

    private void OnNavigationLoaded(object sender, RoutedEventArgs e)
    {
        Motion.ConfigureVisibility(HomePanel);
        Motion.ConfigureVisibility(StandardsHost);
        UpdatePane(MainNavigation.IsPaneOpen);
    }
    private void OnPaneOpening(NavigationView sender, object args) => UpdatePane(true);
    private void OnPaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args) => UpdatePane(false);

    private void UpdatePane(bool expanded)
    {
        var visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        BrandLabels.Visibility = visibility;
        MaterialsGroup.Visibility = visibility;
        UserLabels.Visibility = visibility;
        LogoutButton.Visibility = visibility;
        if (expanded)
        {
            Motion.Enter(BrandLabels, x: -3, milliseconds: 180);
            Motion.Enter(UserLabels, x: -3, milliseconds: 180);
        }
    }

    private async void OnNewStandardClick(object sender, RoutedEventArgs e) =>
        await _referenceMaterialsPage.OpenCreateDialogAsync();

}
