namespace Lims.Contracts.ReferencePreparations;

public static class HistoricalEntryPolicy
{
    // Temporarily disabled for manual entry of historical Excel records.
    // Set to true and rebuild BOTH API and desktop to restore current-expiration checks.
    // Recorded dates, lifecycle status and balances remain unchanged.
    public static bool EnforceCurrentExpiration => false;

    // Temporarily allow any Stock preparation/expiration dates during historical entry.
    // Set to true and rebuild/deploy BOTH API and desktop to restore all Stock date comparisons.
    public static bool EnforceStockPreparationDates => false;
}
