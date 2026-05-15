using System.Text;
using FuelRecon.Domain;
using FuelRecon.Infrastructure.Pdf;

namespace FuelRecon.Tests;

public class SupplierPdfParserTests
{
    [Fact]
    public void Parse_generated_mobil_pdf_extracts_transaction_and_source_reference()
    {
        using var tempFile = TemporaryFile.Create(".pdf");
        WriteSimplePdf(tempFile.Path, "Mobil Statement\n01/04/2026 Mobil Taupo Diesel 42.125 Litres $123.45 INV-123");

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.True(result.Success);
        Assert.False(result.HasErrors);
        Assert.Equal(1, result.PageCount);
        Assert.True(result.CandidateRowCount >= 1);
        Assert.Equal(1, result.ValidRowCount);
        Assert.Equal(0, result.SkippedRowCount);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("Mobil", entry.SupplierName);
        Assert.Equal(new DateOnly(2026, 4, 1), entry.TransactionDate);
        Assert.Equal(42.13m, entry.Litres.Value);
        Assert.Equal(123.45m, entry.Amount?.Value);
        Assert.Equal("TAUPO", entry.BranchId?.Value);
        Assert.Equal("Mobil Taupo", entry.RawSiteText);
        Assert.Equal("INV-123", entry.VoucherOrInvoiceReference);
        Assert.Equal(tempFile.Path, entry.SourceReference.SourceFile);
        Assert.Equal(1, entry.SourceReference.PageNumber);
        Assert.Contains("01/04/2026", entry.SourceReference.ReferenceText);
    }

    [Fact]
    public void Parse_recognised_supplier_without_parseable_rows_returns_explicit_issue()
    {
        using var tempFile = TemporaryFile.Create(".pdf");
        WriteSimplePdf(tempFile.Path, "Farmlands Statement April\nSummary only no transaction table");

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.True(result.Success);
        Assert.True(result.HasWarnings);
        Assert.Empty(result.Entries);
        Assert.Equal(1, result.PageCount);
        Assert.Equal(0, result.CandidateRowCount);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(SupplierPdfParser.SupplierRowNotParsedReasonCode, issue.ReasonCode);
        Assert.Equal(tempFile.Path, issue.SourceReference?.SourceFile);
        Assert.Equal(1, issue.SourceReference?.PageNumber);
    }

    [Fact]
    public void Parse_unsupported_layout_returns_issue_for_page()
    {
        using var tempFile = TemporaryFile.Create(".pdf");
        WriteSimplePdf(tempFile.Path, "Unknown Supplier\n01/04/2026 Somewhere 10 Litres");

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.False(result.Success);
        Assert.True(result.HasErrors);
        Assert.Empty(result.Entries);
        Assert.Equal(1, result.PageCount);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(SupplierPdfParser.UnsupportedPdfLayoutReasonCode, issue.ReasonCode);
        Assert.Equal(tempFile.Path, issue.SourceReference?.SourceFile);
        Assert.Equal(1, issue.SourceReference?.PageNumber);
    }

    [Fact]
    public void Parse_surfaces_reader_failure_as_parser_issue()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.pdf");

        var result = CreateParser().Parse(path, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.False(result.Success);
        Assert.Empty(result.Entries);
        Assert.Equal(0, result.PageCount);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(PdfPigDocumentReader.PdfFileNotFoundReasonCode, issue.ReasonCode);
    }

    [Fact]
    public void Parse_reports_unresolved_branch_as_warning_but_keeps_transaction()
    {
        using var tempFile = TemporaryFile.Create(".pdf");
        WriteSimplePdf(tempFile.Path, "Mobil Statement\n01/04/2026 Unknown Site Diesel 10 Litres $20.00");

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.True(result.Success);
        var entry = Assert.Single(result.Entries);
        Assert.Null(entry.BranchId);
        Assert.True(result.HasWarnings);
        Assert.Contains(result.Issues, issue => issue.ReasonCode == BranchAliasResolver.BranchAliasNotFoundReasonCode);
    }

    [Theory]
    [InlineData("Farmlands Statement April.PDF")]
    [InlineData("Mobile - Taupo.pdf")]
    public void Parse_agreed_sample_pdf_when_present_returns_pages_and_evidence(string fileName)
    {
        var samplePath = Path.Combine("samples", "client-raw", fileName);
        if (!File.Exists(samplePath))
        {
            return;
        }

        var result = CreateParser().Parse(samplePath, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.True(result.PageCount > 0);
        Assert.True(result.Entries.Count > 0 || result.Issues.Count > 0);

        foreach (var entry in result.Entries)
        {
            Assert.Equal(samplePath, entry.SourceReference.SourceFile);
            Assert.NotNull(entry.SourceReference.PageNumber);
        }

        foreach (var issue in result.Issues)
        {
            if (issue.SourceReference is not null)
            {
                Assert.Equal(samplePath, issue.SourceReference.SourceFile);
                Assert.NotNull(issue.SourceReference.PageNumber);
            }
        }
    }

    private static SupplierPdfParser CreateParser() =>
        new(new PdfPigDocumentReader());

    private static BranchAliasResolver CreateBranchAliasResolver()
    {
        var taupo = new BranchMaster(new CanonicalBranchId("TAUPO"), "Taupo");
        return new BranchAliasResolver(
            [taupo],
            [
                new BranchAlias("Mobil Taupo", taupo.Id),
                new BranchAlias("Mobile - Taupo", taupo.Id),
                new BranchAlias("Taupo", taupo.Id),
            ]);
    }

    private static void WriteSimplePdf(string path, string text)
    {
        var escapedText = text
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

        var content = $"BT /F1 12 Tf 40 110 Td ({escapedText}) Tj ET";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 500 200] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream",
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
                $"fuelrecon-supplier-pdf-{Guid.NewGuid():N}{extension}");

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
