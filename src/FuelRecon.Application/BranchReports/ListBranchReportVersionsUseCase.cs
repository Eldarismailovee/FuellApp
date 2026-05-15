using FuelRecon.Application.Persistence;
using FuelRecon.Domain;

namespace FuelRecon.Application.BranchReports;

public sealed record ListBranchReportVersionsRequest(Guid RunId, CanonicalBranchId BranchId);

public interface IListBranchReportVersionsUseCase
{
    /// <summary>
    /// Returns all saved branch report versions for the run and branch, ordered by version number (oldest first).
    /// </summary>
    IReadOnlyList<BranchReportVersion> Execute(ListBranchReportVersionsRequest request);
}

public sealed class ListBranchReportVersionsUseCase(IBranchReportRepository branchReportRepository)
    : IListBranchReportVersionsUseCase
{
    public IReadOnlyList<BranchReportVersion> Execute(ListBranchReportVersionsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return branchReportRepository.ListByRunAndBranch(request.RunId, request.BranchId);
    }
}
