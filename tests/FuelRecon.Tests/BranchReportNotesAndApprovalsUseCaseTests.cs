using System.Text.Json;
using FuelRecon.Application.BranchReports;
using FuelRecon.Application.Persistence;
using FuelRecon.Domain;

namespace FuelRecon.Tests;

public class BranchReportNotesAndApprovalsUseCaseTests
{
    private static readonly FuelPeriod Period = new(2026, 4);
    private static readonly Guid RunId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly CanonicalBranchId Taupo = new("TAUPO");
    private static readonly Guid ReportId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void AddBranchReportNote_throws_when_branch_report_version_is_unknown()
    {
        var useCase = new AddBranchReportNoteUseCase(
            new FakeBranchReportRepository(),
            new FakeBranchReportNoteRepository(),
            new FakeAuditRepository());

        Assert.Throws<InvalidOperationException>(() =>
            useCase.Execute(
                new AddBranchReportNoteRequest(
                    ReportId,
                    "Something happened",
                    "arina",
                    new DateTimeOffset(2026, 5, 15, 10, 0, 0, TimeSpan.Zero))));
    }

    [Fact]
    public void AddBranchReportNote_persists_note_and_audit_without_touching_items()
    {
        var reports = new FakeBranchReportRepository();
        reports.Reports[ReportId] = SampleReport();
        var notes = new FakeBranchReportNoteRepository();
        var audits = new FakeAuditRepository();
        var items = new FakeReconciliationItemRepository();

        var useCase = new AddBranchReportNoteUseCase(reports, notes, audits);

        var response = useCase.Execute(
            new AddBranchReportNoteRequest(
                ReportId,
                "Reviewer comment",
                "arina",
                new DateTimeOffset(2026, 5, 15, 11, 0, 0, TimeSpan.Zero),
                ReasonCode: "Escalation"));

        Assert.Single(notes.Saved);
        Assert.Equal("Reviewer comment", notes.Saved[0].NoteText);
        Assert.Equal("Escalation", notes.Saved[0].ReasonCode);
        Assert.Single(audits.Saved);
        Assert.Equal(AuditActionType.Create, audits.Saved[0].ActionType);
        Assert.Equal(AuditEntityType.BranchReport, audits.Saved[0].EntityType);
        Assert.Equal(ReportId.ToString("D"), audits.Saved[0].EntityId);
        Assert.Equal(BranchReportAuditReasonCodes.NoteAppended, audits.Saved[0].ReasonCode);
        Assert.Empty(items.Saved);
        Assert.Equal(response.Note.Id, notes.Saved[0].Id);
    }

    [Fact]
    public void ListBranchReportNotes_returns_repository_rows()
    {
        var repo = new FakeBranchReportNoteRepository();
        var note = new BranchReportNote(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            ReportId,
            new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero),
            "arina",
            "Listed note");
        repo.ByBranch[ReportId] = [note];

        var useCase = new ListBranchReportNotesUseCase(repo);

        var listed = useCase.Execute(new ListBranchReportNotesRequest(ReportId));

