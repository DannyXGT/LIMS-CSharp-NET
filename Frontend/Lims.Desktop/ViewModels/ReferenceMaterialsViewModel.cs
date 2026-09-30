using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lims.Contracts.Errors;
using Lims.Contracts.ReferenceMaterials;
using Lims.Desktop.Http;
using Lims.Desktop.Services;

namespace Lims.Desktop.ViewModels;

public sealed partial class ReferenceMaterialsViewModel(
    IReferenceMaterialsApiClient api,
    ISessionService session) : ObservableObject
{
    private const int PageSize = 25;
    private long _selectionVersion;

    public ObservableCollection<ReferenceMaterialSummary> Items { get; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? StatusFilter { get; set; }

    [ObservableProperty]
    public partial string? MethodFilter { get; set; }

    [ObservableProperty]
    public partial ReferenceMaterialSummary? SelectedMaterial { get; set; }

    [ObservableProperty]
    public partial ReferenceMaterialDetail? SelectedDetail { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int TotalCount { get; set; }

    [ObservableProperty]
    public partial int CurrentPage { get; set; } = 1;

    public bool CanView => HasPermission(ReferenceMaterialPermissions.View);
    public bool CanCreate => HasPermission(ReferenceMaterialPermissions.Create);
    public bool CanEdit => HasPermission(ReferenceMaterialPermissions.Edit);
    public bool CanArchive => HasPermission(ReferenceMaterialPermissions.Archive);
    public bool CanReplace => CanCreate && CanArchive;
    public bool CanEditSelected => CanEdit && IsSelectedMutable;
    public bool CanArchiveSelected => CanArchive && IsSelectedMutable;
    public bool CanReplaceSelected => CanReplace && IsSelectedMutable;
    public bool HasItems => Items.Count > 0;
    public bool HasNoItems => Items.Count == 0 && !IsBusy;
    public bool HasSelection => SelectedDetail is not null;
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public bool CanGoToPreviousPage => CurrentPage > 1 && !IsBusy;
    public bool CanGoToNextPage => CurrentPage < TotalPages && !IsBusy;
    public string PaginationText => $"Página {CurrentPage} de {TotalPages} · {TotalCount} registros";

    public void RefreshPermissions()
    {
        OnPropertyChanged(nameof(CanView));
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanArchive));
        OnPropertyChanged(nameof(CanReplace));
        OnPropertyChanged(nameof(CanEditSelected));
        OnPropertyChanged(nameof(CanArchiveSelected));
        OnPropertyChanged(nameof(CanReplaceSelected));
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        RefreshPermissions();
        if (!CanView)
        {
            Items.Clear();
            TotalCount = 0;
            Message = "No tiene permiso para consultar Materiales de Referencia.";
            NotifyCollectionState();
            return;
        }

        IsBusy = true;
        Message = string.Empty;
        try
        {
            var result = await api.ListAsync(
                    SearchText,
                    StatusFilter,
                    MethodFilter,
                    CurrentPage,
                    PageSize,
                    cancellationToken)
                .ConfigureAwait(true);
            if (!result.IsSuccess || result.Value is null)
            {
                Message = MessageFor(result.Error);
                return;
            }

            Items.Clear();
            foreach (var item in result.Value.Items)
            {
                Items.Add(item);
            }

            TotalCount = result.Value.TotalCount;
            CurrentPage = result.Value.Page;
            if (SelectedMaterial is not null && Items.All(item => item.Id != SelectedMaterial.Id))
            {
                SelectedMaterial = null;
                SelectedDetail = null;
            }

            NotifyCollectionState();
            NotifyPaginationState();
        }
        catch (HttpRequestException)
        {
            Message = "No se pudo conectar con el servicio LIMS.";
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Message = "El servicio tardó demasiado en responder.";
        }
        finally
        {
            IsBusy = false;
            NotifyPaginationState();
        }
    }

    public async Task GoToPreviousPageAsync(CancellationToken cancellationToken)
    {
        if (!CanGoToPreviousPage)
        {
            return;
        }

        CurrentPage--;
        await LoadAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task GoToNextPageAsync(CancellationToken cancellationToken)
    {
        if (!CanGoToNextPage)
        {
            return;
        }

        CurrentPage++;
        await LoadAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task ApplyFiltersAsync(CancellationToken cancellationToken)
    {
        CurrentPage = 1;
        await LoadAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task SelectAsync(ReferenceMaterialSummary? material, CancellationToken cancellationToken)
    {
        var selectionVersion = Interlocked.Increment(ref _selectionVersion);
        SelectedMaterial = material;
        SelectedDetail = null;
        if (material is null)
        {
            IsBusy = false;
            return;
        }

        IsBusy = true;
        try
        {
            var result = await api.GetAsync(material.Id, cancellationToken).ConfigureAwait(true);
            if (selectionVersion != Volatile.Read(ref _selectionVersion) || SelectedMaterial?.Id != material.Id)
            {
                return;
            }

            if (result.IsSuccess)
            {
                SelectedDetail = result.Value;
                Message = string.Empty;
            }
            else
            {
                Message = MessageFor(result.Error);
            }
        }
        finally
        {
            if (selectionVersion == Volatile.Read(ref _selectionVersion))
            {
                IsBusy = false;
            }
        }
    }

    public async Task<bool> CreateAsync(
        CreateReferenceMaterialRequest request,
        CancellationToken cancellationToken)
    {
        if (!CanCreate)
        {
            Message = "No tiene permiso para crear estándares.";
            return false;
        }

        var result = await api.CreateAsync(request, cancellationToken).ConfigureAwait(true);
        if (!result.IsSuccess || result.Value is null)
        {
            Message = MessageFor(result.Error);
            return false;
        }

        await LoadAsync(cancellationToken).ConfigureAwait(true);
        var created = Items.FirstOrDefault(item => item.Id == result.Value.Id);
        await SelectAsync(created, cancellationToken).ConfigureAwait(true);
        return true;
    }

    public async Task<bool> UpdateAsync(
        UpdateReferenceMaterialRequest request,
        CancellationToken cancellationToken)
    {
        if (!CanEdit || SelectedDetail is null)
        {
            Message = "No tiene permiso para editar estándares.";
            return false;
        }

        var result = await api.UpdateAsync(SelectedDetail.Id, request, cancellationToken).ConfigureAwait(true);
        if (!result.IsSuccess || result.Value is null)
        {
            Message = MessageFor(result.Error);
            return false;
        }

        await LoadAsync(cancellationToken).ConfigureAwait(true);
        var updated = Items.FirstOrDefault(item => item.Id == result.Value.Id);
        await SelectAsync(updated, cancellationToken).ConfigureAwait(true);
        return true;
    }

    public async Task<bool> ArchiveAsync(string reason, CancellationToken cancellationToken)
    {
        if (!CanArchive || SelectedDetail is null)
        {
            Message = "No tiene permiso para archivar estándares.";
            return false;
        }

        var result = await api.ArchiveAsync(
                SelectedDetail.Id,
                new ArchiveReferenceMaterialRequest(reason, SelectedDetail.Version),
                cancellationToken)
            .ConfigureAwait(true);
        if (!result.IsSuccess)
        {
            Message = MessageFor(result.Error);
            return false;
        }

        await LoadAsync(cancellationToken).ConfigureAwait(true);
        var archived = Items.FirstOrDefault(item => item.Id == SelectedDetail?.Id);
        await SelectAsync(archived, cancellationToken).ConfigureAwait(true);
        return true;
    }

    public async Task<bool> ReplaceAsync(
        CreateReferenceMaterialRequest replacement,
        string reason,
        CancellationToken cancellationToken)
    {
        if (!CanReplace || SelectedDetail is null)
        {
            Message = "No tiene permisos para reemplazar estándares.";
            return false;
        }

        var result = await api.ReplaceAsync(
                SelectedDetail.Id,
                new ReplaceReferenceMaterialRequest(replacement, reason, SelectedDetail.Version),
                cancellationToken)
            .ConfigureAwait(true);
        if (!result.IsSuccess || result.Value is null)
        {
            Message = MessageFor(result.Error);
            return false;
        }

        await LoadAsync(cancellationToken).ConfigureAwait(true);
        var created = Items.FirstOrDefault(item => item.Id == result.Value.Id);
        await SelectAsync(created, cancellationToken).ConfigureAwait(true);
        return true;
    }

    partial void OnSelectedDetailChanged(ReferenceMaterialDetail? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanEditSelected));
        OnPropertyChanged(nameof(CanArchiveSelected));
        OnPropertyChanged(nameof(CanReplaceSelected));
    }

    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    partial void OnCurrentPageChanged(int value) => NotifyPaginationState();

    partial void OnTotalCountChanged(int value) => NotifyPaginationState();

    partial void OnIsBusyChanged(bool value)
    {
        NotifyPaginationState();
        OnPropertyChanged(nameof(HasNoItems));
    }

    private bool HasPermission(string permission) =>
        session.Profile?.Permissions.Contains(permission, StringComparer.Ordinal) == true;

    private bool IsSelectedMutable => SelectedDetail?.Status is
        "Active" or "Expired" or "Depleted" or "Blocked";

    private void NotifyCollectionState()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasNoItems));
    }

    private void NotifyPaginationState()
    {
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(CanGoToPreviousPage));
        OnPropertyChanged(nameof(CanGoToNextPage));
        OnPropertyChanged(nameof(PaginationText));
    }

    private static string MessageFor(ApiError? error)
    {
        if (error?.ValidationErrors is { Count: > 0 })
        {
            return string.Join(" ", error.ValidationErrors.Values.SelectMany(value => value));
        }

        return error?.Code switch
        {
            ErrorCodes.Forbidden => "No tiene autorización para realizar esta operación.",
            ErrorCodes.NotFound => "El estándar solicitado ya no existe.",
            ErrorCodes.Conflict => "El estándar cambió. Actualice la lista e intente nuevamente.",
            ErrorCodes.InvalidState => error.Message,
            _ => error?.Message ?? "No se pudo completar la operación.",
        };
    }
}
