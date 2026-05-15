using System.Text;
using FuelRecon.Domain;
using FuelRecon.Infrastructure.Pdf;

namespace FuelRecon.Tests;

public class SupplierPdfParserTests
{
    /// <summary>
    /// Representative merged Mobil token sequence as produced by CLI / flattened PDF text extraction.
    /// </summary>
    private const string MobilMergedBlobCliSample =
        "02/04/2612:56Mobil Junction0578787.74LSynergy ExtraUnleaded3.45 R3.330.9325.773.36";

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

    [Fact]
    public void Parse_farmlands_concatenated_rows_extracts_transactions_and_deduplicates_repeated_rows()
    {
        using var tempFile = TemporaryFile.Create(".pdf");
        WriteSimplePdf(
            tempFile.Path,
            "Farmlands Statement April " +
            "01 Apr 26 Inv: 039842 Caltex Kerikeri 123456 Diesel 45.67 L $123.45 $107.35 " +
            "01 Apr 26 Inv: 039842 Caltex Kerikeri 123456 Diesel 45.67 L $123.45 $107.35 " +
            "10 Apr 26 Crd: 040457 Caltex Whangarei 444555 91 Unleaded -3.50 L -$9.50");

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.True(result.Success);
        Assert.True(result.CandidateRowCount >= 3);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("Farmlands", entry.SupplierName);
        Assert.Equal(new DateOnly(2026, 4, 1), entry.TransactionDate);
        Assert.Equal("Caltex Kerikeri", entry.RawSiteText);
        Assert.Equal("KERIKERI", entry.BranchId?.Value);
        Assert.Equal(45.67m, entry.Litres.Value);
        Assert.Equal("Diesel", entry.Product);
        Assert.Equal("Inv: 039842", entry.VoucherOrInvoiceReference);
        Assert.Equal(123.45m, entry.Amount?.Value);
        Assert.Equal(tempFile.Path, entry.SourceReference.SourceFile);
        Assert.Equal(1, entry.SourceReference.PageNumber);
        Assert.Contains(result.Issues, issue => issue.ReasonCode == SupplierPdfParser.SupplierRowNotParsedReasonCode);
    }

    [Fact]
    public void Parse_mobil_cardholder_section_uses_cardholder_as_branch_site_text()
    {
        using var tempFile = TemporaryFile.Create(".pdf");
        WriteSimplePdf(
            tempFile.Path,
            "Mobil Statement CARD NUMBER: 123456 NAME: HERTZ TAUPO 1/2 " +
            "05/04/2026 13:45 Mobil Taupo VOUCHER 998877 38.40 L Diesel $140.00 GST $18.26");

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.True(result.Success);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("Mobil", entry.SupplierName);
        Assert.Equal(new DateOnly(2026, 4, 5), entry.TransactionDate);
        Assert.Equal("HERTZ TAUPO 1/2", entry.Cardholder);
        Assert.Equal("HERTZ TAUPO", entry.RawSiteText);
        Assert.Equal("TAUPO", entry.BranchId?.Value);
        Assert.Equal(38.40m, entry.Litres.Value);
        Assert.Equal("Diesel", entry.Product);
        Assert.Equal("VOUCHER 998877", entry.VoucherOrInvoiceReference);
        Assert.Equal(140.00m, entry.Amount?.Value);
        Assert.Equal(1, entry.SourceReference.PageNumber);
    }

    [Fact]
    public void Parse_mobil_slash_date_blob_splits_multiple_transactions_without_line_breaks()
    {
        using var tempFile = TemporaryFile.Create(".pdf");
        WriteSimplePdf(
            tempFile.Path,
            "Mobil Statement CARD NO: 999 NAME: HERTZ TAUPO 1/2 " +
            "05/04/2026 Diesel 10.00 L $20.00 " +
            "06/04/2026 Petrol 11.50 L $25.00");

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.True(result.Success);
        Assert.Equal(2, result.Entries.Count);
        Assert.Contains(result.Entries, entry => entry.TransactionDate == new DateOnly(2026, 4, 5) && entry.Litres.Value == 10.00m);
        Assert.Contains(result.Entries, entry => entry.TransactionDate == new DateOnly(2026, 4, 6) && entry.Litres.Value == 11.50m);
        Assert.All(result.Entries, entry => Assert.Equal("TAUPO", entry.BranchId?.Value));
    }

    [Fact]
    public void Parse_mobil_inline_quantity_before_product_when_litres_suffix_missing()
    {
        using var tempFile = TemporaryFile.Create(".pdf");
        WriteSimplePdf(
            tempFile.Path,
            "Mobil Statement\n07/04/2026 Mobil Taupo REF 111 42.12 Diesel $88.00");

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.True(result.Success);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(new DateOnly(2026, 4, 7), entry.TransactionDate);
        Assert.Equal(42.12m, entry.Litres.Value);
        Assert.Equal("Diesel", entry.Product);
        Assert.Equal("TAUPO", entry.BranchId?.Value);
    }

