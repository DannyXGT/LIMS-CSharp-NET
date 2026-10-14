namespace Lims.Contracts.ReferencePreparations;

public static class HistoricalEntryPolicy
{
    // Temporarily disabled for manual entry of historical Excel records.
    // Set to true and rebuild BOTH API and desktop to restore current-expiration checks.
    // Recorded dates, lifecycle status, balances and chronology remain unchanged.
    public static bool EnforceCurrentExpiration => false;
}