        Assert.Single(listed);
        Assert.Equal(note.Id, listed[0].Id);
    }

    [Fact]
    public void ApproveBranchReportVersion_throws_when_report_unknown()
    {
        var useCase = CreateApproveUseCase(
            new FakeBranchReportRepository(),
            new FakeBranchReportApprovalRepository(),
            new FakeReconciliationItemRepository(),
            new FakeAuditRepository());

        Assert.Throws<InvalidOperationException>(() =>
            useCase.Execute(
                new ApproveBranchReportVersionRequest(
                    ReportId,
                    ApprovedBy: "mgr",
                    ApprovedAtUtc: new DateTimeOffset(2026, 5, 15, 13, 0, 0, TimeSpan.Zero),
                    ApprovalNote: null)));
    }

    [Fact]
    public void ApproveBranchReportVersion_throws_when_persisted_metrics_missing()
    {
        var reports = new FakeBranchReportRepository();
        reports.Reports[ReportId] = SampleReport();

        var useCase = CreateApproveUseCase(
            reports,
            new FakeBranchReportApprovalRepository(),
            new FakeReconciliationItemRepository(),
            new FakeAuditRepository());

        Assert.Throws<InvalidOperationException>(() =>
            useCase.Execute(
                new ApproveBranchReportVersionRequest(
                    ReportId,
                    ApprovedBy: "mgr",
                    ApprovedAtUtc: new DateTimeOffset(2026, 5, 15, 13, 0, 0, TimeSpan.Zero),
                    ApprovalNote: null)));
    }

    [Fact]
    public void ApproveBranchReportVersion_throws_when_already_approved()
    {
        var reports = new FakeBranchReportRepository();
        reports.Reports[ReportId] = SampleReport();
        reports.Metrics[ReportId] = SampleMetrics();

        var approvals = new FakeBranchReportApprovalRepository();
        approvals.ByBranch[ReportId] = new BranchReportApprovalRecord(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            ReportId,
            RunId,
            new DateTimeOffset(2026, 5, 14, 9, 0, 0, TimeSpan.Zero),
            "prior",
            "{}");

        var useCase = CreateApproveUseCase(
            reports,
            approvals,
            new FakeReconciliationItemRepository(),
            new FakeAuditRepository());

        Assert.Throws<InvalidOperationException>(() =>
            useCase.Execute(
                new ApproveBranchReportVersionRequest(
                    ReportId,
                    ApprovedBy: "mgr",
                    ApprovedAtUtc: new DateTimeOffset(2026, 5, 15, 13, 0, 0, TimeSpan.Zero),
                    ApprovalNote: "retry")));
    }

    [Fact]
    public void ApproveBranchReportVersion_requires_note_when_default_policy_and_branch_has_unresolved_items()
    {
        var reports = new FakeBranchReportRepository();
        reports.Reports[ReportId] = SampleReport();
        reports.Metrics[ReportId] = SampleMetrics();

        var items = new FakeReconciliationItemRepository();
        items.ByRun[RunId] = [CreateTaupoItem(resolved: false)];

        var useCase = CreateApproveUseCase(
            reports,
            new FakeBranchReportApprovalRepository(),
            items,
            new FakeAuditRepository());

        var exception = Assert.Throws<ArgumentException>(() =>
            useCase.Execute(
                new ApproveBranchReportVersionRequest(
                    ReportId,
                    ApprovedBy: "mgr",
                    ApprovedAtUtc: new DateTimeOffset(2026, 5, 15, 13, 0, 0, TimeSpan.Zero),
                    ApprovalNote: "   ")));

        Assert.Contains(BranchReportAuditReasonCodes.ApprovalNoteRequiredForUnresolvedItems, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApproveBranchReportVersion_succeeds_with_note_when_unresolved_items_exist_under_default_policy()
    {
        var reports = new FakeBranchReportRepository();
        reports.Reports[ReportId] = SampleReport();
        reports.Metrics[ReportId] = SampleMetrics();

        var items = new FakeReconciliationItemRepository();
        items.ByRun[RunId] = [CreateTaupoItem(resolved: false)];

        var approvals = new FakeBranchReportApprovalRepository();
        var audits = new FakeAuditRepository();

        var useCase = CreateApproveUseCase(reports, approvals, items, audits);

        var response = useCase.Execute(
            new ApproveBranchReportVersionRequest(
                ReportId,
                ApprovedBy: "mgr",
                ApprovedAtUtc: new DateTimeOffset(2026, 5, 15, 13, 30, 0, TimeSpan.Zero),
                ApprovalNote: "Approved with known exceptions."));

        Assert.Single(approvals.Saved);
        Assert.Equal("Approved with known exceptions.", approvals.Saved[0].ApprovalNote);
        Assert.Single(audits.Saved);
        Assert.Equal(AuditActionType.Approve, audits.Saved[0].ActionType);
        Assert.Equal(BranchReportAuditReasonCodes.Approved, audits.Saved[0].ReasonCode);

        using var doc = JsonDocument.Parse(response.Approval.SnapshotJson);
        Assert.Equal(2, doc.RootElement.GetProperty("ReviewCount").GetInt32());
        Assert.Equal("Reviewed", doc.RootElement.GetProperty("LifecycleStatus").GetString());
    }

    [Fact]
    public void ApproveBranchReportVersion_allows_empty_note_when_lenient_policy_even_if_unresolved()
    {
        var reports = new FakeBranchReportRepository();
        reports.Reports[ReportId] = SampleReport();
        reports.Metrics[ReportId] = SampleMetrics();

        var items = new FakeReconciliationItemRepository();
        items.ByRun[RunId] = [CreateTaupoItem(resolved: false)];

        var approvals = new FakeBranchReportApprovalRepository();

        var useCase = CreateApproveUseCase(reports, approvals, items, new FakeAuditRepository());

        var response = useCase.Execute(
            new ApproveBranchReportVersionRequest(
                ReportId,
                ApprovedBy: "mgr",
                ApprovedAtUtc: new DateTimeOffset(2026, 5, 15, 14, 0, 0, TimeSpan.Zero),
                ApprovalNote: null,
                Policy: BranchReportApprovalPolicy.Lenient));

        Assert.Single(approvals.Saved);
        Assert.Null(approvals.Saved[0].ApprovalNote);
        Assert.NotEmpty(response.Approval.SnapshotJson);
    }

    [Fact]
    public void ApproveBranchReportVersion_ignores_unresolved_items_for_other_branches_when_checking_note_policy()
    {
        var reports = new FakeBranchReportRepository();
        reports.Reports[ReportId] = SampleReport();
        reports.Metrics[ReportId] = SampleMetrics();

        var items = new FakeReconciliationItemRepository();
        items.ByRun[RunId] =
        [
            CreateItemForBranch(new CanonicalBranchId("KERIKERI"), resolved: false),
        ];

        var approvals = new FakeBranchReportApprovalRepository();

        var useCase = CreateApproveUseCase(reports, approvals, items, new FakeAuditRepository());

        useCase.Execute(
            new ApproveBranchReportVersionRequest(
                ReportId,
                ApprovedBy: "mgr",
                ApprovedAtUtc: new DateTimeOffset(2026, 5, 15, 14, 30, 0, TimeSpan.Zero),
                ApprovalNote: null));

        Assert.Single(approvals.Saved);
    }

    [Fact]
    public void ApproveBranchReportVersion_allows_empty_note_when_all_branch_items_are_resolved()
    {
        var reports = new FakeBranchReportRepository();
        reports.Reports[ReportId] = SampleReport();
        reports.Metrics[ReportId] = SampleMetrics();

        var items = new FakeReconciliationItemRepository();
        items.ByRun[RunId] = [CreateTaupoItem(resolved: true)];

        var approvals = new FakeBranchReportApprovalRepository();

        var useCase = CreateApproveUseCase(reports, approvals, items, new FakeAuditRepository());

        useCase.Execute(
            new ApproveBranchReportVersionRequest(
                ReportId,
                ApprovedBy: "mgr",
                ApprovedAtUtc: new DateTimeOffset(2026, 5, 15, 15, 0, 0, TimeSpan.Zero),
                ApprovalNote: null));

        Assert.Single(approvals.Saved);
    }

    private static IApproveBranchReportVersionUseCase CreateApproveUseCase(
        IBranchReportRepository branchReports,
        IBranchReportApprovalRepository approvals,
        IReconciliationItemRepository items,
        IAuditRepository audits) =>
        new ApproveBranchReportVersionUseCase(branchReports, approvals, items, audits);

    private static BranchReportVersion SampleReport() =>
        new(
            ReportId,
            RunId,
            Taupo,
            Period,
            versionNumber: 3,
            new DateTimeOffset(2026, 5, 15, 9, 0, 0, TimeSpan.Zero),
            "arina",
            PeriodLifecycleStatus.Reviewed);

    private static BranchReportPersistedMetrics SampleMetrics() =>
        new(
            ReviewCount: 2,
            LifecycleStatus: PeriodLifecycleStatus.Reviewed,
            SupplierLitres: new Litres(100m),
            BranchLitres: new Litres(95m),
            BilledLitres: new Litres(90m),
            UnbilledLitres: new Litres(5m),
            EstimatedRecovery: new MoneyAmount(12.345m));

    private static ReconciliationItem CreateTaupoItem(bool resolved) =>
        CreateItemForBranch(Taupo, resolved);

    private static ReconciliationItem CreateItemForBranch(CanonicalBranchId branchId, bool resolved)
    {
        var resolution = resolved ? ResolutionStatus.Resolved : ResolutionStatus.Unresolved;

        return new ReconciliationItem(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            RunId,
            Period,
            ReconciliationStatus.Matched,
            resolution,
            ConfidenceBucket.High,
            [],
            branchId,
            ReconciliationStatus.Matched,
            humanReadableReason: null);
    }

    private sealed class FakeBranchReportRepository : IBranchReportRepository
    {
        public Dictionary<Guid, BranchReportVersion> Reports { get; } = [];

        public Dictionary<Guid, BranchReportPersistedMetrics?> Metrics { get; } = [];

        public void Save(BranchReportVersion report, BranchSummary? summary = null) =>
            Reports[report.Id] = report;

        public BranchReportVersion? GetById(Guid id) =>
            Reports.TryGetValue(id, out var report) ? report : null;

        public BranchReportPersistedMetrics? GetPersistedMetrics(Guid branchReportVersionId) =>
            Metrics.TryGetValue(branchReportVersionId, out var metrics) ? metrics : null;

        public IReadOnlyList<BranchReportVersion> ListByRunAndBranch(Guid runId, CanonicalBranchId branchId) =>
            Reports.Values
                .Where(r => r.RunId == runId && r.BranchId.Value == branchId.Value)
                .OrderBy(r => r.VersionNumber)
                .ToArray();
    }

    private sealed class FakeBranchReportNoteRepository : IBranchReportNoteRepository
    {
        public List<BranchReportNote> Saved { get; } = [];

        public Dictionary<Guid, List<BranchReportNote>> ByBranch { get; } = [];

        public void Save(BranchReportNote note) => Saved.Add(note);

        public IReadOnlyList<BranchReportNote> ListByBranchReport(Guid branchReportVersionId) =>
            ByBranch.TryGetValue(branchReportVersionId, out var list)
                ? list
                : [];
    }

    private sealed class FakeBranchReportApprovalRepository : IBranchReportApprovalRepository
    {
        public List<BranchReportApprovalRecord> Saved { get; } = [];

        public Dictionary<Guid, BranchReportApprovalRecord> ByBranch { get; } = [];

        public void Save(BranchReportApprovalRecord approval)
        {
            Saved.Add(approval);
            ByBranch[approval.BranchReportVersionId] = approval;
        }

        public BranchReportApprovalRecord? FindByBranchReport(Guid branchReportVersionId) =>
            ByBranch.TryGetValue(branchReportVersionId, out var existing) ? existing : null;
    }

    private sealed class FakeReconciliationItemRepository : IReconciliationItemRepository
    {
        public List<ReconciliationItem> Saved { get; } = [];

        public Dictionary<Guid, List<ReconciliationItem>> ByRun { get; } = [];

        public void Save(ReconciliationItem item) => Saved.Add(item);

        public void SaveMany(IEnumerable<ReconciliationItem> items) =>
            Saved.AddRange(items);

        public ReconciliationItem? GetById(Guid id) =>
            Saved.FirstOrDefault(i => i.Id == id);

        public IReadOnlyList<ReconciliationItem> ListByRun(Guid runId) =>
            ByRun.TryGetValue(runId, out var list) ? list : [];
    }

    private sealed class FakeAuditRepository : IAuditRepository
    {
        public List<AuditRecord> Saved { get; } = [];

        public void Save(AuditRecord auditRecord) => Saved.Add(auditRecord);

        public AuditRecord? GetById(Guid id) =>
            Saved.FirstOrDefault(a => a.Id == id);

        public IReadOnlyList<AuditRecord> ListByEntity(AuditEntityType entityType, string entityId) =>
            Saved.Where(a => a.EntityType == entityType && a.EntityId == entityId).ToArray();
    }
}
