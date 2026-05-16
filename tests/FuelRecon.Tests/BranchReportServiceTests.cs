using FuelRecon.Application.BranchReports;
using FuelRecon.Application.Reconciliation;
using FuelRecon.Domain;

namespace FuelRecon.Tests;

public class BranchReportServiceTests
{
    private static readonly FuelPeriod Period = new(2026, 4);
    private static readonly Guid RunId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly CanonicalBranchId Taupo = new("TAUPO");
    private static readonly CanonicalBranchId Kerikeri = new("KERIKERI");

    private readonly IBranchReportService _service = new BranchReportService();

    [Fact]
    public void Build_maps_branch_items_into_status_counts_and_category_buckets()
    {
        var run = CreateRun();
        var summaryTaupo = CreateSummary(Taupo, run.Id, reviewCount: 5);

        var items = new[]
        {
            CreateItem(run.Id, "00000000-0000-0000-0000-000000000010", ReconciliationStatus.Matched, ResolutionStatus.Resolved, Taupo),
            CreateItem(run.Id, "00000000-0000-0000-0000-000000000011", ReconciliationStatus.Unbilled, ResolutionStatus.Unresolved, Taupo),
            CreateItem(run.Id, "00000000-0000-0000-0000-000000000012", ReconciliationStatus.Variance, ResolutionStatus.Unresolved, Taupo),
            CreateItem(run.Id, "00000000-0000-0000-0000-000000000013", ReconciliationStatus.DuplicatePossible, ResolutionStatus.InReview, Taupo),
            CreateItem(run.Id, "00000000-0000-0000-0000-000000000014", ReconciliationStatus.ReviewRequired, ResolutionStatus.Unresolved, Taupo),
            CreateItem(run.Id, "00000000-0000-0000-0000-000000000015", ReconciliationStatus.CarsOnly, ResolutionStatus.Unresolved, Taupo),
            CreateItem(run.Id, "00000000-0000-0000-0000-000000000016", ReconciliationStatus.SupplierOnly, ResolutionStatus.Unresolved, Taupo),
            CreateItem(run.Id, "00000000-0000-0000-0000-000000000017", ReconciliationStatus.RegoMismatch, ResolutionStatus.Unresolved, Taupo),
        };

        var engineResult = new ReconciliationEngineResult(run, items, [summaryTaupo]);
        var report = _service.Build(engineResult, Taupo);

        Assert.Equal(Period, report.Period);
        Assert.Equal(RunId, report.RunId);
        Assert.Equal(Taupo, report.BranchId);
        Assert.Same(summaryTaupo, report.BranchSummary);

        Assert.Equal(Enum.GetValues<ReconciliationStatus>().Length, report.CountsBySystemStatus.Count);
        Assert.Equal(1, report.CountsBySystemStatus.Single(x => x.Status == ReconciliationStatus.Matched).Count);
        Assert.Equal(1, report.CountsBySystemStatus.Single(x => x.Status == ReconciliationStatus.Unbilled).Count);
        Assert.Equal(1, report.CountsBySystemStatus.Single(x => x.Status == ReconciliationStatus.Variance).Count);
        Assert.Equal(1, report.CountsBySystemStatus.Single(x => x.Status == ReconciliationStatus.DuplicatePossible).Count);
        Assert.Equal(1, report.CountsBySystemStatus.Single(x => x.Status == ReconciliationStatus.ReviewRequired).Count);
        Assert.Equal(1, report.CountsBySystemStatus.Single(x => x.Status == ReconciliationStatus.CarsOnly).Count);
        Assert.Equal(1, report.CountsBySystemStatus.Single(x => x.Status == ReconciliationStatus.SupplierOnly).Count);
        Assert.Equal(1, report.CountsBySystemStatus.Single(x => x.Status == ReconciliationStatus.RegoMismatch).Count);
        Assert.Equal(0, report.CountsBySystemStatus.Single(x => x.Status == ReconciliationStatus.MissingRA).Count);

        Assert.Single(report.MatchedItems);
        Assert.Equal(ReconciliationStatus.Matched, report.MatchedItems[0].SystemStatus);

        Assert.Equal(7, report.UnresolvedItems.Count);
        Assert.DoesNotContain(report.UnresolvedItems, item => item.SystemStatus == ReconciliationStatus.Matched);

        Assert.Equal(4, report.ExceptionItems.Count);
        Assert.Contains(report.ExceptionItems, item => item.SystemStatus == ReconciliationStatus.Variance);
        Assert.Contains(report.ExceptionItems, item => item.SystemStatus == ReconciliationStatus.DuplicatePossible);
        Assert.Contains(report.ExceptionItems, item => item.SystemStatus == ReconciliationStatus.ReviewRequired);
        Assert.Contains(report.ExceptionItems, item => item.SystemStatus == ReconciliationStatus.RegoMismatch);

        Assert.Single(report.SupplierOnlyItems);
        Assert.Single(report.CarsOnlyItems);
        Assert.Single(report.UnbilledItems);
        Assert.Single(report.VarianceItems);

        Assert.Equal(2, report.ReviewItems.Count);
        Assert.Contains(report.ReviewItems, item => item.SystemStatus == ReconciliationStatus.DuplicatePossible);
        Assert.Contains(report.ReviewItems, item => item.SystemStatus == ReconciliationStatus.ReviewRequired);
    }

