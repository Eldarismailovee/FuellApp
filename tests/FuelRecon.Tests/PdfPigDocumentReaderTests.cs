using System.Text;
using FuelRecon.Infrastructure.Pdf;

namespace FuelRecon.Tests;

public class PdfPigDocumentReaderTests
{
    [Theory]
    [InlineData(".txt")]
    [InlineData(".xlsx")]
    [InlineData(".csv")]
    public void ReadDocument_rejects_non_pdf_extensions(string extension)
    {
        using var tempFile = TemporaryFile.Create(extension);
        File.WriteAllText(tempFile.Path, "not a pdf");

        var result = new PdfPigDocumentReader().ReadDocument(tempFile.Path);

        Assert.False(result.Success);
        Assert.Null(result.Document);
        Assert.Equal(PdfPigDocumentReader.UnsupportedPdfFormatReasonCode, result.ReasonCode);
        Assert.Contains(".pdf", result.Message);
    }

    [Fact]
    public void ReadDocument_returns_clear_error_for_missing_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.pdf");

        var result = new PdfPigDocumentReader().ReadDocument(path);

        Assert.False(result.Success);
        Assert.Null(result.Document);
        Assert.Equal(PdfPigDocumentReader.PdfFileNotFoundReasonCode, result.ReasonCode);
        Assert.Contains("not found", result.Message);
    }

    [Fact]
    public void ReadDocument_returns_clear_error_for_corrupted_pdf()
    {
        using var tempFile = TemporaryFile.Create(".pdf");
        File.WriteAllText(tempFile.Path, "this is not a valid pdf");

        var result = new PdfPigDocumentReader().ReadDocument(tempFile.Path);

        Assert.False(result.Success);
        Assert.Null(result.Document);
        Assert.Equal(PdfPigDocumentReader.PdfReadFailedReasonCode, result.ReasonCode);
        Assert.Contains("could not be read", result.Message);
    }

    [Fact]
    public void ReadDocument_opens_readable_pdf_and_preserves_source_and_page_number()
    {
        using var tempFile = TemporaryFile.Create(".pdf");
        WriteSimplePdf(tempFile.Path, "Fuel Recon Test");

        var result = new PdfPigDocumentReader().ReadDocument(tempFile.Path);

        Assert.True(result.Success);
        Assert.Null(result.ReasonCode);
        Assert.NotNull(result.Document);
        Assert.Equal(tempFile.Path, result.Document.SourceFile);

        var page = Assert.Single(result.Document.Pages);
        Assert.Equal(tempFile.Path, page.SourceFile);
        Assert.Equal(1, page.PageNumber);
        Assert.Contains("Fuel Recon Test", page.Text);
        Assert.Contains(page.Lines, line => line.Contains("Fuel Recon Test", StringComparison.Ordinal));
    }

    private static void WriteSimplePdf(string path, string text)
    {
        var escapedText = text
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);

        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 144] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {Encoding.ASCII.GetByteCount($"BT /F1 18 Tf 40 80 Td ({escapedText}) Tj ET")} >>\nstream\nBT /F1 18 Tf 40 80 Td ({escapedText}) Tj ET\nendstream",
        };

        using var stream = File.Create(path);
        using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { NewLine = "\n" };

        writer.WriteLine("%PDF-1.4");
        writer.Flush();

        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(stream.Position);
            writer.WriteLine($"{index + 1} 0 obj");
            writer.WriteLine(objects[index]);
            writer.WriteLine("endobj");
            writer.Flush();
        }

        var xrefOffset = stream.Position;
        writer.WriteLine("xref");
        writer.WriteLine($"0 {objects.Length + 1}");
        writer.WriteLine("0000000000 65535 f ");

        for (var index = 1; index < offsets.Count; index++)
        {
            writer.WriteLine($"{offsets[index]:D10} 00000 n ");
        }

        writer.WriteLine("trailer");
        writer.WriteLine($"<< /Size {objects.Length + 1} /Root 1 0 R >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefOffset);
        writer.WriteLine("%%EOF");
    }

    private sealed class TemporaryFile : IDisposable
    {
        private TemporaryFile(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryFile Create(string extension)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"fuelrecon-pdf-{Guid.NewGuid():N}{extension}");

            return new TemporaryFile(path);
        }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}