    [Fact]
    public void Parse_mobil_merged_cli_blob_splits_slash_two_digit_year_time_litres_and_voucher()
    {
        using var tempFile = TemporaryFile.Create(".pdf");
        WriteSimplePdf(tempFile.Path, $"Mobil Statement\n{MobilMergedBlobCliSample}");

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.True(result.Success);
        Assert.False(result.HasErrors);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("Mobil", entry.SupplierName);
        Assert.Equal(new DateOnly(2026, 4, 2), entry.TransactionDate);
        Assert.Equal(7.74m, entry.Litres.Value);
        Assert.Equal("057878", entry.VoucherOrInvoiceReference);
        Assert.Equal("Mobil Junction", entry.RawSiteText);
        Assert.Equal("JUNCTION", entry.BranchId?.Value);
        Assert.Contains("Synergy Extra Unleaded", entry.Product ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        // ReferenceText uses Mobil blob spacing normalisation (e.g. L Synergy) vs raw CLI extraction.
        Assert.Contains("02/04/2612:56Mobil Junction0578787.74L", entry.SourceReference.ReferenceText, StringComparison.Ordinal);
        Assert.Contains("Synergy Extra Unleaded", entry.SourceReference.ReferenceText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(tempFile.Path, entry.SourceReference.SourceFile);
        Assert.Equal(1, entry.SourceReference.PageNumber);
    }

    [Fact]
    public void Parse_mobile_taupo_real_sample_extracts_many_transactions_when_present()
    {
        var samplePath = LocateClientRawSample("Mobile - Taupo.pdf");
        if (samplePath is null)
        {
            return;
        }

        var result = CreateParser().Parse(samplePath, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.True(result.Success);
        Assert.False(result.HasErrors);
        Assert.True(result.Entries.Count >= 55, $"Expected many Mobil transactions from merged PDF text (got {result.Entries.Count}).");
        Assert.True(result.CandidateRowCount >= 55);
        Assert.All(result.Entries, entry =>
        {
            Assert.True(entry.Litres.Value > 0);
            Assert.NotNull(entry.BranchId);
        });
    }

    [Theory]
    [InlineData("Farmlands Statement April.PDF")]
    [InlineData("Mobile - Taupo.pdf")]
    public void Parse_agreed_sample_pdf_when_present_returns_pages_and_evidence(string fileName)
    {
        var samplePath = LocateClientRawSample(fileName);
        if (samplePath is null)
        {
            return;
        }

        var result = CreateParser().Parse(samplePath, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.True(result.PageCount > 0);
        Assert.True(result.Entries.Count > 0 || result.Issues.Count > 0);

        if (fileName.Contains("Mobile", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("Mobil", StringComparison.OrdinalIgnoreCase))
        {
            Assert.NotEmpty(result.Entries);
            Assert.Contains(result.Entries, entry => entry.Litres.Value > 0);
        }

        if (result.Entries.Count == 0)
        {
            return;
        }

        foreach (var entry in result.Entries)
        {
            Assert.Equal(samplePath, entry.SourceReference.SourceFile);
            Assert.NotNull(entry.SourceReference.PageNumber);
            Assert.Equal(2026, entry.TransactionDate.Year);
            Assert.Equal(4, entry.TransactionDate.Month);
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

    private static string? LocateClientRawSample(string fileName)
    {
        foreach (var root in EnumerateRepositoryRoots())
        {
            var candidate = Path.Combine(root, "samples", "client-raw", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateRepositoryRoots()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FuelRecon.slnx")))
            {
                yield return directory.FullName;
                yield break;
            }

            directory = directory.Parent;
        }
    }

    private static BranchAliasResolver CreateBranchAliasResolver()
    {
        var taupo = new BranchMaster(new CanonicalBranchId("TAUPO"), "Taupo");
        var kerikeri = new BranchMaster(new CanonicalBranchId("KERIKERI"), "Kerikeri");
        var whangarei = new BranchMaster(new CanonicalBranchId("WHANGAREI"), "Whangarei");
        var junction = new BranchMaster(new CanonicalBranchId("JUNCTION"), "Mobil Junction");
        return new BranchAliasResolver(
            [taupo, kerikeri, whangarei, junction],
            [
                new BranchAlias("Mobil Taupo", taupo.Id),
                new BranchAlias("Mobile - Taupo", taupo.Id),
                new BranchAlias("Taupo", taupo.Id),
                new BranchAlias("Hertz Taupo", taupo.Id),
                new BranchAlias("HERTZ TAUPO", taupo.Id),
                new BranchAlias("Mobil Junction", junction.Id),
                new BranchAlias("Caltex Kerikeri", kerikeri.Id),
                new BranchAlias("Caltex Whangarei", whangarei.Id),
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