    [Fact]
    public void Build_scopes_rows_to_requested_branch_only()
    {
        var run = CreateRun();
        var summaryTaupo = CreateSummary(Taupo, run.Id, reviewCount: 0);
        var summaryKerikeri = CreateSummary(Kerikeri, run.Id, reviewCount: 0, supplierLitres: 0m, branchLitres: 3m);

        var taupoItem = CreateItem(run.Id, "00000000-0000-0000-0000-000000000020", ReconciliationStatus.Matched, ResolutionStatus.Resolved, Taupo);
        var kerikeriItem = CreateItem(run.Id, "00000000-0000-0000-0000-000000000021", ReconciliationStatus.Unbilled, ResolutionStatus.Unresolved, Kerikeri);

        var engineResult = new ReconciliationEngineResult(run, [kerikeriItem, taupoItem], [summaryTaupo, summaryKerikeri]);

        var taupoReport = _service.Build(engineResult, Taupo);
        Assert.Single(taupoReport.MatchedItems);
        Assert.Equal(taupoItem.Id, taupoReport.MatchedItems[0].Id);
        Assert.Equal(0, taupoReport.CountsBySystemStatus.Single(x => x.Status == ReconciliationStatus.Unbilled).Count);

        var kerikeriReport = _service.Build(engineResult, Kerikeri);
        Assert.Single(kerikeriReport.UnbilledItems);
        Assert.Equal(kerikeriItem.Id, kerikeriReport.UnbilledItems[0].Id);
        Assert.Equal(0, kerikeriReport.CountsBySystemStatus.Single(x => x.Status == ReconciliationStatus.Matched).Count);
    }

    [Fact]
    public void Build_orders_branch_items_by_item_id_lexically()
    {
        var run = CreateRun();
        var summary = CreateSummary(Taupo, run.Id, reviewCount: 0);

        var third = CreateItem(run.Id, "00000000-0000-0000-0000-000000000099", ReconciliationStatus.Matched, ResolutionStatus.Resolved, Taupo);
        var first = CreateItem(run.Id, "00000000-0000-0000-0000-000000000097", ReconciliationStatus.Matched, ResolutionStatus.Resolved, Taupo);
        var second = CreateItem(run.Id, "00000000-0000-0000-0000-000000000098", ReconciliationStatus.Matched, ResolutionStatus.Resolved, Taupo);

        var engineResult = new ReconciliationEngineResult(run, [third, first, second], [summary]);
        var report = _service.Build(engineResult, Taupo);

        Assert.Equal([first.Id, second.Id, third.Id], report.MatchedItems.Select(item => item.Id).ToArray());
    }

