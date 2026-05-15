using FuelRecon.Application.Reconciliation;
using FuelRecon.Domain;

namespace FuelRecon.Application.BranchReports;

public interface IBranchReportService
{
    /// <summary>
    /// Builds a branch-scoped report view from a completed reconciliation engine result.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is null.</exception>
    /// <exception cref="ArgumentException">No <see cref="BranchSummary"/> exists for <paramref name="branchId"/>.</exception>
    BranchReportReadModel Build(ReconciliationEngineResult result, CanonicalBranchId branchId);
}

public sealed class BranchReportService : IBranchReportService
{
    public BranchReportReadModel Build(ReconciliationEngineResult result, CanonicalBranchId branchId)
    {
        ArgumentNullException.ThrowIfNull(result);

        var summary = result.BranchSummaries.SingleOrDefault(s => s.BranchId.Value == branchId.Value)
            ?? throw new ArgumentException($"No branch summary exists for branch '{branchId.Value}'.", nameof(branchId));

        if (summary.RunId != result.Run.Id || summary.Period != result.Run.Period)
        {
            throw new ArgumentException("Branch summary run id or period does not match reconciliation run.", nameof(result));
        }

        var branchItems = result.Items
            .Where(item => item.BranchId?.Value == branchId.Value)
            .OrderBy(item => item.Id.ToString("D"), StringComparer.Ordinal)
            .ToArray();

        var countsByStatus = Enum.GetValues<ReconciliationStatus>()
            .OrderBy(status => (int)status)
            .Select(status => new BranchReportStatusCount(status, branchItems.Count(item => item.SystemStatus == status)))
            .ToArray();

        var matchedItems = Filter(branchItems, item => item.SystemStatus == ReconciliationStatus.Matched);
        var unresolvedItems = Filter(
            branchItems,
            item => item.ResolutionStatus is ResolutionStatus.Unresolved or ResolutionStatus.InReview);

        var exceptionItems = Filter(
            branchItems,
            item => item.SystemStatus is ReconciliationStatus.Variance
                or ReconciliationStatus.DuplicatePossible
                or ReconciliationStatus.ReviewRequired
                or ReconciliationStatus.RegoMismatch);

        var supplierOnlyItems = Filter(branchItems, item => item.SystemStatus == ReconciliationStatus.SupplierOnly);
        var carsOnlyItems = Filter(branchItems, item => item.SystemStatus == ReconciliationStatus.CarsOnly);
        var unbilledItems = Filter(branchItems, item => item.SystemStatus == ReconciliationStatus.Unbilled);
        var varianceItems = Filter(branchItems, item => item.SystemStatus == ReconciliationStatus.Variance);

        var reviewItems = Filter(
            branchItems,
            item => item.SystemStatus is ReconciliationStatus.ReviewRequired
                or ReconciliationStatus.DuplicatePossible);

        return new BranchReportReadModel(
            result.Run.Period,
            result.Run.Id,
            branchId,
            summary,
            countsByStatus,
            matchedItems,
            unresolvedItems,
            exceptionItems,
            supplierOnlyItems,
            carsOnlyItems,
            unbilledItems,
            varianceItems,
            reviewItems);
    }

    private static ReconciliationItem[] Filter(ReconciliationItem[] branchItems, Func<ReconciliationItem, bool> predicate) =>
        branchItems.Where(predicate).ToArray();
}
