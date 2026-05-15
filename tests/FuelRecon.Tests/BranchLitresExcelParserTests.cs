using ClosedXML.Excel;
using FuelRecon.Domain;
using FuelRecon.Infrastructure.Excel;

namespace FuelRecon.Tests;

public class BranchLitresExcelParserTests
{
    [Fact]
    public void Parse_reads_valid_branch_litres_rows_and_maps_aliases()
    {
        using var tempFile = TemporaryExcelFile.Create();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Branch Litres");
            sheet.Cell(1, 1).Value = "Branch";
            sheet.Cell(1, 2).Value = "Fuel Date";
            sheet.Cell(1, 3).Value = "Litres";
            sheet.Cell(1, 4).Value = "Rental Agreement";
            sheet.Cell(1, 5).Value = "Registration";
            sheet.Cell(1, 6).Value = "Notes";
            sheet.Cell(2, 1).Value = "Mobil Taupo";
            sheet.Cell(2, 2).Value = "01/04/2026";
            sheet.Cell(2, 3).Value = "42.125";
            sheet.Cell(2, 4).Value = " RA-123 ";
            sheet.Cell(2, 5).Value = "abc-123";
            sheet.Cell(2, 6).Value = " customer note ";
            sheet.Cell(3, 1).Value = "Mobile - Taupo";
            sheet.Cell(3, 2).Value = "2 Apr 2026";
            sheet.Cell(3, 3).Value = "10,25";
            sheet.Cell(3, 4).Value = "RA-456";
            workbook.SaveAs(tempFile.Path);
        }

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.True(result.Success);
        Assert.Equal(2, result.RowCount);
        Assert.Equal(2, result.ValidRowCount);
        Assert.Equal(0, result.SkippedRowCount);
        Assert.Empty(result.Issues);
        Assert.Equal(2, result.Entries.Count);

        var first = result.Entries[0];
        Assert.Equal("TAUPO", first.BranchId.Value);
        Assert.Equal(new DateOnly(2026, 4, 1), first.Date);
        Assert.Equal(42.13m, first.Litres.Value);
        Assert.Equal(" RA-123 ", first.RentalAgreementNumber?.RawValue);
        Assert.Equal("RA123", first.RentalAgreementNumber?.NormalisedValue);
        Assert.Equal("abc-123", first.Rego?.RawValue);
        Assert.Equal("ABC123", first.Rego?.NormalisedValue);
        Assert.Equal("customer note", first.NoteOrReference);
        Assert.Equal(tempFile.Path, first.SourceReference.SourceFile);
        Assert.Equal("Branch Litres", first.SourceReference.SheetName);
        Assert.Equal(2, first.SourceReference.RowNumber);

