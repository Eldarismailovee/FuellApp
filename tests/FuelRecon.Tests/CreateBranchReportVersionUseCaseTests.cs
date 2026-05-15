using FuelRecon.Application.BranchReports;
using FuelRecon.Application.Persistence;
using FuelRecon.Domain;

namespace FuelRecon.Tests;

public class CreateBranchReportVersionUseCaseTests
{
    private static readonly FuelPeriod Period = new(2026, 4);
    private static readonly Guid RunId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly CanonicalBranchId Taupo = new("TAUPO");

    [Fact]
    public void Execute_throws_InvalidOperationException_when_run_is_unknown()
    {
        var runs = new FakeReconciliationRunRepository();
        var branchReports = new FakeBranchReportRepository();
        var useCase = new CreateBranchReportVersionUseCase(runs, branchReports);

        var exception = Assert.Throws<InvalidOperationException>(() => useCase.Execute(CreateValidRequest()));
        Assert.Contains(RunId.ToString("D"), exception.Message, StringComparison.Ordinal);
        Assert.Empty(branchReports.SavedReports);
    }

    [Fact]
    public void Execute_throws_when_summary_period_differs_from_run()
    {
        var runs = new FakeReconciliationRunRepository();
        runs.Save(CreateRun());
        var branchReports = new FakeBranchReportRepository();
        var useCase = new CreateBranchReportVersionUseCase(runs, branchReports);

        var badSummary = CreateSummary(runPeriod: new FuelPeriod(2026, 3));

        Assert.Throws<ArgumentException>(() => useCase.Execute(CreateValidRequest(summary: badSummary)));
        Assert.Empty(branchReports.SavedReports);
    }

    [Fact]
    public void Execute_throws_when_summary_run_id_differs_from_request()
    {
        var runs = new FakeReconciliationRunRepository();
        runs.Save(CreateRun());
        var branchReports = new FakeBranchReportRepository();
        var useCase = new CreateBranchReportVersionUseCase(runs, branchReports);

        var badSummary = CreateSummary(runId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

        Assert.Throws<ArgumentException>(() => useCase.Execute(CreateValidRequest(summary: badSummary)));
        Assert.Empty(branchReports.SavedReports);
    }

    [Fact]
    public void Execute_throws_when_summary_branch_differs_from_request()
    {
        var runs = new FakeReconciliationRunRepository();
        runs.Save(CreateRun());
        var branchReports = new FakeBranchReportRepository();
        var useCase = new CreateBranchReportVersionUseCase(runs, branchReports);

        var badSummary = CreateSummary(branchId: new CanonicalBranchId("KERIKERI"));

        Assert.Throws<ArgumentException>(() => useCase.Execute(CreateValidRequest(summary: badSummary)));
        Assert.Empty(branchReports.SavedReports);
    }

    [Fact]
    public void Execute_assigns_version_number_one_when_no_prior_versions_exist()
    {
        var runs = new FakeReconciliationRunRepository();
        runs.Save(CreateRun());
        var branchReports = new FakeBranchReportRepository();
        var useCase = new CreateBranchReportVersionUseCase(runs, branchReports);

        var response = useCase.Execute(CreateValidRequest());

        Assert.Equal(1, response.Version.VersionNumber);
        Assert.Equal(RunId, response.Version.RunId);
        Assert.Equal(Taupo, response.Version.BranchId);
        Assert.Equal(Period, response.Version.Period);
        Assert.Equal(PeriodLifecycleStatus.Draft, response.Version.Status);
        Assert.Single(branchReports.SavedReports);
        Assert.Same(response.Version, branchReports.SavedReports[0].Report);
        Assert.NotNull(branchReports.SavedReports[0].Summary);
    }

    [Fact]
    public void Execute_assigns_incremented_version_after_prior_report_exists()
    {
        var runs = new FakeReconciliationRunRepository();
        runs.Save(CreateRun());
        var branchReports = new FakeBranchReportRepository();
        branchReports.Seed(
            new BranchReportVersion(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                RunId,
                Taupo,
                Period,
                1,
                new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero),
                "prior",
                PeriodLifecycleStatus.Draft));

        var useCase = new CreateBranchReportVersionUseCase(runs, branchReports);

        var response = useCase.Execute(
            CreateValidRequest(createdAtUtc: new DateTimeOffset(2026, 5, 16, 0, 0, 0, TimeSpan.Zero)));

        Assert.Equal(2, response.Version.VersionNumber);
        Assert.Single(branchReports.SavedReports);
        Assert.Equal(2, branchReports.SavedReports[0].Report.VersionNumber);
    }

