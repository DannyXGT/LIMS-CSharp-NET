namespace Lims.Contracts.ReferencePreparations;

/// <summary>Read-only preparation filters, applied before counting and paging.</summary>
public sealed record PreparationListFilter(string? Status = null, string? Availability = null, string? Method = null)
{
    public bool IsValid => Status is null or "" or "Active" or "Expired" or "Archived" &&
        Availability is null or "" or "Positive" or "Low" or "Depleted" && (Method?.Length ?? 0) <= 200;
    public bool HasCriteria => !string.IsNullOrWhiteSpace(Status) || !string.IsNullOrWhiteSpace(Availability) || !string.IsNullOrWhiteSpace(Method);
}
