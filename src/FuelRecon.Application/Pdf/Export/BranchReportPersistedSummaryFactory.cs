using FuelRecon.Application.Persistence;
using FuelRecon.Domain;

namespace FuelRecon.Application.Pdf.Export;

/// <summary>
/// Builds a <see cref="BranchSummary"/> aligned with persisted branch report totals for reuse by <see cref="BranchReports.IBranchReportService.BuildFromPersisted"/>.
/// </summary>
public static class BranchReportPersistedSummaryFactory
{
    public static BranchSummary Create(BranchReportVersion report, BranchReportPersistedMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(metrics);

        var recoStatus = report.Status switch
        {
            PeriodLifecycleStatus.Reconciled or PeriodLifecycleStatus.Approved or PeriodLifecycleStatus.Closed => ReconciliationStatus.Matched,
            _ => ReconciliationStatus.ReviewRequired,
        };

        return new BranchSummary(
            report.BranchId,
            report.Period,
            report.RunId,
            metrics.SupplierLitres,
            metrics.BranchLitres,
            metrics.BilledLitres,
            metrics.UnbilledLitres,
            metrics.EstimatedRecovery,
            metrics.ReviewCount,
            recoStatus);
    }
}
