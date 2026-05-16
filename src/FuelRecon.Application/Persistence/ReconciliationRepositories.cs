using FuelRecon.Domain;

namespace FuelRecon.Application.Persistence;

public interface IReconciliationRunRepository
{
    void Save(ReconciliationRun run);

    ReconciliationRun? GetById(Guid id);

    ReconciliationRun? GetLatestForPeriod(FuelPeriod period);
}

public interface IReconciliationItemRepository
{
    void Save(ReconciliationItem item);

    void SaveMany(IEnumerable<ReconciliationItem> items);

    ReconciliationItem? GetById(Guid id);

    IReadOnlyList<ReconciliationItem> ListByRun(Guid runId);
}

public sealed record BranchReportPersistedMetrics(
    int ReviewCount,
    PeriodLifecycleStatus LifecycleStatus,
    Litres SupplierLitres,
    Litres BranchLitres,
    Litres BilledLitres,
    Litres UnbilledLitres,
    MoneyAmount EstimatedRecovery);

public interface IBranchReportRepository
{
    void Save(BranchReportVersion report, BranchSummary? summary = null);

    BranchReportVersion? GetById(Guid id);

    IReadOnlyList<BranchReportVersion> ListByRunAndBranch(Guid runId, CanonicalBranchId branchId);

    /// <summary>
    /// Reads persisted totals captured when the branch report version row was inserted (SQLite BranchReports).
    /// </summary>
    BranchReportPersistedMetrics? GetPersistedMetrics(Guid branchReportVersionId);
}

public interface IBranchReportNoteRepository
{
    void Save(BranchReportNote note);

    IReadOnlyList<BranchReportNote> ListByBranchReport(Guid branchReportVersionId);
}

public interface IBranchReportApprovalRepository
{
    void Save(BranchReportApprovalRecord approval);

    BranchReportApprovalRecord? FindByBranchReport(Guid branchReportVersionId);
}

public interface IPdfExportRepository
{
    void Save(PdfExportRecord exportRecord);

    PdfExportRecord? GetById(Guid id);

    IReadOnlyList<PdfExportRecord> ListByBranchReport(Guid branchReportId);
}

public interface IAuditRepository
{
    void Save(AuditRecord auditRecord);

    AuditRecord? GetById(Guid id);

    IReadOnlyList<AuditRecord> ListByEntity(AuditEntityType entityType, string entityId);
}

public interface IPeriodLifecycleRepository
{
    PeriodLifecycleRecord? Get(FuelPeriod period);

    /// <summary>
    /// Ensures a period row exists with lifecycle Draft when missing (matches import bootstrap semantics).
    /// </summary>
    void EnsurePeriod(FuelPeriod period, DateTimeOffset createdAtUtc);

    /// <summary>
    /// Updates mutable lifecycle columns for an existing period row.
    /// </summary>
    void Save(PeriodLifecycleRecord record);
}

public interface ISettingsSnapshotRepository
{
    void Save(SettingsSnapshotRecord snapshot);

    SettingsSnapshotRecord? GetById(string id);
}
