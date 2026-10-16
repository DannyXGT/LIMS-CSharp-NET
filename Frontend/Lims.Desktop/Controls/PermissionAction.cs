using Lims.Desktop.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Lims.Desktop.Controls;

/// <summary>A themed permission tile retaining the native checkbox keyboard and TogglePattern.</summary>
public sealed class PermissionAction : CheckBox
{
    private Border? _selection;
    private Border? _hover;
    private TextBlock? _check;
    private TextBlock? _state;
    public static readonly DependencyProperty IsChangedProperty = DependencyProperty.Register(
        nameof(IsChanged), typeof(bool), typeof(PermissionAction), new PropertyMetadata(false, OnChanged));
    public bool IsChanged { get => (bool)GetValue(IsChangedProperty); set => SetValue(IsChangedProperty, value); }
    private static void OnChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((PermissionAction)sender).Refresh(false);
    public PermissionAction()
    {
        ActualThemeChanged += (_, _) => Refresh(false);
        Checked += (_, _) => Refresh(true);
        Unchecked += (_, _) => Refresh(true);
        // Composition owns the effective opacity, including the subtle hover alpha.
        PointerEntered += (_, _) => { if (_hover is not null) PermissionsMotion.Opacity(_hover, 0.06f, 120); };
        PointerExited += (_, _) => { if (_hover is not null) PermissionsMotion.Opacity(_hover, 0, 120); };
    }
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _selection = GetTemplateChild("SelectionSurface") as Border;
        _hover = GetTemplateChild("HoverSurface") as Border;
        _check = GetTemplateChild("CheckMark") as TextBlock;
        _state = GetTemplateChild("SelectionText") as TextBlock;
        if (_hover is not null) Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(_hover).Opacity = 0;
        Refresh(false);
    }
    private void Refresh(bool animate)
    {
        if (_selection is null || _check is null || _state is null) return;
        var selected = IsChecked == true;
        _state.Text = IsChanged ? "Cambio pendiente" : selected ? "Asignado" : "Sin asignar";
        if (animate)
        {
            PermissionsMotion.Opacity(_selection, selected ? 1 : 0, 140);
            PermissionsMotion.Opacity(_check, selected ? 1 : 0, 140);
        }
        else
        {
            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(_selection).Opacity = selected ? 1 : 0;
            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(_check).Opacity = selected ? 1 : 0;
        }
        _state.Foreground = (Brush)Application.Current.Resources[IsChanged ? "LimsWarningBrush" : "LimsTextSecondaryBrush"];
    }
}
