using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Services;

public sealed class NavigationService : INavigationService
{
    private ContentControl? _host;
    private FrameworkElement? _loginView;
    private FrameworkElement? _shellView;

    public void Initialize(ContentControl host, FrameworkElement loginView, FrameworkElement shellView)
    {
        _host = host;
        _loginView = loginView;
        _shellView = shellView;
    }

    public void ShowLogin() => Navigate(_loginView);

    public void ShowAuthenticatedShell() => Navigate(_shellView);

    private void Navigate(FrameworkElement? destination)
    {
        if (_host is null || destination is null)
        {
            throw new InvalidOperationException("Navigation has not been initialized.");
        }

        _host.Content = destination;
    }
}
