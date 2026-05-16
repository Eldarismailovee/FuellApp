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
        var correlationId = request.CorrelationId;

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
                errorCategory: notFound.ErrorCategory);

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
                PdfBranchExportErrorCategories.BranchReportNotFound);

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
                PdfBranchExportErrorCategories.BranchReportMetricsNotFound);

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
                PdfBranchExportErrorCategories.ReconciliationRunNotFound);

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
                PdfBranchExportErrorCategories.PdfWriteFailed);

            return FailedResponse(exportId, PdfBranchExportErrorCategories.PdfWriteFailed, template);
        }

        PersistExport(
            exportId,
            request,
            PdfExportStatus.Succeeded,
            fullPath,
            template.TemplateName,
            template.TemplateVersion,
            errorCategory: null);

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
        string? errorCategory)
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
}
