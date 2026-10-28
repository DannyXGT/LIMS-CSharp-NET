namespace Lims.Contracts.ReferencePreparations;

public static class HistoricalEntryPolicy
{
    // Temporary pause for Stock, Intermedia, Curva, AQS and commercial preparations.
    // Set to true and rebuild/deploy BOTH API and desktop to restore date comparisons.
    // To restore database date constraints, also apply the reviewed rollback migration.
    // Dates are still recorded; required fields, lifecycle, balances and audit timestamps remain enforced.
    public static bool EnforcePreparationDates => false;

    public static bool EnforceCurrentExpiration => EnforcePreparationDates;

    // Temporarily allow any Stock preparation/expiration dates during historical entry.
    // Set to true and rebuild/deploy BOTH API and desktop to restore all Stock date comparisons.
    public static bool EnforceStockPreparationDates => EnforcePreparationDates;
}
