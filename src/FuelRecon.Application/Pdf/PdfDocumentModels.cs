namespace FuelRecon.Application.Pdf;

public sealed record PdfReadResult(
    bool Success,
    PdfDocumentModel? Document,
    string? ReasonCode,
    string Message)
{
    public static PdfReadResult Succeeded(PdfDocumentModel document) =>
        new(Success: true, document, ReasonCode: null, Message: "PDF read successfully.");

    public static PdfReadResult Failed(string reasonCode, string message) =>
        new(Success: false, Document: null, reasonCode, message);
}

public sealed record PdfDocumentModel(
    string SourceFile,
    IReadOnlyList<PdfPageModel> Pages);

public sealed record PdfPageModel(
    string SourceFile,
    int PageNumber,
    string Text,
    IReadOnlyList<string> Lines);
