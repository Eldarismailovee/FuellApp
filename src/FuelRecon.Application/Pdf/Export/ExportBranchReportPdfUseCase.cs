using System.Text.Json;
using FuelRecon.Application.BranchReports;
using FuelRecon.Application.Pdf.Templates;
using FuelRecon.Application.Persistence;
using FuelRecon.Domain;

namespace FuelRecon.Application.Pdf.Export;

public sealed record ExportBranchReportPdfRequest(
    Guid BranchReportVersionId,
    string ExportedBy,
    DateTimeOffset ExportedAtUtc,
    string OutputDirectory,
    CorrelationId? CorrelationId = null);

public sealed record ExportBranchReportPdfResponse(
    Guid ExportId,
    PdfExportStatus Status,
    string? FilePath,
    string? ErrorCategory,
    string? TemplateName,
    string? TemplateVersion);

public interface IExportBranchReportPdfUseCase
{
    ExportBranchReportPdfResponse Execute(ExportBranchReportPdfRequest request);
}

public sealed class ExportBranchReportPdfUseCase(
    IPdfTemplateService pdfTemplateService,
    IBranchReportRepository branchReportRepository,
    IReconciliationRunRepository reconciliationRunRepository,
    IReconciliationItemRepository reconciliationItemRepository,
    IBranchReportApprovalRepository branchReportApprovalRepository,
    IPdfExportRepository pdfExportRepository,
    IBranchReportService branchReportService,
    IBranchReportPdfRenderer pdfRenderer) : IExportBranchReportPdfUseCase
{
    private static readonly JsonSerializerOptions ExportSnapshotJsonOptions = new() { WriteIndented = false };

    public ExportBranchReportPdfResponse Execute(ExportBranchReportPdfRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExportedBy);

        if (request.BranchReportVersionId == Guid.Empty)
        {
            throw new ArgumentException("Branch report version id cannot be empty.", nameof(request));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputDirectory);

        var exportId = Guid.NewGuid();

        var templateResult = pdfTemplateService.GetActiveTemplate();
        if (templateResult is PdfTemplateNotFound notFound)
        {
            PersistExport(
                exportId,
                request,
                PdfExportStatus.Failed,
                filePath: null,
                templateName: null,
                templateVersion: null,
                errorCategory: notFound.ErrorCategory,
                errorMessage: "Active PDF template configuration was not found.",
                SerializeSnapshot(BuildTemplateNotFoundSnapshot(request, notFound)));

            return new ExportBranchReportPdfResponse(
                exportId,
                PdfExportStatus.Failed,
                FilePath: null,
                ErrorCategory: notFound.ErrorCategory,
                TemplateName: null,
                TemplateVersion: null);
        }

        var resolved = (PdfTemplateResolved)templateResult;
        var template = resolved.Configuration;

        var report = branchReportRepository.GetById(request.BranchReportVersionId);
        if (report is null)
        {
            PersistExport(
                exportId,
                request,
                PdfExportStatus.Failed,
                filePath: null,
                template.TemplateName,
                template.TemplateVersion,
                PdfBranchExportErrorCategories.BranchReportNotFound,
                errorMessage: "Branch report version was not found.",
                SerializeSnapshot(BuildFailureSnapshot(request, template, "BranchReportNotFound")));

            return FailedResponse(exportId, PdfBranchExportErrorCategories.BranchReportNotFound, template);
        }

        var metrics = branchReportRepository.GetPersistedMetrics(report.Id);
        if (metrics is null)
        {
            PersistExport(
                exportId,
                request,
                PdfExportStatus.Failed,
                filePath: null,
                template.TemplateName,
                template.TemplateVersion,
                PdfBranchExportErrorCategories.BranchReportMetricsNotFound,
                errorMessage: "Persisted branch report metrics were not found.",
                SerializeSnapshot(BuildFailureSnapshot(request, template, "BranchReportMetricsNotFound")));

            return FailedResponse(exportId, PdfBranchExportErrorCategories.BranchReportMetricsNotFound, template);
        }

        var run = reconciliationRunRepository.GetById(report.RunId);
        if (run is null)
        {
            PersistExport(
                exportId,
                request,
                PdfExportStatus.Failed,
                filePath: null,
                template.TemplateName,
                template.TemplateVersion,
                PdfBranchExportErrorCategories.ReconciliationRunNotFound,
                errorMessage: "Reconciliation run was not found.",
                SerializeSnapshot(BuildFailureSnapshot(request, template, "ReconciliationRunNotFound")));

            return FailedResponse(exportId, PdfBranchExportErrorCategories.ReconciliationRunNotFound, template);
        }

        var items = reconciliationItemRepository.ListByRun(run.Id);
        var summary = BranchReportPersistedSummaryFactory.Create(report, metrics);
        var readModel = branchReportService.BuildFromPersisted(run, report.BranchId, summary, items);
        var approval = branchReportApprovalRepository.FindByBranchReport(report.Id);
        var signing = new BranchReportPdfSigningSection(
            report.CreatedAtUtc,
            report.CreatedBy,
            approval?.ApprovedAtUtc,
            approval?.ApprovedBy,
            approval?.ApprovalNote);

        var content = new BranchReportPdfDocumentContent(template, report, metrics, readModel, signing);

        Directory.CreateDirectory(request.OutputDirectory);

        var fileName = $"FuelRecon-BranchReport-{report.Id:D}-{exportId:N}.pdf";
        var fullPath = Path.Combine(request.OutputDirectory, fileName);

        try
        {
            using (var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                pdfRenderer.Render(content, stream);
            }
        }
        catch (Exception)
        {
            TryDeleteFile(fullPath);

            PersistExport(
                exportId,
                request,
                PdfExportStatus.Failed,
                filePath: null,
                template.TemplateName,
                template.TemplateVersion,
                PdfBranchExportErrorCategories.PdfWriteFailed,
                errorMessage: "Failed to write the PDF file.",
                SerializeSnapshot(BuildFailureSnapshot(request, template, "PdfWriteFailed")));

            return FailedResponse(exportId, PdfBranchExportErrorCategories.PdfWriteFailed, template);
        }

        PersistExport(
            exportId,
            request,
            PdfExportStatus.Succeeded,
            fullPath,
            template.TemplateName,
            template.TemplateVersion,
            errorCategory: null,
            errorMessage: null,
            SerializeSnapshot(BuildSuccessSnapshot(request, template)));

        return new ExportBranchReportPdfResponse(
            exportId,
            PdfExportStatus.Succeeded,
            fullPath,
            ErrorCategory: null,
            template.TemplateName,
            template.TemplateVersion);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup; export row still records failure without a usable PDF path.
        }
    }

    private void PersistExport(
        Guid exportId,
        ExportBranchReportPdfRequest request,
        PdfExportStatus status,
        string? filePath,
        string? templateName,
        string? templateVersion,
        string? errorCategory,
        string? errorMessage = null,
        string? exportSettingsSnapshot = null)
    {
        pdfExportRepository.Save(
            new PdfExportRecord(
                exportId,
                request.BranchReportVersionId,
                request.ExportedAtUtc,
                request.ExportedBy,
                status,
                filePath,
                templateName,
                templateVersion,
                errorCategory,
                errorMessage,
                exportSettingsSnapshot,
                request.CorrelationId));
    }

    private static ExportBranchReportPdfResponse FailedResponse(
        Guid exportId,
        string errorCategory,
        PdfTemplateConfiguration template) =>
        new(
            exportId,
            PdfExportStatus.Failed,
            FilePath: null,
            errorCategory,
            template.TemplateName,
            template.TemplateVersion);

    private static SortedDictionary<string, string?> BaseSnapshotFields(ExportBranchReportPdfRequest request) =>
        new(StringComparer.Ordinal)
        {
            ["branchReportVersionId"] = request.BranchReportVersionId.ToString("D"),
            ["outputDirectory"] = request.OutputDirectory,
            ["schemaVersion"] = "1",
        };

    private static SortedDictionary<string, string?> BuildTemplateNotFoundSnapshot(
        ExportBranchReportPdfRequest request,
        PdfTemplateNotFound notFound)
    {
        var fields = BaseSnapshotFields(request);
        fields["activeTemplateKey"] = notFound.ActiveTemplateKey;
        fields["failureStage"] = "TemplateNotFound";
        return fields;
    }

    private static SortedDictionary<string, string?> BuildFailureSnapshot(
        ExportBranchReportPdfRequest request,
        PdfTemplateConfiguration template,
        string failureStage)
    {
        var fields = BaseSnapshotFields(request);
        fields["failureStage"] = failureStage;
        fields["templateName"] = template.TemplateName;
        fields["templateVersion"] = template.TemplateVersion;
        return fields;
    }

    private static SortedDictionary<string, string?> BuildSuccessSnapshot(
        ExportBranchReportPdfRequest request,
        PdfTemplateConfiguration template)
    {
        var fields = BaseSnapshotFields(request);
        fields["exportOutcome"] = "Succeeded";
        fields["templateName"] = template.TemplateName;
        fields["templateVersion"] = template.TemplateVersion;
        return fields;
    }

    private static string SerializeSnapshot(SortedDictionary<string, string?> fields) =>
        JsonSerializer.Serialize(fields, ExportSnapshotJsonOptions);
}
