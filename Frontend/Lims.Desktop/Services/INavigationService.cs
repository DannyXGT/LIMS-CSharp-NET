using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Services;

public interface INavigationService
{
    void Initialize(ContentControl host, FrameworkElement loginView, FrameworkElement shellView);

    void ShowLogin();

    void ShowAuthenticatedShell();
}
