using PdfSharp.Fonts;

namespace FuelRecon.Infrastructure.Pdf;

/// <summary>
/// Maps PDFsharp font requests to the embedded DejaVu Sans face (cross-platform; licensed under a permissive font license).
/// </summary>
internal sealed class BranchReportPdfFontResolver(byte[] fontBytes) : IFontResolver
{
    internal const string FaceKey = "FuelReconBranchReportDejaVuSans";

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new FontResolverInfo(FaceKey, isBold, isItalic);

    public byte[]? GetFont(string faceName) =>
        string.Equals(faceName, FaceKey, StringComparison.Ordinal) ? fontBytes : null;
}
