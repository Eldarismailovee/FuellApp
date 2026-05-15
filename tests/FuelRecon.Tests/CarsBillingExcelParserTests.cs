using ClosedXML.Excel;
using FuelRecon.Domain;
using FuelRecon.Infrastructure.Excel;

namespace FuelRecon.Tests;

public class CarsBillingExcelParserTests
{
    [Fact]
    public void Parse_reads_client_cars_plus_statement_sample_when_present()
    {
        var path = Path.Combine("samples", "client-raw", "cars+ statement.xlsx");
        if (!File.Exists(path))
        {
            return;
        }

        var result = CreateParser().Parse(path, new FuelPeriod(2026, 4), CreateSampleBranchAliasResolver());

        Assert.True(result.Success);
        Assert.True(result.RowCount > 0);
        Assert.True(result.Entries.Count > 0);
        Assert.All(result.Entries, entry => Assert.NotNull(entry.RentalAgreementNumber));
        Assert.Contains(result.Entries, entry => entry.BilledAmount is not null);
        Assert.Contains(result.Entries, entry => entry.BillingStatus is not null);
    }

    [Fact]
    public void Parse_reads_valid_cars_billing_rows_and_maps_branch_aliases()
    {
        using var tempFile = TemporaryExcelFile.Create();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Cars Export");
            sheet.Cell(1, 1).Value = "Branch";
            sheet.Cell(1, 2).Value = "Billing Date";
            sheet.Cell(1, 3).Value = "Rental Agreement";
            sheet.Cell(1, 4).Value = "Registration";
            sheet.Cell(1, 5).Value = "Billed Litres";
            sheet.Cell(1, 6).Value = "Billed Amount";
            sheet.Cell(1, 7).Value = "Billing Status";
            sheet.Cell(2, 1).Value = "Mobil Taupo";
            sheet.Cell(2, 2).Value = "01/04/2026";
            sheet.Cell(2, 3).Value = " RA-123 ";
            sheet.Cell(2, 4).Value = "abc-123";
            sheet.Cell(2, 5).Value = "42.125";
            sheet.Cell(2, 6).Value = "$123.455";
            sheet.Cell(2, 7).Value = " Closed ";
            sheet.Cell(3, 1).Value = "Mobile - Taupo";
            sheet.Cell(3, 2).Value = "April 2026";
            sheet.Cell(3, 3).Value = "RA-456";
            sheet.Cell(3, 5).Value = "10,25";
            sheet.Cell(3, 7).Value = "Open";
            workbook.SaveAs(tempFile.Path);
        }

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.True(result.Success);
        Assert.False(result.HasErrors);
        Assert.Equal(2, result.RowCount);
        Assert.Equal(2, result.ValidRowCount);
        Assert.Equal(0, result.SkippedRowCount);
        Assert.Empty(result.Issues);
        Assert.Equal(2, result.Entries.Count);

        var first = result.Entries[0];
        Assert.Equal("TAUPO", first.BranchId?.Value);
        Assert.Equal(new DateOnly(2026, 4, 1), first.Date);
        Assert.Equal(" RA-123 ", first.RentalAgreementNumber?.RawValue);
        Assert.Equal("RA123", first.RentalAgreementNumber?.NormalisedValue);
        Assert.Equal("abc-123", first.Rego?.RawValue);
        Assert.Equal("ABC123", first.Rego?.NormalisedValue);
        Assert.Equal(42.13m, first.BilledLitres?.Value);
        Assert.Equal(123.46m, first.BilledAmount?.Value);
        Assert.Equal("Closed", first.BillingStatus);
        Assert.Equal(tempFile.Path, first.SourceReference.SourceFile);
        Assert.Equal("Cars Export", first.SourceReference.SheetName);
        Assert.Equal(2, first.SourceReference.RowNumber);

