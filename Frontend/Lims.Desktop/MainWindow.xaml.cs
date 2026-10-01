using Lims.Desktop.Services;
using Lims.Desktop.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Lims.Desktop;

public sealed partial class MainWindow : Window
{
    private const int InitialWidth = 1180;
    private const int InitialHeight = 720;
    private const int MinimumWidth = 920;
    private const int MinimumHeight = 560;

    public MainWindow(
        INavigationService navigation,
        LoginPage loginPage,
        ShellPage shellPage)
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/Branding/LIMS.ico");
        ConfigureTitleBar();
        ConfigureWindow();
        navigation.Initialize(RootContent, loginPage, shellPage);
        navigation.ShowLogin();
    }

    private void ConfigureTitleBar()
    {
        var background = Windows.UI.Color.FromArgb(255, 10, 16, 23);
        var foreground = Windows.UI.Color.FromArgb(255, 245, 247, 250);
        var hover = Windows.UI.Color.FromArgb(255, 24, 38, 53);
        var pressed = Windows.UI.Color.FromArgb(255, 32, 50, 70);

        AppWindow.TitleBar.BackgroundColor = background;
        AppWindow.TitleBar.InactiveBackgroundColor = background;
        AppWindow.TitleBar.ButtonBackgroundColor = background;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = background;
        AppWindow.TitleBar.ButtonForegroundColor = foreground;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 177, 189, 202);
        AppWindow.TitleBar.ButtonHoverBackgroundColor = hover;
        AppWindow.TitleBar.ButtonHoverForegroundColor = foreground;
        AppWindow.TitleBar.ButtonPressedBackgroundColor = pressed;
        AppWindow.TitleBar.ButtonPressedForegroundColor = foreground;
    }

    private void ConfigureWindow()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = MinimumWidth;
            presenter.PreferredMinimumHeight = MinimumHeight;
        }

        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var width = Math.Min(InitialWidth, workArea.Width);
        var height = Math.Min(InitialHeight, workArea.Height);
        var x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
        var y = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }
}