    [Fact]
    public void Build_throws_when_branch_summary_is_absent()
    {
        var run = CreateRun();
        var summaryKerikeri = CreateSummary(Kerikeri, run.Id, reviewCount: 0);
        var engineResult = new ReconciliationEngineResult(run, [], [summaryKerikeri]);

        var exception = Assert.Throws<ArgumentException>((Action)(() => _service.Build(engineResult, Taupo)));
        Assert.Contains("No branch summary", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_throws_when_summary_run_id_does_not_match_engine_run()
    {
        var run = CreateRun();
        var wrongRunSummary = CreateSummary(Taupo, Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), reviewCount: 0);
        var engineResult = new ReconciliationEngineResult(run, [], [wrongRunSummary]);

        Assert.Throws<ArgumentException>((Action)(() => _service.Build(engineResult, Taupo)));
    }

    [Fact]
    public void BuildFromPersisted_matches_Build_when_inputs_are_equivalent()
    {
        var run = CreateRun();
        var summaryTaupo = CreateSummary(Taupo, run.Id, reviewCount: 5);

        var items = new[]
        {
            CreateItem(run.Id, "00000000-0000-0000-0000-000000000010", ReconciliationStatus.Matched, ResolutionStatus.Resolved, Taupo),
            CreateItem(run.Id, "00000000-0000-0000-0000-000000000011", ReconciliationStatus.Unbilled, ResolutionStatus.Unresolved, Taupo),
            CreateItem(run.Id, "00000000-0000-0000-0000-000000000012", ReconciliationStatus.Variance, ResolutionStatus.Unresolved, Taupo),
        };

        var engineResult = new ReconciliationEngineResult(run, items, [summaryTaupo]);
        var fromEngine = _service.Build(engineResult, Taupo);
        var fromPersisted = _service.BuildFromPersisted(run, Taupo, summaryTaupo, items);

        Assert.Equal(fromEngine.MatchedItems.Select(i => i.Id), fromPersisted.MatchedItems.Select(i => i.Id));
        Assert.Equal(fromEngine.ExceptionItems.Select(i => i.Id), fromPersisted.ExceptionItems.Select(i => i.Id));
        Assert.Equal(fromEngine.UnresolvedItems.Count, fromPersisted.UnresolvedItems.Count);
        Assert.Equal(fromEngine.CountsBySystemStatus.Select(c => c.Count), fromPersisted.CountsBySystemStatus.Select(c => c.Count));
    }

    private static ReconciliationRun CreateRun() =>
        new(
            RunId,
            Period,
            new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero),
            "test",
            [new FileChecksum("SHA256", "run")],
            status: ReconciliationRunStatus.Completed,
            completedAtUtc: new DateTimeOffset(2026, 5, 15, 12, 1, 0, TimeSpan.Zero),
            totalItemCount: 0,
            matchedItemCount: 0,
            reviewRequiredCount: 0);

    private static BranchSummary CreateSummary(
        CanonicalBranchId branchId,
        Guid runId,
        int reviewCount,
        decimal supplierLitres = 100m,
        decimal branchLitres = 100m) =>
        new(
            branchId,
            Period,
            runId,
            new Litres(supplierLitres),
            new Litres(branchLitres),
            new Litres(50m),
            new Litres(5m),
            new MoneyAmount(0m),
            reviewCount,
            ReconciliationStatus.ReviewRequired);

    private static ReconciliationItem CreateItem(
        Guid runId,
        string id,
        ReconciliationStatus systemStatus,
        ResolutionStatus resolutionStatus,
        CanonicalBranchId branchId) =>
        new(
            Guid.Parse(id),
            runId,
            Period,
            systemStatus,
            resolutionStatus,
            ConfidenceBucket.Medium,
            [systemStatus.ToString()],
            branchId,
            branchSourceReference: new SourceReference("branch.xlsx", sheetName: "April", rowNumber: 2));
}
