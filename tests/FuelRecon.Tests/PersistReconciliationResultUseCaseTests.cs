using FuelRecon.Application.Persistence;
using FuelRecon.Application.Reconciliation;
using FuelRecon.Domain;

namespace FuelRecon.Tests;

public class PersistReconciliationResultUseCaseTests
{
    [Fact]
    public void Execute_saves_reconciliation_run()
    {
        var repositories = new FakeRepositories();
        var result = CreateResult();

        repositories.CreateUseCase().Execute(new PersistReconciliationResultRequest(result, "arina"));

        var savedRun = Assert.Single(repositories.Runs.Saved);
        Assert.Same(result.Run, savedRun);
    }

    [Fact]
    public void Execute_saves_reconciliation_items()
    {
        var repositories = new FakeRepositories();
        var result = CreateResult();

        repositories.CreateUseCase().Execute(new PersistReconciliationResultRequest(result));

        var savedItems = Assert.Single(repositories.Items.SavedManyCalls);
        Assert.Equal(result.Items.Count, savedItems.Count);
        Assert.Same(result.Items[0], savedItems[0]);
        Assert.Same(result.Items[1], savedItems[1]);
    }

    [Fact]
    public void Execute_returns_run_id_and_item_count()
    {
        var repositories = new FakeRepositories();
        var result = CreateResult();

        var response = repositories.CreateUseCase().Execute(new PersistReconciliationResultRequest(result));

        Assert.Equal(result.Run.Id, response.RunId);
        Assert.Equal(result.Items.Count, response.SavedItemCount);
    }

    [Fact]
    public void Execute_does_not_mutate_engine_result()
    {
        var repositories = new FakeRepositories();
        var result = CreateResult();
        var originalRun = result.Run;
        var originalItems = result.Items.ToArray();
        var originalSummaries = result.BranchSummaries.ToArray();

        repositories.CreateUseCase().Execute(new PersistReconciliationResultRequest(result));

        Assert.Same(originalRun, result.Run);
        Assert.Equal(originalItems.Length, result.Items.Count);
        Assert.Equal(originalSummaries.Length, result.BranchSummaries.Count);
        Assert.True(originalItems.SequenceEqual(result.Items));
        Assert.True(originalSummaries.SequenceEqual(result.BranchSummaries));
    }

    [Fact]
    public void Execute_does_not_call_item_repository_when_no_items_exist()
    {
        var repositories = new FakeRepositories();
        var run = CreateRun(itemCount: 0);
        var result = new ReconciliationEngineResult(run, [], []);

        var response = repositories.CreateUseCase().Execute(new PersistReconciliationResultRequest(result));

        Assert.Equal(run.Id, response.RunId);
        Assert.Equal(0, response.SavedItemCount);
        Assert.Single(repositories.Runs.Saved);
        Assert.Empty(repositories.Items.SavedManyCalls);
    }

    private static ReconciliationEngineResult CreateResult()
    {
        var run = CreateRun(itemCount: 2);
        var first = CreateItem(run.Id, "00000000-0000-0000-0000-000000000101", ReconciliationStatus.Matched);
        var second = CreateItem(run.Id, "00000000-0000-0000-0000-000000000102", ReconciliationStatus.Unbilled);
        var summary = new BranchSummary(
            new CanonicalBranchId("TAUPO"),
            new FuelPeriod(2026, 4),
            run.Id,
            new Litres(10m),
            new Litres(10m),
            new Litres(10m),
            new Litres(0m),
            new MoneyAmount(0m),
            1,
            ReconciliationStatus.ReviewRequired);

        return new ReconciliationEngineResult(run, [first, second], [summary]);
    }

    private static ReconciliationRun CreateRun(int itemCount) =>
        new(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            new FuelPeriod(2026, 4),
            new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero),
            "arina",
            [new FileChecksum("SHA256", "run")],
            status: ReconciliationRunStatus.Completed,
            completedAtUtc: new DateTimeOffset(2026, 5, 15, 12, 1, 0, TimeSpan.Zero),
            totalItemCount: itemCount,
            matchedItemCount: itemCount == 0 ? 0 : 1,
            reviewRequiredCount: itemCount == 0 ? 0 : 1);

    private static ReconciliationItem CreateItem(Guid runId, string id, ReconciliationStatus status) =>
        new(
            Guid.Parse(id),
            runId,
            new FuelPeriod(2026, 4),
            status,
            status == ReconciliationStatus.Matched ? ResolutionStatus.Resolved : ResolutionStatus.Unresolved,
            ConfidenceBucket.High,
            [status.ToString()],
            new CanonicalBranchId("TAUPO"),
            branchSourceReference: new SourceReference("branch.xlsx", sheetName: "April", rowNumber: 2));

    private sealed class FakeRepositories
    {
        public FakeReconciliationRunRepository Runs { get; } = new();
        public FakeReconciliationItemRepository Items { get; } = new();

        public PersistReconciliationResultUseCase CreateUseCase() => new(Runs, Items);
    }

    private sealed class FakeReconciliationRunRepository : IReconciliationRunRepository
    {
        public List<ReconciliationRun> Saved { get; } = [];

        public void Save(ReconciliationRun run) => Saved.Add(run);

        public ReconciliationRun? GetById(Guid id) => Saved.FirstOrDefault(run => run.Id == id);

        public ReconciliationRun? GetLatestForPeriod(FuelPeriod period) =>
            Saved.LastOrDefault(run => run.Period == period);
    }

    private sealed class FakeReconciliationItemRepository : IReconciliationItemRepository
    {
        public List<IReadOnlyList<ReconciliationItem>> SavedManyCalls { get; } = [];

        public void Save(ReconciliationItem item) => SaveMany([item]);

        public void SaveMany(IEnumerable<ReconciliationItem> items) => SavedManyCalls.Add(items.ToArray());

        public ReconciliationItem? GetById(Guid id) =>
            SavedManyCalls.SelectMany(call => call).FirstOrDefault(item => item.Id == id);

        public IReadOnlyList<ReconciliationItem> ListByRun(Guid runId) =>
            SavedManyCalls.SelectMany(call => call).Where(item => item.RunId == runId).ToArray();
    }
}
