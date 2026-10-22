using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lims.Contracts.ReferencePreparations;

namespace Lims.Desktop.ViewModels;

/// <summary>One draft, percentage basis and validation for all preparation editors.</summary>
public sealed class SolventCompositionViewModel : ObservableObject
{
    public ObservableCollection<SolventOption> Catalog { get; } = [];
    public ObservableCollection<StockSolventRow> Rows { get; } = [];
    public bool IsReady { get; private set; }
    public string CatalogMessage { get; private set; } = "Cargando catálogo compartido…";
    public IReadOnlyList<StockSolventInput> Inputs => Rows.Select(r => new StockSolventInput(r.SelectedSolvent?.Id ?? 0, r.TryPercent(out var n) ? n : 0)).ToArray();
    public IReadOnlyList<StockSolventDetail> Details => Rows.Select((r, i) => new StockSolventDetail(r.SelectedSolvent?.Id ?? 0,
        r.SelectedSolvent?.Name ?? "Sin seleccionar", r.SelectedSolvent?.Abbreviation ?? "?", r.TryPercent(out var n) && n is >= 0 and <= 100 ? n : 0, i)).ToArray();
    public string? Error => Rows.Any(r => !r.TryPercent(out _)) ? "Complete las proporciones con hasta cuatro decimales." :
        StockSolventComposition.Validate(Inputs, StockSolventComposition.Basis) ??
        (Rows.Any(r => !Catalog.Any(s => s.Id == r.SelectedSolvent?.Id && s.IsActive)) ? "Seleccione solventes activos del catálogo compartido." : null);
    public bool IsValid => IsReady && Error is null;
    public decimal Total => Details.Sum(r => r.Percent);
    public string TotalDisplay => $"Proporción total: {StockSolventComposition.Percent(Total)} %";
    public string Summary => StockSolventComposition.Summary(Details);
    public SolventCompositionViewModel()
    {
        Rows.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null) foreach (StockSolventRow row in e.OldItems) row.PropertyChanged -= OnRowChanged;
            if (e.NewItems is not null) foreach (StockSolventRow row in e.NewItems) row.PropertyChanged += OnRowChanged;
            Refresh();
        };
    }
    public StockSolventRow Add() { var row = new StockSolventRow(); Rows.Add(row); return row; }
    public void Remove(StockSolventRow row) => Rows.Remove(row);
    public void Loading() { IsReady = false; CatalogMessage = "Cargando catálogo compartido…"; Refresh(); }
    public void Fail(string message) { IsReady = false; CatalogMessage = message; Refresh(); }
    public void SetCatalog(IReadOnlyList<SolventOption>? catalog)
    {
        if (catalog is null) { Fail("El servidor no devolvió el catálogo de solventes. Actualice la API o reintente la consulta."); return; }
        var selections = Rows.Select(r => (Row: r, Id: r.SelectedSolvent?.Id)).ToArray();
        Catalog.Clear(); foreach (var solvent in catalog.Where(s => s.IsActive)) Catalog.Add(solvent);
        foreach (var selection in selections) selection.Row.SelectedSolvent = Catalog.FirstOrDefault(s => s.Id == selection.Id);
        IsReady = Catalog.Count > 0;
        CatalogMessage = IsReady ? "" : "No hay solventes activos en el catálogo persistente. Revise su carga en el servidor.";
        Refresh();
    }
    private void OnRowChanged(object? sender, PropertyChangedEventArgs e) => Refresh();
    private void Refresh()
    {
        foreach (var name in new[] { nameof(IsReady), nameof(CatalogMessage), nameof(Inputs), nameof(Details), nameof(Error), nameof(IsValid), nameof(TotalDisplay), nameof(Summary) }) OnPropertyChanged(name);
    }
}
