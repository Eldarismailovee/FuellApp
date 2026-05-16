using FuelRecon.Application.BranchReports;
using FuelRecon.Application.Pdf.Templates;
using FuelRecon.Application.Persistence;
using FuelRecon.Domain;

namespace FuelRecon.Application.Pdf.Export;

public sealed record BranchReportPdfSigningSection(
    DateTimeOffset PreparedAtUtc,
    string PreparedBy,
    DateTimeOffset? ApprovedAtUtc,
    string? ApprovedBy,
    string? ApprovalNote);

/// <summary>
/// Immutable content snapshot passed to the PDF renderer (totals come from persisted branch report metrics).
/// </summary>
public sealed record BranchReportPdfDocumentContent(
    PdfTemplateConfiguration Template,
    BranchReportVersion ReportVersion,
    BranchReportPersistedMetrics Totals,
    BranchReportReadModel ReadModel,
    BranchReportPdfSigningSection Signing);
