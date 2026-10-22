using Lims.DesignSystem.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation;

namespace Lims.Desktop.Views;

/// <summary>The common heading and useful-area action for preparation dialogs.</summary>
public sealed class PreparationEditorHeader : UserControl
{
    internal static Windows.Foundation.Size SizeFor(XamlRoot root, bool expanded) => new(
        Math.Min(expanded ? 1700 : 1120, Math.Max(1, root.Size.Width - (expanded ? 80 : 120))),
        Math.Min(expanded ? 1200 : 760, Math.Max(1, root.Size.Height - (expanded ? 80 : 96))));
    private readonly TextBlock _title = new() { FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _subtitle = new() { Style = (Style)Application.Current.Resources["LimsCaptionStyle"], TextWrapping = TextWrapping.Wrap };
    private readonly Button _expand = new() { Style = (Style)Application.Current.Resources["LimsActionIconStyle"], VerticalAlignment = VerticalAlignment.Top };
    public string Heading { get => _title.Text; set => _title.Text = value; }
    public string Description { get => _subtitle.Text; set => _subtitle.Text = value; }
    public event EventHandler? ExpandRequested;
    public bool IsExpanded
    {
        set
        {
            var label = value ? "Restaurar" : "Ampliar";
            LimsAction.SetIcon(_expand, value ? LimsIconKind.Restore : LimsIconKind.Maximize);
            ToolTipService.SetToolTip(_expand, label);
            AutomationProperties.SetName(_expand, label);
        }
    }
    public PreparationEditorHeader()
    {
        IsExpanded = false;
        AutomationProperties.SetAutomationId(_expand, "PreparationExpand");
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new());
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var icon = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(10), Background = (Brush)Application.Current.Resources["LimsAccentSoftBrush"],
            Child = new LimsIcon { Icon = LimsIconKind.Preparations, Size = 22, Foreground = (Brush)Application.Current.Resources["LimsAccentBrush"] } };
        var labels = new StackPanel { Spacing = 3 }; labels.Children.Add(_title); labels.Children.Add(_subtitle);
        Grid.SetColumn(labels, 1); Grid.SetColumn(_expand, 2);
        grid.Children.Add(icon); grid.Children.Add(labels); grid.Children.Add(_expand);
        _expand.Click += (_, _) => ExpandRequested?.Invoke(this, EventArgs.Empty);
        Content = grid;
    }
}
