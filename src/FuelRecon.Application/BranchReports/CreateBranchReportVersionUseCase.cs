using FuelRecon.Application.Persistence;
using FuelRecon.Domain;

namespace FuelRecon.Application.BranchReports;

public sealed record CreateBranchReportVersionRequest(
    Guid RunId,
    CanonicalBranchId BranchId,
    BranchSummary SummarySnapshot,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    PeriodLifecycleStatus? InitialLifecycleStatus = null,
    string? Notes = null);

public sealed record CreateBranchReportVersionResponse(BranchReportVersion Version);

public interface ICreateBranchReportVersionUseCase
{
    /// <summary>
    /// Persists a new append-only branch report version for the reconciliation run and branch.
    /// Version numbers increase monotonically per (run, branch); reconciliation rows are never modified.
    /// </summary>
    /// <exception cref="ArgumentException">Summary snapshot does not align with the request or run.</exception>
    /// <exception cref="InvalidOperationException">The reconciliation run does not exist.</exception>
    CreateBranchReportVersionResponse Execute(CreateBranchReportVersionRequest request);
}

public sealed class CreateBranchReportVersionUseCase(
    IReconciliationRunRepository reconciliationRunRepository,
    IBranchReportRepository branchReportRepository) : ICreateBranchReportVersionUseCase
{
    public CreateBranchReportVersionResponse Execute(CreateBranchReportVersionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SummarySnapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CreatedBy);

        if (request.RunId == Guid.Empty)
        {
            throw new ArgumentException("Run id cannot be empty.", nameof(request));
        }

        var run = reconciliationRunRepository.GetById(request.RunId);
        if (run is null)
        {
            throw new InvalidOperationException($"Reconciliation run '{request.RunId:D}' was not found.");
        }

        if (run.Period != request.SummarySnapshot.Period)
        {
            throw new ArgumentException(
                "Branch summary period does not match the reconciliation run period.",
                nameof(request));
        }

        if (request.SummarySnapshot.RunId != request.RunId)
        {
            throw new ArgumentException(
                "Branch summary run id does not match the requested run id.",
                nameof(request));
        }

        if (request.SummarySnapshot.BranchId.Value != request.BranchId.Value)
        {
            throw new ArgumentException(
                "Branch summary branch id does not match the requested branch id.",
                nameof(request));
        }

        var existing = branchReportRepository.ListByRunAndBranch(request.RunId, request.BranchId);
        var nextVersion = existing.Count == 0 ? 1 : existing.Max(version => version.VersionNumber) + 1;

        var lifecycleStatus = request.InitialLifecycleStatus ?? PeriodLifecycleStatus.Draft;

        var report = new BranchReportVersion(
            Guid.NewGuid(),
            request.RunId,
            request.BranchId,
            run.Period,
            nextVersion,
            request.CreatedAtUtc,
            request.CreatedBy,
            lifecycleStatus,
            request.Notes);

        branchReportRepository.Save(report, request.SummarySnapshot);

        return new CreateBranchReportVersionResponse(report);
    }
}
