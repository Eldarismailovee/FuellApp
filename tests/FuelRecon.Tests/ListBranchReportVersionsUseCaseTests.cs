using FuelRecon.Application.BranchReports;
using FuelRecon.Application.Persistence;
using FuelRecon.Domain;

namespace FuelRecon.Tests;

public class ListBranchReportVersionsUseCaseTests
{
    [Fact]
    public void Execute_returns_repository_ordering_for_run_and_branch()
    {
        var runId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-aaaaaaaaaaaa");
        var taupo = new CanonicalBranchId("TAUPO");
        var period = new FuelPeriod(2026, 4);

        var repository = new FakeBranchReportRepository();

        repository.AddWithoutSaveTracking(
            new BranchReportVersion(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                runId,
                taupo,
                period,
                1,
                new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
                "a",
                PeriodLifecycleStatus.Draft));

        repository.AddWithoutSaveTracking(
            new BranchReportVersion(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                runId,
                taupo,
                period,
                2,
                new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero),
                "b",
                PeriodLifecycleStatus.Reconciled));

        var useCase = new ListBranchReportVersionsUseCase(repository);
        var listed = useCase.Execute(new ListBranchReportVersionsRequest(runId, taupo));

        Assert.Equal(2, listed.Count);
        Assert.Equal(1, listed[0].VersionNumber);
        Assert.Equal(2, listed[1].VersionNumber);
    }

    private sealed class FakeBranchReportRepository : IBranchReportRepository
    {
        private readonly List<BranchReportVersion> _reports = [];

        public void AddWithoutSaveTracking(BranchReportVersion report) => _reports.Add(report);

        public void Save(BranchReportVersion report, BranchSummary? summary = null) =>
            throw new NotSupportedException();

        public BranchReportVersion? GetById(Guid id) =>
            _reports.FirstOrDefault(report => report.Id == id);

        public IReadOnlyList<BranchReportVersion> ListByRunAndBranch(Guid runId, CanonicalBranchId branchId) =>
            _reports
                .Where(report => report.RunId == runId && report.BranchId.Value == branchId.Value)
                .OrderBy(report => report.VersionNumber)
                .ToArray();
    }
}
