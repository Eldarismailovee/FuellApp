using FuelRecon.Application.Persistence;
using FuelRecon.Domain;

namespace FuelRecon.Application.BranchReports;

public sealed record ListBranchReportNotesRequest(Guid BranchReportVersionId);

public interface IListBranchReportNotesUseCase
{
    IReadOnlyList<BranchReportNote> Execute(ListBranchReportNotesRequest request);
}

public sealed class ListBranchReportNotesUseCase(IBranchReportNoteRepository branchReportNoteRepository)
    : IListBranchReportNotesUseCase
{
    public IReadOnlyList<BranchReportNote> Execute(ListBranchReportNotesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.BranchReportVersionId == Guid.Empty)
        {
            throw new ArgumentException("Branch report version id cannot be empty.", nameof(request));
        }

        return branchReportNoteRepository.ListByBranchReport(request.BranchReportVersionId);
    }
}
