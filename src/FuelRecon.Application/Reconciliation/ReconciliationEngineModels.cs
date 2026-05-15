using FuelRecon.Domain;

namespace FuelRecon.Application.Reconciliation;

public sealed record ReconciliationEngineInput(
    FuelPeriod Period,
    IReadOnlyList<SupplierTransaction> SupplierTransactions,
    IReadOnlyList<BranchLitresEntry> BranchLitresEntries,
    IReadOnlyList<CarsBillingEntry> CarsBillingEntries,
    ReconciliationRulesOptions? Rules = null);

public sealed record ReconciliationRulesOptions(
    int DateToleranceDays = 1,
    decimal LitresTolerance = 0.50m,
    decimal AmountTolerance = 0.50m,
    DateTimeOffset? RunCreatedAtUtc = null,
    string CreatedBy = "System")
{
    public static ReconciliationRulesOptions Default { get; } = new();
}

public sealed record ReconciliationEngineResult(
    ReconciliationRun Run,
    IReadOnlyList<ReconciliationItem> Items,
    IReadOnlyList<BranchSummary> BranchSummaries);