        var second = result.Entries[1];
        Assert.Equal("TAUPO", second.BranchId?.Value);
        Assert.Equal(new DateOnly(2026, 4, 1), second.Date);
        Assert.Equal("RA456", second.RentalAgreementNumber?.NormalisedValue);
        Assert.Null(second.Rego);
        Assert.Equal(10.25m, second.BilledLitres?.Value);
        Assert.Null(second.BilledAmount);
        Assert.Equal("Open", second.BillingStatus);
    }

    [Fact]
    public void Parse_resolves_branch_from_sheet_name_when_branch_column_is_missing()
    {
        using var tempFile = TemporaryExcelFile.Create();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Taupo");
            sheet.Cell(1, 1).Value = "RA Number";
            sheet.Cell(1, 2).Value = "Date In";
            sheet.Cell(1, 3).Value = "Fuel Charges";
            sheet.Cell(1, 4).Value = "FUEL";
            sheet.Cell(2, 1).Value = "570221934";
            sheet.Cell(2, 2).Value = "46112";
            sheet.Cell(2, 3).Value = "23.32";
            sheet.Cell(2, 4).Value = "FSC";
            workbook.SaveAs(tempFile.Path);
        }

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateSampleBranchAliasResolver());

        Assert.True(result.Success);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("TAUPO", entry.BranchId?.Value);
        Assert.Equal(new DateOnly(2026, 4, 1), entry.Date);
        Assert.Equal("570221934", entry.RentalAgreementNumber?.RawValue);
        Assert.Equal(23.32m, entry.BilledAmount?.Value);
        Assert.Equal("FSC", entry.BillingStatus);
    }

    [Fact]
    public void Parse_skips_report_total_footer_row_without_errors()
    {
        using var tempFile = TemporaryExcelFile.Create();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("scratch");
            sheet.Cell(1, 1).Value = "RA Number";
            sheet.Cell(1, 2).Value = "Time In";
            sheet.Cell(1, 3).Value = "Fuel Charges";
            sheet.Cell(2, 1).Value = "570221934";
            sheet.Cell(2, 2).Value = "1500";
            sheet.Cell(2, 3).Value = "23.32";
            sheet.Cell(3, 2).Value = "REPORT TOTAL";
            sheet.Cell(3, 3).Value = "49754.23";
            workbook.SaveAs(tempFile.Path);
        }

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.True(result.Success);
        Assert.False(result.HasErrors);
        Assert.Single(result.Entries);
        Assert.Equal(23.32m, result.Entries[0].BilledAmount?.Value);
    }

    [Fact]
    public void Parse_supports_header_variants_and_optional_branch_and_date()
    {
        using var tempFile = TemporaryExcelFile.Create();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Variants");
            sheet.Cell(1, 1).Value = "Depot";
            sheet.Cell(1, 2).Value = "Period";
            sheet.Cell(1, 3).Value = "RA";
            sheet.Cell(1, 4).Value = "Plate";
            sheet.Cell(1, 5).Value = "Qty";
            sheet.Cell(1, 6).Value = "Charge";
            sheet.Cell(1, 7).Value = "Invoice Status";
            sheet.Cell(2, 1).Value = "Taupo";
            sheet.Cell(2, 2).Value = "April 2026";
            sheet.Cell(2, 3).Value = "RA-99";
            sheet.Cell(2, 4).Value = "xy-999";
            sheet.Cell(2, 5).Value = "5";
            sheet.Cell(2, 6).Value = "$12.30";
            sheet.Cell(2, 7).Value = "Invoiced";
            sheet.Cell(3, 3).Value = "RA-100";
            sheet.Cell(3, 6).Value = "$1.23";
            workbook.SaveAs(tempFile.Path);
        }

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.True(result.Success);
        Assert.Equal(2, result.Entries.Count);

        var first = result.Entries[0];
        Assert.Equal("TAUPO", first.BranchId?.Value);
        Assert.Equal(new DateOnly(2026, 4, 1), first.Date);
        Assert.Equal("RA99", first.RentalAgreementNumber?.NormalisedValue);
        Assert.Equal("XY999", first.Rego?.NormalisedValue);
        Assert.Equal(5m, first.BilledLitres?.Value);
        Assert.Equal(12.30m, first.BilledAmount?.Value);
        Assert.Equal("Invoiced", first.BillingStatus);

        var second = result.Entries[1];
        Assert.Null(second.BranchId);
        Assert.Null(second.Date);
        Assert.Equal("RA100", second.RentalAgreementNumber?.NormalisedValue);
        Assert.Null(second.Rego);
        Assert.Null(second.BilledLitres);
        Assert.Equal(1.23m, second.BilledAmount?.Value);
        Assert.Null(second.BillingStatus);
        Assert.Equal(3, second.SourceReference.RowNumber);
    }

    [Fact]
    public void Parse_ignores_totals_sheets_and_matches_headers_when_labels_include_punctuation()
    {
        using var tempFile = TemporaryExcelFile.Create();
        using (var workbook = new XLWorkbook())
        {
            var totals = workbook.Worksheets.Add("Totals");
            totals.Cell(1, 1).Value = "Ignore";

            var sheet = workbook.Worksheets.Add("Cars Data");
            sheet.Cell(1, 1).Value = "Site:";
            sheet.Cell(1, 2).Value = "Inv Date!";
            sheet.Cell(1, 3).Value = "Agreement #";
            sheet.Cell(1, 4).Value = "Plate.";
            sheet.Cell(1, 5).Value = "Qty:";
            sheet.Cell(1, 6).Value = "Charge";
            sheet.Cell(1, 7).Value = "Bill Status";

            sheet.Cell(2, 1).Value = "Taupo";
            sheet.Cell(2, 2).Value = "01/04/2026";
            sheet.Cell(2, 3).Value = "RA-9";
            sheet.Cell(2, 4).Value = "zz-88";
            sheet.Cell(2, 5).Value = "6";
            sheet.Cell(2, 6).Value = "$12.34";
            sheet.Cell(2, 7).Value = "Open";

            workbook.SaveAs(tempFile.Path);
        }

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.True(result.Success);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("Cars Data", entry.SourceReference.SheetName);
        Assert.Equal("TAUPO", entry.BranchId?.Value);
        Assert.Equal(6m, entry.BilledLitres?.Value);
        Assert.Equal(12.34m, entry.BilledAmount?.Value);
        Assert.Equal("Open", entry.BillingStatus);
        Assert.DoesNotContain(result.Issues, issue => issue.SourceReference?.SheetName == "Totals");
    }

    [Fact]
    public void Parse_skips_leading_metadata_rows_when_headers_are_on_row_four()
    {
        using var tempFile = TemporaryExcelFile.Create();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Cars Detail");
            sheet.Cell(1, 1).Value = "Cars+ billing recon — internal only";
            sheet.Cell(2, 2).Value = "Exported by AP";
            sheet.Cell(3, 1).Value = "Confidential metadata row";

            sheet.Cell(4, 1).Value = "RA";
            sheet.Cell(4, 2).Value = "Charge";

            sheet.Cell(5, 1).Value = "RA-707";
            sheet.Cell(5, 2).Value = "$99.10";

            workbook.SaveAs(tempFile.Path);
        }

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.True(result.Success);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(5, entry.SourceReference.RowNumber);
        Assert.Null(entry.BranchId);
        Assert.Equal("RA707", entry.RentalAgreementNumber?.NormalisedValue);
        Assert.Equal(99.10m, entry.BilledAmount?.Value);
    }

    [Fact]
    public void Parse_reports_missing_required_columns()
    {
        using var tempFile = TemporaryExcelFile.Create();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Missing");
            sheet.Cell(1, 1).Value = "Branch";
            sheet.Cell(1, 2).Value = "Amount";
            sheet.Cell(2, 1).Value = "Taupo";
            sheet.Cell(2, 2).Value = "$10.00";
            workbook.SaveAs(tempFile.Path);
        }

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.False(result.Success);
        Assert.True(result.HasErrors);
        Assert.Empty(result.Entries);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(ValidationSeverity.Error, issue.Severity);
        Assert.Equal(CarsBillingExcelParser.MissingRequiredColumnsReasonCode, issue.ReasonCode);
        Assert.Equal(tempFile.Path, issue.SourceReference?.SourceFile);
        Assert.Equal("Missing", issue.SourceReference?.SheetName);
        Assert.Null(issue.SourceReference?.RowNumber);
    }

    [Fact]
    public void Parse_reports_invalid_row_level_values_without_crashing_full_parse()
    {
        using var tempFile = TemporaryExcelFile.Create();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Rows");
            sheet.Cell(1, 1).Value = "Location";
            sheet.Cell(1, 2).Value = "Date";
            sheet.Cell(1, 3).Value = "RA";
            sheet.Cell(1, 4).Value = "Rego";
            sheet.Cell(1, 5).Value = "Litres";
            sheet.Cell(1, 6).Value = "Amount";
            sheet.Cell(1, 7).Value = "Status";
            sheet.Cell(2, 1).Value = "Taupo";
            sheet.Cell(2, 2).Value = "01/04/2026";
            sheet.Cell(2, 3).Value = "RA-1";
            sheet.Cell(2, 5).Value = "12.5";
            sheet.Cell(3, 1).Value = "Unknown Branch";
            sheet.Cell(3, 2).Value = "not a date";
            sheet.Cell(3, 3).Value = "---";
            sheet.Cell(3, 4).Value = "---";
            sheet.Cell(3, 5).Value = "abc";
            sheet.Cell(3, 6).Value = "$abc";
            workbook.SaveAs(tempFile.Path);
        }

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.True(result.Success);
        Assert.True(result.HasErrors);
        Assert.Equal(2, result.RowCount);
        Assert.Equal(1, result.ValidRowCount);
        Assert.Equal(1, result.SkippedRowCount);
        Assert.Single(result.Entries);
        Assert.Contains(result.Issues, issue => issue.ReasonCode == BranchAliasResolver.BranchAliasNotFoundReasonCode);
        Assert.Contains(result.Issues, issue => issue.ReasonCode == DateNormaliser.InvalidDateFormatReasonCode);
        Assert.Contains(result.Issues, issue => issue.ReasonCode == RentalAgreementNormaliser.InvalidReasonCode);
        Assert.Contains(result.Issues, issue => issue.ReasonCode == RegoNormaliser.InvalidReasonCode);
        Assert.Contains(result.Issues, issue => issue.ReasonCode == LitresNormaliser.FailureReasonCode);
        Assert.Contains(result.Issues, issue => issue.ReasonCode == MoneyAmountNormaliser.FailureReasonCode);
        Assert.Contains(result.Issues, issue => issue.ReasonCode == CarsBillingExcelParser.MissingIdentifierReasonCode);
        Assert.Contains(result.Issues, issue => issue.ReasonCode == CarsBillingExcelParser.MissingBillingValueReasonCode);
        Assert.All(result.Issues, issue => Assert.Equal(3, issue.SourceReference?.RowNumber));
    }

    [Fact]
    public void Parse_preserves_source_row_reference()
    {
        using var tempFile = TemporaryExcelFile.Create();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Source");
            sheet.Cell(4, 2).Value = "RA";
            sheet.Cell(4, 3).Value = "Total";
            sheet.Cell(7, 2).Value = "RA-777";
            sheet.Cell(7, 3).Value = "$77.70";
            workbook.SaveAs(tempFile.Path);
        }

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.True(result.Success);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(tempFile.Path, entry.SourceReference.SourceFile);
        Assert.Equal("Source", entry.SourceReference.SheetName);
        Assert.Equal(7, entry.SourceReference.RowNumber);
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

    private static CarsBillingExcelParser CreateParser() =>
        new(new ClosedXmlExcelWorkbookReader());

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

    private static BranchAliasResolver CreateSampleBranchAliasResolver()
    {
        var taupo = new BranchMaster(new CanonicalBranchId("TAUPO"), "Taupo");
        var kerikeri = new BranchMaster(new CanonicalBranchId("KERIKERI"), "Kerikeri");
        var whangarei = new BranchMaster(new CanonicalBranchId("WHANGAREI"), "Whangarei");

        return new BranchAliasResolver(
            [taupo, kerikeri, whangarei],
            [
                new BranchAlias("Taupo", taupo.Id),
                new BranchAlias("Mobil Taupo", taupo.Id),
                new BranchAlias("Mobile - Taupo", taupo.Id),
                new BranchAlias("Hertz Taupo", taupo.Id),
                new BranchAlias("Caltex Kerikeri", kerikeri.Id),
                new BranchAlias("Caltex Whangarei", whangarei.Id),
                new BranchAlias("Kerikeri", kerikeri.Id),
                new BranchAlias("Whangarei", whangarei.Id),
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
                $"fuelrecon-cars-billing-{Guid.NewGuid():N}.xlsx");

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
