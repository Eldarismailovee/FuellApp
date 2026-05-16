namespace FuelRecon.Application.Pdf.Export;

/// <summary>
/// Renders branch report PDF layout (Infrastructure provides PDFsharp implementation).
/// </summary>
public interface IBranchReportPdfRenderer
{
    void Render(BranchReportPdfDocumentContent content, Stream output);
}
