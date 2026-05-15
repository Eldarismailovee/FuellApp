using FuelRecon.Application.Pdf;
using UglyToad.PdfPig;

namespace FuelRecon.Infrastructure.Pdf;

public sealed class PdfPigDocumentReader : IPdfDocumentReader
{
    public const string UnsupportedPdfFormatReasonCode = "UnsupportedPdfFormat";

    public const string PdfFileNotFoundReasonCode = "PdfFileNotFound";

    public const string PasswordProtectedPdfReasonCode = "PasswordProtectedFile";

    public const string PdfReadFailedReasonCode = "PdfReadFailed";

    public PdfReadResult ReadDocument(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return PdfReadResult.Failed(PdfFileNotFoundReasonCode, "PDF file path cannot be empty.");
        }

        if (!string.Equals(Path.GetExtension(filePath), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return PdfReadResult.Failed(
                UnsupportedPdfFormatReasonCode,
                "Only .pdf documents are supported.");
        }

        if (!File.Exists(filePath))
        {
            return PdfReadResult.Failed(PdfFileNotFoundReasonCode, "PDF document was not found.");
        }

        try
        {
            using var document = PdfDocument.Open(filePath);
            var pages = document
                .GetPages()
                .Select(page =>
                {
                    var text = page.Text ?? string.Empty;
                    var lines = text
                        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToArray();

                    return new PdfPageModel(filePath, page.Number, text, lines);
                })
                .ToArray();

            return PdfReadResult.Succeeded(new PdfDocumentModel(filePath, pages));
        }
        catch (Exception exception) when (LooksPasswordProtected(exception))
        {
            return PdfReadResult.Failed(
                PasswordProtectedPdfReasonCode,
                "PDF document appears to be password protected.");
        }
        catch (Exception exception)
        {
            return PdfReadResult.Failed(
                PdfReadFailedReasonCode,
                $"PDF document could not be read: {exception.Message}");
        }
    }

    private static bool LooksPasswordProtected(Exception exception) =>
        exception.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
        || exception.Message.Contains("encrypted", StringComparison.OrdinalIgnoreCase);
}