        var second = result.Entries[1];
        Assert.Equal("TAUPO", second.BranchId.Value);
        Assert.Equal(new DateOnly(2026, 4, 2), second.Date);
        Assert.Equal(10.25m, second.Litres.Value);
    }

    [Fact]
    public void Parse_supports_header_variants()
    {
        using var tempFile = TemporaryExcelFile.Create();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Variants");
            sheet.Cell(1, 1).Value = "Depot";
            sheet.Cell(1, 2).Value = "Transaction Date";
            sheet.Cell(1, 3).Value = "Qty";
            sheet.Cell(1, 4).Value = "Plate";
            sheet.Cell(1, 5).Value = "Reference";
            sheet.Cell(2, 1).Value = "Taupo";
            sheet.Cell(2, 2).Value = "2026-04-03";
            sheet.Cell(2, 3).Value = "9.5";
            sheet.Cell(2, 4).Value = "xy-999";
            sheet.Cell(2, 5).Value = "ref";
            workbook.SaveAs(tempFile.Path);
        }

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.True(result.Success);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("TAUPO", entry.BranchId.Value);
        Assert.Equal(new DateOnly(2026, 4, 3), entry.Date);
        Assert.Equal(9.5m, entry.Litres.Value);
        Assert.Equal("XY999", entry.Rego?.NormalisedValue);
        Assert.Equal("ref", entry.NoteOrReference);
    }

    [Fact]
    public void Parse_reports_missing_required_columns()
    {
        using var tempFile = TemporaryExcelFile.Create();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Missing");
            sheet.Cell(1, 1).Value = "Branch";
            sheet.Cell(1, 2).Value = "Date";
            sheet.Cell(2, 1).Value = "Taupo";
            sheet.Cell(2, 2).Value = "01/04/2026";
            workbook.SaveAs(tempFile.Path);
        }

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.False(result.Success);
        Assert.Empty(result.Entries);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(ValidationSeverity.Error, issue.Severity);
        Assert.Equal(BranchLitresExcelParser.MissingRequiredColumnsReasonCode, issue.ReasonCode);
        Assert.Equal(tempFile.Path, issue.SourceReference?.SourceFile);
        Assert.Equal("Missing", issue.SourceReference?.SheetName);
        Assert.Equal(1, issue.SourceReference?.RowNumber);
    }

    [Fact]
    public void Parse_reports_invalid_row_values_without_crashing_full_parse()
    {
        using var tempFile = TemporaryExcelFile.Create();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Rows");
            sheet.Cell(1, 1).Value = "Location";
            sheet.Cell(1, 2).Value = "Date";
            sheet.Cell(1, 3).Value = "L";
            sheet.Cell(1, 4).Value = "RA";
            sheet.Cell(1, 5).Value = "Rego";
            sheet.Cell(2, 1).Value = "Taupo";
            sheet.Cell(2, 2).Value = "01/04/2026";
            sheet.Cell(2, 3).Value = "12.5";
            sheet.Cell(2, 4).Value = "RA-1";
            sheet.Cell(2, 5).Value = "abc123";
            sheet.Cell(3, 1).Value = "Unknown Branch";
            sheet.Cell(3, 2).Value = "not a date";
            sheet.Cell(3, 3).Value = "abc";
            sheet.Cell(3, 4).Value = " - . - ";
            sheet.Cell(3, 5).Value = "---";
            workbook.SaveAs(tempFile.Path);
        }

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.True(result.Success);
        Assert.Equal(2, result.RowCount);
        Assert.Equal(1, result.ValidRowCount);
        Assert.Equal(1, result.SkippedRowCount);
        Assert.Single(result.Entries);
        Assert.Contains(result.Issues, issue => issue.ReasonCode == BranchAliasResolver.BranchAliasNotFoundReasonCode);
        Assert.Contains(result.Issues, issue => issue.ReasonCode == DateNormaliser.InvalidDateFormatReasonCode);
        Assert.Contains(result.Issues, issue => issue.ReasonCode == LitresNormaliser.FailureReasonCode);
        Assert.Contains(result.Issues, issue => issue.ReasonCode == RentalAgreementNormaliser.InvalidReasonCode);
        Assert.Contains(result.Issues, issue => issue.ReasonCode == RegoNormaliser.InvalidReasonCode);
        Assert.All(result.Issues, issue => Assert.Equal(3, issue.SourceReference?.RowNumber));
    }

    [Fact]
    public void Parse_allows_missing_date_when_identifier_or_reference_exists_and_reports_warning()
    {
        using var tempFile = TemporaryExcelFile.Create();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("No Date");
            sheet.Cell(1, 1).Value = "Branch";
            sheet.Cell(1, 2).Value = "Litres";
            sheet.Cell(1, 3).Value = "RA";
            sheet.Cell(2, 1).Value = "Taupo";
            sheet.Cell(2, 2).Value = "5";
            sheet.Cell(2, 3).Value = "RA-99";
            workbook.SaveAs(tempFile.Path);
        }

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.True(result.Success);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(new DateOnly(2026, 4, 1), entry.Date);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(ValidationSeverity.Warning, issue.Severity);
        Assert.Equal(BranchLitresExcelParser.MissingDateDefaultedReasonCode, issue.ReasonCode);
        Assert.Equal([BranchLitresExcelParser.MissingDateDefaultedReasonCode], entry.ValidationIssueCodes);
    }

    [Fact]
    public void Parse_wraps_workbook_reader_failure_as_parser_issue()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.xlsx");

        var result = CreateParser().Parse(path, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.False(result.Success);
        Assert.Empty(result.Entries);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(ClosedXmlExcelWorkbookReader.ExcelFileNotFoundReasonCode, issue.ReasonCode);
    }

    private static BranchLitresExcelParser CreateParser() =>
        new(new ClosedXmlExcelWorkbookReader());

    private static BranchAliasResolver CreateBranchAliasResolver()
    {
        var taupo = new BranchMaster(new CanonicalBranchId("TAUPO"), "Taupo");
        return new BranchAliasResolver(
            [taupo],
            [
                new BranchAlias("Mobil Taupo", taupo.Id),
                new BranchAlias("Mobile - Taupo", taupo.Id),
                new BranchAlias("Hertz Taupo", taupo.Id),
                new BranchAlias("Taupo", taupo.Id),
            ]);
    }

    private sealed class TemporaryExcelFile : IDisposable
    {
        private TemporaryExcelFile(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryExcelFile Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"fuelrecon-branch-litres-{Guid.NewGuid():N}.xlsx");

            return new TemporaryExcelFile(path);
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
