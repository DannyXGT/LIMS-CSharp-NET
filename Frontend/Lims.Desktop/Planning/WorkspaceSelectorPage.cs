using Lims.Desktop.Services;
using Lims.DesignSystem.Controls;
using Lims.DesignSystem.Presentation;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Planning;

public sealed class WorkspaceSelectorPage : Page, IDisposable
{
    private readonly Grid _choices = new() { ColumnSpacing = 16, RowSpacing = 16 };
    private readonly TextBlock _message = PlanningUi.Text("Verificando espacios autorizados…", 12, true);
    private readonly TextBlock _account = PlanningUi.Text("", 12, true);
    private readonly ProgressRing _progress = new() { Width = 16, Height = 16, IsActive = true };
    private readonly Button _retry;
    private readonly StackPanel _body;
    private readonly Grid _accountRow;
    private readonly Grid _footer;
    private readonly StackPanel _actions;
    private readonly TextBlock _heading = PlanningUi.Text("Elige tu espacio de trabajo", 30);
    private readonly PlanningApiClient _api;
    private readonly Action<string> _open;
    private CancellationTokenSource? _load;

    public WorkspaceSelectorPage(PlanningApiClient api, ISessionService session, Action<string> open, Func<Task> logout)
    {
        _api = api;
        _open = open;
        Background = PlanningUi.Brush("LimsBackgroundBrush");
        for (var i = 0; i < 2; i++) _choices.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (var i = 0; i < 4; i++) _choices.RowDefinitions.Add(new() { Height = GridLength.Auto });

        var eyebrow = PlanningUi.Text("ESPACIOS DE TRABAJO", 11);
        eyebrow.Foreground = PlanningUi.Brush("LimsAccentBrush");
        eyebrow.FontWeight = FontWeights.SemiBold;
        eyebrow.CharacterSpacing = 120;
        _account.MaxWidth = 250;
        _account.TextWrapping = TextWrapping.NoWrap;
        _account.TextTrimming = TextTrimming.CharacterEllipsis;
        var identity = PlanningUi.Row(new LimsIcon { Icon = LimsIconKind.User, Size = 16 }, _account);
        identity.Spacing = 8;
        _accountRow = Split(eyebrow, identity);

        _heading.FontWeight = FontWeights.SemiBold;
        AutomationProperties.SetHeadingLevel(_heading, AutomationHeadingLevel.Level1);
        var introduction = PlanningUi.Stack(_heading, PlanningUi.Text("Selecciona el área en la que quieres continuar.", 14, true));
        introduction.Spacing = 8;
        introduction.Margin = new Thickness(0, 0, 0, 4);

        _message.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetLiveSetting(_message, AutomationLiveSetting.Polite);
        var status = new Grid { ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
        status.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        status.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        status.Children.Add(_progress);
        Grid.SetColumn(_message, 1);
        status.Children.Add(_message);
        _retry = Utility("Volver a verificar", LimsIconKind.Refresh, Load);
        _actions = PlanningUi.Row(_retry, Utility("Cerrar sesión", LimsIconKind.SignOut, logout));
        _actions.Spacing = 4;
        _footer = Split(status, _actions);

        _body = PlanningUi.Stack(_accountRow, introduction, _choices,
            new Border { Height = 1, Background = PlanningUi.Brush("LimsBorderBrush") }, _footer);
        _body.Spacing = 16;
        _body.MaxWidth = 920;
        _body.HorizontalAlignment = HorizontalAlignment.Stretch;
        _body.VerticalAlignment = VerticalAlignment.Center;
        _body.Margin = new Thickness(40, 24, 40, 24);
        Content = new ScrollViewer
        {
            Content = _body,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled
        };
        SizeChanged += (_, _) => ArrangeChoices();
        Loaded += async (_, _) =>
        {
            _account.Text = session.Profile?.Name ?? "Sesión iniciada";
            ToolTipService.SetToolTip(_account, _account.Text);
            await Load();
            if (IsLoaded) Motion.Enter(_body, y: 6, milliseconds: 160);
        };
        Unloaded += (_, _) => CancelLoad();
    }

    private async Task Load()
    {
        CancelLoad();
        var request = new CancellationTokenSource();
        _load = request;
        var ct = request.Token;
        _choices.Children.Clear();
        _retry.IsEnabled = false;
        _progress.IsActive = true;
        _progress.Visibility = Visibility.Visible;
        _message.Text = "Verificando espacios autorizados…";
        try
        {
            var result = await _api.Workspaces(ct);
            if (ct.IsCancellationRequested) return;
            if (!result.IsSuccess || result.Value is not { } access)
            {
                _message.Text = result.Error ?? "No se pudo verificar el acceso. Intenta nuevamente.";
                return;
            }
            if (access.Laboratory) Choice("Laboratorio", "Estándares, preparaciones y gestión de materiales.", "laboratory", LimsIconKind.Standards, "Laboratory");
            if (access.Planning) Choice("Planificación", "Consulta de Jobs, expediente WIP e importaciones.", "planning", LimsIconKind.Scheduling, "Planning");
            if (access.Laboratory) Choice("Laboratorio Analítico", "Módulos de laboratorio disponibles para tu cuenta.", "laboratory", LimsIconKind.Results, "Analytical");
            if (access.Administration) Choice("Usuarios y Administración", "Usuarios, roles y permisos de acceso.", "administration", LimsIconKind.Administration, "Administration");
            ArrangeChoices();
            _message.Text = _choices.Children.Count == 0
                ? "Tu cuenta no tiene espacios habilitados. Contacta al administrador."
                : access.Notice ?? "Accesos según los permisos de tu cuenta.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_load, request))
            {
                _progress.IsActive = false;
                _progress.Visibility = Visibility.Collapsed;
                _retry.IsEnabled = true;
                _load = null;
                request.Dispose();
            }
        }
    }

    private void Choice(string title, string description, string target, LimsIconKind icon, string id)
    {
        var artwork = new Border
        {
            Width = 42, Height = 42, CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Left,
            Background = PlanningUi.Brush("LimsAccentSoftBrush"),
            Child = new LimsIcon { Icon = icon, Size = 23, Foreground = PlanningUi.Brush("LimsAccentBrush") }
        };
        var label = PlanningUi.Text(title, 18);
        label.FontWeight = FontWeights.SemiBold;
        var caption = PlanningUi.Text(description, 13, true);
        caption.FontWeight = FontWeights.Normal;
        var text = PlanningUi.Stack(label, caption);
        text.Spacing = 6;
        var content = new Grid { ColumnSpacing = 14, IsHitTestVisible = false };
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        content.Children.Add(artwork);
        Grid.SetColumn(text, 1);
        content.Children.Add(text);
        var arrow = new LimsIcon { Icon = LimsIconKind.ChevronRight, Size = 16 };
        Grid.SetColumn(arrow, 2);
        content.Children.Add(arrow);

        // The native Button template gives the whole card hover, keyboard and focus states.
        var button = new Button
        {
            Content = content, Tag = target,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            MinHeight = 132, Padding = new Thickness(20), CornerRadius = new CornerRadius(12),
            Background = PlanningUi.Brush("LimsSurfaceBrush"),
            BorderBrush = PlanningUi.Brush("LimsBorderBrush"), BorderThickness = new Thickness(1),
            FocusVisualPrimaryBrush = PlanningUi.Brush("LimsFocusBrush"),
            FocusVisualSecondaryBrush = PlanningUi.Brush("LimsBackgroundBrush"),
            UseSystemFocusVisuals = true
        };
        button.Resources["ButtonBackgroundPointerOver"] = PlanningUi.Brush("LimsSurfaceSecondaryBrush");
        button.Resources["ButtonBackgroundPressed"] = PlanningUi.Brush("LimsSelectionBrush");
        button.Resources["ButtonBorderBrushPointerOver"] = PlanningUi.Brush("LimsAccentBrush");
        button.Resources["ButtonBorderBrushPressed"] = PlanningUi.Brush("LimsFocusBrush");
        AutomationProperties.SetName(button, title);
        AutomationProperties.SetHelpText(button, description);
        AutomationProperties.SetAutomationId(button, "Workspace" + id);
        if (target == "administration")
            ToolTipService.SetToolTip(button, "Los accesos de Planificación se administran dentro de ese espacio.");
        button.Click += (_, _) => _open(target);
        _choices.Children.Add(button);
    }

    private void ArrangeChoices()
    {
        var compact = ActualWidth < 760;
        _body.Margin = compact ? new Thickness(24) : new Thickness(40, 24, 40, 24);
        _heading.FontSize = compact ? 26 : 30;
        _choices.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        _choices.ColumnSpacing = compact ? 0 : 16;
        var rowCount = Math.Max(1, (int)Math.Ceiling(_choices.Children.Count / (compact ? 1d : 2d)));
        if (_choices.RowDefinitions.Count != rowCount)
        {
            _choices.RowDefinitions.Clear();
            for (var i = 0; i < rowCount; i++) _choices.RowDefinitions.Add(new() { Height = GridLength.Auto });
        }
        for (var i = 0; i < _choices.Children.Count; i++)
        {
            var child = (FrameworkElement)_choices.Children[i];
            Grid.SetColumn(child, compact ? 0 : i % 2);
            Grid.SetRow(child, compact ? i : i / 2);
        }
        Reflow(_accountRow, compact);
        Reflow(_footer, compact);
        _actions.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    }

    private static Grid Split(UIElement left, UIElement right)
    {
        var grid = new Grid { ColumnSpacing = 20, RowSpacing = 12 };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Grid.SetColumn((FrameworkElement)right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    private static void Reflow(Grid grid, bool compact)
    {
        if (compact && grid.RowDefinitions.Count == 1) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        else if (!compact && grid.RowDefinitions.Count == 2) grid.RowDefinitions.RemoveAt(1);
        var right = (FrameworkElement)grid.Children[1];
        Grid.SetColumn(right, compact ? 0 : 1);
        Grid.SetRow(right, compact ? 1 : 0);
        grid.ColumnSpacing = compact ? 0 : 20;
    }

    private static Button Utility(string title, LimsIconKind icon, Func<Task> action)
    {
        var button = PlanningUi.Button(title, action);
        button.Style = (Style)Application.Current.Resources["LimsUtilityActionStyle"];
        LimsAction.SetIcon(button, icon);
        AutomationProperties.SetName(button, title);
        return button;
    }

    private void CancelLoad()
    {
        _load?.Cancel();
        _load?.Dispose();
        _load = null;
    }

    public void Dispose() => CancelLoad();
}
