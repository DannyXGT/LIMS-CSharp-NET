using CommunityToolkit.Mvvm.ComponentModel;
using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.Validation;

namespace Lims.Desktop.ViewModels;

public sealed partial class StockSolventRow : ObservableObject
{
    [ObservableProperty] public partial SolventOption? SelectedSolvent { get; set; }
    [ObservableProperty] public partial string Proportion { get; set; } = string.Empty;
    public bool TryPercent(out decimal percent) => ReferenceMaterialInput.TryParseDecimal(Proportion, 4, out percent);
}
