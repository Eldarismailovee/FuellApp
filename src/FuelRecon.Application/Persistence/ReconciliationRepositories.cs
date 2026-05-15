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

public interface IBranchReportRepository
{
    void Save(BranchReportVersion report, BranchSummary? summary = null);

    BranchReportVersion? GetById(Guid id);

    IReadOnlyList<BranchReportVersion> ListByRunAndBranch(Guid runId, CanonicalBranchId branchId);
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

public interface ISettingsSnapshotRepository
{
    void Save(SettingsSnapshotRecord snapshot);

    SettingsSnapshotRecord? GetById(string id);
}
