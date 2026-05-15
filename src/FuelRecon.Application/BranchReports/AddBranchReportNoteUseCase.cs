using System.Text.Json;
using FuelRecon.Application.Persistence;
using FuelRecon.Domain;

namespace FuelRecon.Application.BranchReports;

public sealed record AddBranchReportNoteRequest(
    Guid BranchReportVersionId,
    string NoteText,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    string? ReasonCode = null);

public sealed record AddBranchReportNoteResponse(BranchReportNote Note);

public interface IAddBranchReportNoteUseCase
{
    /// <summary>
    /// Appends an immutable note row for a branch report version (does not touch reconciliation items).
    /// </summary>
    AddBranchReportNoteResponse Execute(AddBranchReportNoteRequest request);
}

public sealed class AddBranchReportNoteUseCase(
    IBranchReportRepository branchReportRepository,
    IBranchReportNoteRepository branchReportNoteRepository,
    IAuditRepository auditRepository) : IAddBranchReportNoteUseCase
{
    private const string AuditOrigin = "FuelRecon.Application.BranchReports";

    private static readonly JsonSerializerOptions AuditJsonOptions = new() { WriteIndented = false };

    public AddBranchReportNoteResponse Execute(AddBranchReportNoteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.NoteText);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CreatedBy);

        if (request.BranchReportVersionId == Guid.Empty)
        {
            throw new ArgumentException("Branch report version id cannot be empty.", nameof(request));
        }

        var report = branchReportRepository.GetById(request.BranchReportVersionId);
        if (report is null)
        {
            throw new InvalidOperationException(
                $"Branch report version '{request.BranchReportVersionId:D}' was not found.");
        }

        var note = new BranchReportNote(
            Guid.NewGuid(),
            request.BranchReportVersionId,
            request.CreatedAtUtc,
            request.CreatedBy,
            request.NoteText,
            request.ReasonCode);

        branchReportNoteRepository.Save(note);

        var auditPayload = JsonSerializer.Serialize(
            new { branchReportNoteId = note.Id.ToString("D"), branchReportVersionId = report.Id.ToString("D") },
            AuditJsonOptions);

        auditRepository.Save(
            new AuditRecord(
                Guid.NewGuid(),
                request.CreatedAtUtc,
                request.CreatedBy,
                AuditActionType.Create,
                AuditEntityType.BranchReport,
                report.Id.ToString("D"),
                AuditOrigin,
                NewValuesJson: auditPayload,
                Note: note.NoteText.Length > 512 ? note.NoteText[..512] : note.NoteText,
                ReasonCode: BranchReportAuditReasonCodes.NoteAppended));

        return new AddBranchReportNoteResponse(note);
    }
}
