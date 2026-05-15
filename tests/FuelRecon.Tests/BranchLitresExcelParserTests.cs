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
    public void Parse_skips_branch_only_noise_rows_without_litres_or_transactional_columns()
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
            sheet.Cell(2, 1).Value = "Taupo";
            sheet.Cell(2, 2).Value = "01/04/2026";
            sheet.Cell(2, 3).Value = "40";
            sheet.Cell(2, 4).Value = "RA-100";
            sheet.Cell(3, 1).Value = "Taupo";
            workbook.SaveAs(tempFile.Path);
        }

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.True(result.Success);
        Assert.Equal(1, result.RowCount);
        Assert.Equal(1, result.ValidRowCount);
        Assert.Empty(result.Issues);
        Assert.DoesNotContain(result.Issues, issue => issue.ReasonCode == LitresNormaliser.FailureReasonCode);
        Assert.Single(result.Entries);
    }

    [Fact]
    public void Parse_skips_row_when_litres_is_blank_after_trim_and_no_date_ra_rego_or_note()
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
            sheet.Cell(2, 1).Value = "Taupo";
            sheet.Cell(2, 2).Value = "02/04/2026";
            sheet.Cell(2, 3).Value = "11";
            sheet.Cell(2, 4).Value = "RA-200";
            sheet.Cell(3, 1).Value = "Taupo";
            sheet.Cell(3, 3).Value = "      ";
            workbook.SaveAs(tempFile.Path);
        }

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.True(result.Success);
        Assert.Equal(1, result.RowCount);
        Assert.Empty(result.Issues);
        Assert.DoesNotContain(result.Issues, issue => issue.ReasonCode == DateNormaliser.InvalidDateFormatReasonCode);
        Assert.Single(result.Entries);
        Assert.Equal(11m, result.Entries[0].Litres.Value);
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
    public void Parse_ignores_non_data_sheets_without_emitting_missing_column_errors_for_them()
    {
        using var tempFile = TemporaryExcelFile.Create();
        using (var workbook = new XLWorkbook())
        {
            var summary = workbook.Worksheets.Add("Monthly Summary");
            summary.Cell(1, 1).Value = "Noise";

            var data = workbook.Worksheets.Add("Fuel Data");
            data.Cell(1, 1).Value = "Branch #";
            data.Cell(1, 2).Value = "Fuel Date:";
            data.Cell(1, 3).Value = "Litres.";
            data.Cell(1, 4).Value = "RA #";
            data.Cell(1, 5).Value = "Plate!";
            data.Cell(1, 6).Value = "Memo";

            data.Cell(2, 1).Value = "Taupo";
            data.Cell(2, 2).Value = "01/04/2026";
            data.Cell(2, 3).Value = "12";
            data.Cell(2, 4).Value = "RA-77";
            data.Cell(2, 5).Value = "ab-12";
            data.Cell(2, 6).Value = "ok";

            workbook.SaveAs(tempFile.Path);
        }

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.True(result.Success);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("Fuel Data", entry.SourceReference.SheetName);
        Assert.Equal("TAUPO", entry.BranchId.Value);
        Assert.DoesNotContain(result.Issues, issue => issue.SourceReference?.SheetName == "Monthly Summary");
    }

    [Fact]
    public void Parse_skips_leading_metadata_rows_when_headers_are_on_row_four()
    {
        using var tempFile = TemporaryExcelFile.Create();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Taupo");
            sheet.Cell(1, 1).Value = "Internal fuel recon extract — confidential";
            sheet.Cell(2, 3).Value = "Prepared: Finance Ops";
            sheet.Cell(3, 2).Value = "Billing period April 2026";

            sheet.Cell(4, 1).Value = "Fuel Date";
            sheet.Cell(4, 2).Value = "Litres.";
            sheet.Cell(4, 3).Value = "Rental Agreement";

            sheet.Cell(5, 1).Value = "01/04/2026";
            sheet.Cell(5, 2).Value = "12.5";
            sheet.Cell(5, 3).Value = "RA-303";

            workbook.SaveAs(tempFile.Path);
        }

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateBranchAliasResolver());

        Assert.True(result.Success);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(5, entry.SourceReference.RowNumber);
        Assert.Equal("TAUPO", entry.BranchId.Value);
        Assert.Equal(new DateOnly(2026, 4, 1), entry.Date);
        Assert.Equal(12.5m, entry.Litres.Value);
        Assert.Equal("RA303", entry.RentalAgreementNumber?.NormalisedValue);
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
        Assert.Null(issue.SourceReference?.RowNumber);
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
        Assert.Contains(result.Issues, issue => issue.Severity == ValidationSeverity.Error);
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
        Assert.DoesNotContain(result.Issues, issue => issue.Severity == ValidationSeverity.Error);
        Assert.Contains(result.Issues, issue => issue.Severity == ValidationSeverity.Warning);

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

    [Fact]
    public void Parse_infers_branch_from_sheet_name_when_branch_column_is_absent()
    {
        using var tempFile = TemporaryExcelFile.Create();
        using (var workbook = new XLWorkbook())
        {
            void AddBranchSheet(string sheetName, string litresText)
            {
                var sheet = workbook.Worksheets.Add(sheetName);
                sheet.Cell(1, 1).Value = "Date";
                sheet.Cell(1, 2).Value = "Litres";
                sheet.Cell(1, 3).Value = "RA";
                sheet.Cell(2, 1).Value = "01/04/2026";
                sheet.Cell(2, 2).Value = litresText;
                sheet.Cell(2, 3).Value = "RA-1";
            }

            AddBranchSheet("Taupo", "10");
            AddBranchSheet("Kerikeri", "20");
            AddBranchSheet("Whangarei", "30");

            workbook.SaveAs(tempFile.Path);
        }

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateNorthIslandBranchAliasResolver());

        Assert.True(result.Success);
        Assert.DoesNotContain(result.Issues, issue => issue.Severity == ValidationSeverity.Error);
        Assert.Equal(3, result.Entries.Count);
        Assert.Single(result.Entries, entry => entry.SourceReference.SheetName == "Taupo" && entry.BranchId.Value == "TAUPO");
        Assert.Single(result.Entries, entry => entry.SourceReference.SheetName == "Kerikeri" && entry.BranchId.Value == "KERIKERI");
        Assert.Single(result.Entries, entry => entry.SourceReference.SheetName == "Whangarei" && entry.BranchId.Value == "WHANGAREI");
    }

    [Fact]
    public void Parse_defaults_date_when_note_provided_without_date_ra_or_rego_columns()
    {
        using var tempFile = TemporaryExcelFile.Create();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Whangarei");
            sheet.Cell(1, 1).Value = "Litres";
            sheet.Cell(1, 2).Value = "Memo";
            sheet.Cell(2, 1).Value = "8.5";
            sheet.Cell(2, 2).Value = "Ref ABC";
            workbook.SaveAs(tempFile.Path);
        }

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateNorthIslandBranchAliasResolver());

        Assert.True(result.Success);
        Assert.Single(result.Issues, issue => issue.ReasonCode == BranchLitresExcelParser.MissingDateDefaultedReasonCode);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("WHANGAREI", entry.BranchId.Value);
        Assert.Equal(new DateOnly(2026, 4, 1), entry.Date);
        Assert.Equal(8.5m, entry.Litres.Value);
        Assert.Null(entry.RentalAgreementNumber);
        Assert.Null(entry.Rego);
        Assert.Equal("Ref ABC", entry.NoteOrReference);
    }

    [Fact]
    public void Parse_weak_taupo_layout_reads_litres_column_after_standard_detection_fails()
    {
        using var tempFile = TemporaryExcelFile.Create();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Taupo");
            sheet.Cell(1, 1).Value = "When";
            sheet.Cell(1, 5).Value = "Litres";
            sheet.Cell(2, 1).Value = "01/04/2026";
            sheet.Cell(2, 2).Value = "NBM929";
            sheet.Cell(2, 5).Value = "52.25";
            workbook.SaveAs(tempFile.Path);
        }

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateNorthIslandBranchAliasResolver());

        Assert.True(result.Success);
        Assert.NotEmpty(result.Entries);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("TAUPO", entry.BranchId.Value);
        Assert.Equal(new DateOnly(2026, 4, 1), entry.Date);
        Assert.Equal(52.25m, entry.Litres.Value);
        Assert.NotNull(entry.Rego);
        Assert.Equal("NBM929", entry.Rego.NormalisedValue);
        Assert.Equal("Taupo", entry.SourceReference.SheetName);
        Assert.Equal(2, entry.SourceReference.RowNumber);
    }

    [Fact]
    public void Parse_weak_kerikeri_layout_treats_first_row_as_data_with_litres_at_column_three()
    {
        using var tempFile = TemporaryExcelFile.Create();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Kerikeri");
            sheet.Cell(1, 1).Value = 570226495;
            sheet.Cell(1, 2).Value = 7;
            sheet.Cell(1, 3).Value = 13.78;
            sheet.Cell(1, 4).Value = 47.78;
            sheet.Cell(1, 5).Value = 600;
            sheet.Cell(1, 6).Value = "01/04/2026";
            workbook.SaveAs(tempFile.Path);
        }

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateNorthIslandBranchAliasResolver());

        Assert.True(result.Success);
        Assert.NotEmpty(result.Entries);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("KERIKERI", entry.BranchId.Value);
        Assert.Equal(new DateOnly(2026, 4, 1), entry.Date);
        Assert.Equal(47.78m, entry.Litres.Value);
        Assert.NotNull(entry.RentalAgreementNumber);
        Assert.Equal("570226495", entry.RentalAgreementNumber.NormalisedValue);
        Assert.Equal("Kerikeri", entry.SourceReference.SheetName);
        Assert.Equal(1, entry.SourceReference.RowNumber);
    }

    [Fact]
    public void Parse_weak_whangarei_layout_reads_litres_and_date_from_headerless_row()
    {
        using var tempFile = TemporaryExcelFile.Create();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Whangarei");
            sheet.Cell(1, 1).Value = 6630;
            sheet.Cell(1, 2).Value = 8;
            sheet.Cell(1, 5).Value = 780034150;
            sheet.Cell(1, 6).Value = 1200;
            sheet.Cell(1, 7).Value = "01/04/2026";
            workbook.SaveAs(tempFile.Path);
        }

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateNorthIslandBranchAliasResolver());

        Assert.True(result.Success);
        Assert.NotEmpty(result.Entries);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("WHANGAREI", entry.BranchId.Value);
        Assert.Equal(1200m, entry.Litres.Value);
        Assert.Equal(new DateOnly(2026, 4, 1), entry.Date);
        Assert.Equal("Whangarei", entry.SourceReference.SheetName);
        Assert.Equal(1, entry.SourceReference.RowNumber);
    }

    [Fact]
    public void Parse_reports_branch_resolution_error_per_row_when_branch_column_absent_and_sheet_name_not_recognised()
    {
        using var tempFile = TemporaryExcelFile.Create();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Atlantis");
            sheet.Cell(1, 1).Value = "Date";
            sheet.Cell(1, 2).Value = "Litres";
            sheet.Cell(1, 3).Value = "RA";
            sheet.Cell(2, 1).Value = "01/04/2026";
            sheet.Cell(2, 2).Value = "5";
            sheet.Cell(2, 3).Value = "RA-9";
            workbook.SaveAs(tempFile.Path);
        }

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateNorthIslandBranchAliasResolver());

        Assert.False(result.Success);
        Assert.Empty(result.Entries);
        Assert.Equal(1, result.RowCount);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(ValidationSeverity.Error, issue.Severity);
        Assert.Equal(BranchAliasResolver.BranchAliasNotFoundReasonCode, issue.ReasonCode);
        Assert.Contains("Atlantis", issue.Message, StringComparison.Ordinal);
        Assert.Equal("Atlantis", issue.SourceReference?.SheetName);
        Assert.Equal(2, issue.SourceReference?.RowNumber);
    }

    [Fact]
    public void Parse_prefers_branch_column_value_over_sheet_name_when_branch_column_exists()
    {
        using var tempFile = TemporaryExcelFile.Create();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Kerikeri");
            sheet.Cell(1, 1).Value = "Branch";
            sheet.Cell(1, 2).Value = "Date";
            sheet.Cell(1, 3).Value = "Litres";
            sheet.Cell(1, 4).Value = "RA";
            sheet.Cell(2, 1).Value = "Taupo";
            sheet.Cell(2, 2).Value = "01/04/2026";
            sheet.Cell(2, 3).Value = "15";
            sheet.Cell(2, 4).Value = "RA-2";
            workbook.SaveAs(tempFile.Path);
        }

        var result = CreateParser().Parse(tempFile.Path, new FuelPeriod(2026, 4), CreateNorthIslandBranchAliasResolver());

        Assert.True(result.Success);
        var entry = Assert.Single(result.Entries);
        Assert.Equal("TAUPO", entry.BranchId.Value);
        Assert.Equal("Kerikeri", entry.SourceReference.SheetName);
        Assert.Equal(15m, entry.Litres.Value);
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

    private static BranchAliasResolver CreateNorthIslandBranchAliasResolver()
    {
        var taupo = new BranchMaster(new CanonicalBranchId("TAUPO"), "Taupo");
        var kerikeri = new BranchMaster(new CanonicalBranchId("KERIKERI"), "Kerikeri");
        var whangarei = new BranchMaster(new CanonicalBranchId("WHANGAREI"), "Whangarei");

        return new BranchAliasResolver(
            [taupo, kerikeri, whangarei],
            [
                new BranchAlias("Taupo", taupo.Id),
                new BranchAlias("Kerikeri", kerikeri.Id),
                new BranchAlias("Whangarei", whangarei.Id),
                new BranchAlias("Mobil Taupo", taupo.Id),
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
