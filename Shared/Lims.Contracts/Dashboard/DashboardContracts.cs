using System.Text.Json.Serialization;
using Lims.Contracts.ReferenceMaterials;
namespace Lims.Contracts.Dashboard;

public sealed record DashboardResponse(string View, string Name, string Role, string? Department,
    DateTimeOffset AsOf, int? ExpirationWindowDays, IReadOnlyList<string> Permissions,
    DashboardStandards? Standards, DashboardPreparations? Stock, DashboardPreparations? Intermediate,
    IReadOnlyList<DashboardMaterial> Expirations, IReadOnlyList<DashboardMaterial> LowAvailability,
    IReadOnlyList<DashboardActivity> Activity);
public sealed record DashboardStandards(int Registered, int Active, int Critical, int Low, int? ExpiringSoon,
    int Expired, IReadOnlyList<DashboardState> Distribution);
public sealed record DashboardPreparations(int Available, int Critical, int Low, int? ExpiringSoon, int Expired);
public sealed record DashboardState(string Status, int Count)
{
    [JsonIgnore] public string Label => ReferenceMaterialPresentation.StatusLabel(Status);
    [JsonIgnore] public string Tone => ReferenceMaterialPresentation.StatusTone(Status);
}
public sealed record DashboardMaterial(Guid Id, string Kind, string Name, string Lot, string Method,
    DateOnly ExpirationDate, int DaysRemaining, decimal? AvailabilityPercent)
{
    [JsonIgnore] public string ExpirationDisplay => ReferenceMaterialPresentation.Date(ExpirationDate);
    [JsonIgnore] public string DaysDisplay => DaysRemaining < 0 ? $"Hace {-DaysRemaining} días" : DaysRemaining == 0 ? "Hoy" : $"{DaysRemaining} días";
    [JsonIgnore] public string PercentDisplay => AvailabilityPercent is {} p ? $"{ReferenceMaterialPresentation.Number(decimal.Round(p, 2))} %" : "No disponible";
    [JsonIgnore] public double Progress => (double)(AvailabilityPercent ?? 0);
    [JsonIgnore] public string AvailabilityTone => AvailabilityPercent switch { null => "Neutral", < 25 => "Danger", <= 60 => "Warning", _ => "Information" };
}
public sealed record DashboardActivity(string Action, string Material, string User, DateTimeOffset At,
    string Kind, Guid RecordId)
{
    [JsonIgnore] public string DateDisplay => ReferenceMaterialPresentation.LocalTimestamp(At);
}
