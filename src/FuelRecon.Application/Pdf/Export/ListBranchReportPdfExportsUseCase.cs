using FuelRecon.Application.Persistence;
using FuelRecon.Domain;

namespace FuelRecon.Application.Pdf.Export;

/// <summary>
/// Application read model for PDF export history rows (append-only source: <see cref="PdfExportRecord"/>).
/// </summary>
public sealed record PdfExportHistoryEntry(
    Guid ExportId,
    Guid BranchReportVersionId,
    DateTimeOffset ExportedAtUtc,
    string ExportedBy,
    PdfExportStatus Status,
    string? FilePath,
    string? TemplateName,
    string? TemplateVersion,
    string? ErrorCategory,
    string? ErrorMessage,
    string? ExportSettingsSnapshotJson,
    CorrelationId? CorrelationId)
{
    public static PdfExportHistoryEntry FromRecord(PdfExportRecord record) =>
        new(
            record.Id,
            record.BranchReportVersionId,
            record.ExportedAtUtc,
            record.ExportedBy,
            record.Status,
            record.FilePath,
            record.TemplateName,
            record.TemplateVersion,
            record.ErrorCategory,
            record.ErrorMessage,
            record.ExportSettingsSnapshot,
            record.CorrelationId);
}

public sealed record ListBranchReportPdfExportsRequest(Guid BranchReportVersionId);

public sealed record ListBranchReportPdfExportsResponse(IReadOnlyList<PdfExportHistoryEntry> Exports);

public interface IListBranchReportPdfExportsUseCase
{
    ListBranchReportPdfExportsResponse Execute(ListBranchReportPdfExportsRequest request);
}

/// <summary>
/// Lists PDF export attempts for a branch report version (newest first, deterministic tie-break by export id).
/// </summary>
public sealed class ListBranchReportPdfExportsUseCase(IPdfExportRepository pdfExportRepository)
    : IListBranchReportPdfExportsUseCase
{
    public ListBranchReportPdfExportsResponse Execute(ListBranchReportPdfExportsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.BranchReportVersionId == Guid.Empty)
        {
            throw new ArgumentException("Branch report version id cannot be empty.", nameof(request));
        }

        var rows = pdfExportRepository.ListByBranchReport(request.BranchReportVersionId);
        var entries = rows.Select(PdfExportHistoryEntry.FromRecord).ToArray();
        return new ListBranchReportPdfExportsResponse(entries);
    }
}
