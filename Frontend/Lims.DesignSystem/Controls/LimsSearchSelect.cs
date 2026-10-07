namespace Lims.DesignSystem.Controls;

/// <summary>Searches only ItemsSource, without dependencies on API clients.</summary>
public sealed class LimsSearchSelect : LimsSelect
{
    protected override bool SearchEnabled => true;
}
