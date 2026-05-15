using FuelRecon.Domain;

namespace FuelRecon.Application.BranchReports;

/// <summary>
/// Application-layer read model for a branch fuel reconciliation report slice (no PDF/UI).
/// </summary>
public sealed record BranchReportReadModel(
    FuelPeriod Period,
    Guid RunId,
    CanonicalBranchId BranchId,
    BranchSummary BranchSummary,
    IReadOnlyList<BranchReportStatusCount> CountsBySystemStatus,
    IReadOnlyList<ReconciliationItem> MatchedItems,
    IReadOnlyList<ReconciliationItem> UnresolvedItems,
    IReadOnlyList<ReconciliationItem> ExceptionItems,
    IReadOnlyList<ReconciliationItem> SupplierOnlyItems,
    IReadOnlyList<ReconciliationItem> CarsOnlyItems,
    IReadOnlyList<ReconciliationItem> UnbilledItems,
    IReadOnlyList<ReconciliationItem> VarianceItems,
    IReadOnlyList<ReconciliationItem> ReviewItems);

/// <summary>
/// Deterministic status tally for items scoped to a branch (<see cref="ReconciliationItem.BranchId"/>).
/// </summary>
public sealed record BranchReportStatusCount(ReconciliationStatus Status, int Count);