    [Fact]
    public void Execute_uses_requested_initial_lifecycle_status_when_provided()
    {
        var runs = new FakeReconciliationRunRepository();
        runs.Save(CreateRun());
        var branchReports = new FakeBranchReportRepository();
        var useCase = new CreateBranchReportVersionUseCase(runs, branchReports);

        var response = useCase.Execute(
            CreateValidRequest(initialLifecycleStatus: PeriodLifecycleStatus.Reconciled));

        Assert.Equal(PeriodLifecycleStatus.Reconciled, response.Version.Status);
    }

    private static CreateBranchReportVersionRequest CreateValidRequest(
        BranchSummary? summary = null,
        DateTimeOffset? createdAtUtc = null,
        PeriodLifecycleStatus? initialLifecycleStatus = null) =>
        new(
            RunId,
            Taupo,
            summary ?? CreateSummary(),
            CreatedBy: "unit-test",
            createdAtUtc ?? new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero),
            initialLifecycleStatus,
            Notes: "snapshot");

    private static ReconciliationRun CreateRun() =>
        new(
            RunId,
            Period,
            new DateTimeOffset(2026, 5, 15, 10, 0, 0, TimeSpan.Zero),
            "system",
            [new FileChecksum("SHA256", "abc")],
            status: ReconciliationRunStatus.Completed,
            completedAtUtc: new DateTimeOffset(2026, 5, 15, 10, 5, 0, TimeSpan.Zero));

    private static BranchSummary CreateSummary(
        FuelPeriod? runPeriod = null,
        Guid? runId = null,
        CanonicalBranchId? branchId = null) =>
        new(
            branchId ?? Taupo,
            runPeriod ?? Period,
            runId ?? RunId,
            new Litres(12m),
            new Litres(11m),
            new Litres(10m),
            new Litres(1m),
            new MoneyAmount(2.5m),
            reviewCount: 2,
            status: ReconciliationStatus.ReviewRequired);

    private sealed class FakeReconciliationRunRepository : IReconciliationRunRepository
    {
        private readonly Dictionary<Guid, ReconciliationRun> _runs = [];

        public void Save(ReconciliationRun run) => _runs[run.Id] = run;

        public ReconciliationRun? GetById(Guid id) => _runs.TryGetValue(id, out var run) ? run : null;

        public ReconciliationRun? GetLatestForPeriod(FuelPeriod period) =>
            throw new NotSupportedException();
    }

    private sealed class FakeBranchReportRepository : IBranchReportRepository
    {
        private readonly List<BranchReportVersion> _existing = [];

        public List<(BranchReportVersion Report, BranchSummary? Summary)> SavedReports { get; } = [];

        public void Seed(BranchReportVersion report) => _existing.Add(report);

        public void Save(BranchReportVersion report, BranchSummary? summary = null)
        {
            SavedReports.Add((report, summary));
            _existing.Add(report);
        }

        public BranchReportVersion? GetById(Guid id) =>
            _existing.FirstOrDefault(report => report.Id == id);

        public BranchReportPersistedMetrics? GetPersistedMetrics(Guid branchReportVersionId) => null;

        public IReadOnlyList<BranchReportVersion> ListByRunAndBranch(Guid runId, CanonicalBranchId branchId) =>
            _existing
                .Where(report => report.RunId == runId && report.BranchId.Value == branchId.Value)
                .OrderBy(report => report.VersionNumber)
                .ToArray();
    }
}
