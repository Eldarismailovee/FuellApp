namespace FuelRecon.Application.Pdf.Export;

/// <summary>
/// Non-template PDF export failure categories (persisted on <see cref="FuelRecon.Domain.PdfExportRecord"/>).
/// </summary>
public static class PdfBranchExportErrorCategories
{
    public const string BranchReportNotFound = "BranchReportNotFound";

    public const string BranchReportMetricsNotFound = "BranchReportMetricsNotFound";

    public const string ReconciliationRunNotFound = "ReconciliationRunNotFound";

    public const string PdfWriteFailed = "PdfWriteFailed";
}
