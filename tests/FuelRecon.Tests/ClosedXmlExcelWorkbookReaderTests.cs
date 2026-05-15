using ClosedXML.Excel;
using FuelRecon.Infrastructure.Excel;

namespace FuelRecon.Tests;

public class ClosedXmlExcelWorkbookReaderTests
{
    [Fact]
    public void ReadWorkbook_opens_simple_xlsx_file_and_returns_sheet_headers_and_rows()
    {
        using var tempFile = TemporaryExcelFile.Create(".xlsx");
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Fuel Data");
            sheet.Cell(1, 1).Value = " Branch ";
            sheet.Cell(1, 2).Value = "Litres";
            sheet.Cell(1, 3).Value = " Notes ";
            sheet.Cell(2, 1).Value = "  Taupo  ";
            sheet.Cell(2, 2).Value = 42.5;
            sheet.Cell(2, 3).Value = " raw note ";
            sheet.Cell(4, 1).Value = "Rotorua";
            sheet.Cell(4, 2).Value = 10;
            workbook.SaveAs(tempFile.Path);
        }

        var reader = new ClosedXmlExcelWorkbookReader();

        var result = reader.ReadWorkbook(tempFile.Path);

        Assert.True(result.Success);
        Assert.Null(result.ReasonCode);
        Assert.NotNull(result.Workbook);
        Assert.Equal(tempFile.Path, result.Workbook.SourceFile);
        var sheetModel = Assert.Single(result.Workbook.Sheets);
        Assert.Equal("Fuel Data", sheetModel.SheetName);
        Assert.Equal(1, sheetModel.HeaderRowNumber);
        Assert.Equal(["Branch", "Litres", "Notes"], sheetModel.Headers);
        Assert.Equal(2, sheetModel.Rows.Count);

        var firstRow = sheetModel.Rows[0];
        Assert.Equal(2, firstRow.RowNumber);
        Assert.Equal(tempFile.Path, firstRow.SourceFile);
        Assert.Equal("Fuel Data", firstRow.SheetName);
        Assert.Equal("  Taupo  ", firstRow.Cells[0].RawText);
        Assert.Equal("Branch", firstRow.Cells[0].Header);
        Assert.Equal(1, firstRow.Cells[0].ColumnNumber);
        Assert.Equal("42.5", firstRow.Cells[1].RawText);
        Assert.Equal(" raw note ", firstRow.Cells[2].RawText);

        var secondRow = sheetModel.Rows[1];
        Assert.Equal(4, secondRow.RowNumber);
        Assert.Equal("Rotorua", secondRow.Cells[0].RawText);
    }

    [Fact]
    public void ReadWorkbook_returns_all_sheets_with_sheet_names()
    {
        using var tempFile = TemporaryExcelFile.Create(".xlsx");
        using (var workbook = new XLWorkbook())
        {
            var branchSheet = workbook.Worksheets.Add("Branch");
            branchSheet.Cell(1, 1).Value = "Header";
            branchSheet.Cell(2, 1).Value = "Value";

            var carsSheet = workbook.Worksheets.Add("Cars Export");
            carsSheet.Cell(1, 1).Value = "RA";
            carsSheet.Cell(2, 1).Value = "123";

            workbook.SaveAs(tempFile.Path);
        }

        var result = new ClosedXmlExcelWorkbookReader().ReadWorkbook(tempFile.Path);

        Assert.True(result.Success);
        Assert.NotNull(result.Workbook);
        Assert.Equal(2, result.Workbook.Sheets.Count);
        Assert.Contains(result.Workbook.Sheets, sheet => sheet.SheetName == "Branch");
        Assert.Contains(result.Workbook.Sheets, sheet => sheet.SheetName == "Cars Export");
    }

    [Fact]
    public void ReadWorkbook_skips_blank_rows_but_preserves_actual_data_row_numbers()
    {
        using var tempFile = TemporaryExcelFile.Create(".xlsx");
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.Worksheets.Add("Rows");
            sheet.Cell(3, 2).Value = "Name";
            sheet.Cell(5, 2).Value = "Taupo";
            workbook.SaveAs(tempFile.Path);
        }

        var result = new ClosedXmlExcelWorkbookReader().ReadWorkbook(tempFile.Path);

        Assert.True(result.Success);
        Assert.NotNull(result.Workbook);
        var sheetModel = Assert.Single(result.Workbook.Sheets);
        Assert.Equal(3, sheetModel.HeaderRowNumber);
        var row = Assert.Single(sheetModel.Rows);
        Assert.Equal(5, row.RowNumber);
        var cell = Assert.Single(row.Cells);
        Assert.Equal(2, cell.ColumnNumber);
        Assert.Equal("Taupo", cell.RawText);
    }

    [Theory]
    [InlineData(".xls")]
    [InlineData(".csv")]
    [InlineData(".xlsb")]
    public void ReadWorkbook_rejects_non_xlsx_extensions(string extension)
    {
        using var tempFile = TemporaryExcelFile.Create(extension);
        File.WriteAllText(tempFile.Path, "not an xlsx workbook");

        var result = new ClosedXmlExcelWorkbookReader().ReadWorkbook(tempFile.Path);

        Assert.False(result.Success);
        Assert.Null(result.Workbook);
        Assert.Equal(ClosedXmlExcelWorkbookReader.UnsupportedExcelFormatReasonCode, result.ReasonCode);
        Assert.Contains(".xlsx", result.Message);
    }

    [Fact]
    public void ReadWorkbook_returns_clear_error_for_missing_file()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.xlsx");

        var result = new ClosedXmlExcelWorkbookReader().ReadWorkbook(path);

        Assert.False(result.Success);
        Assert.Null(result.Workbook);
        Assert.Equal(ClosedXmlExcelWorkbookReader.ExcelFileNotFoundReasonCode, result.ReasonCode);
        Assert.Contains("not found", result.Message);
    }

    [Fact]
    public void ReadWorkbook_returns_clear_error_for_corrupted_xlsx_file()
    {
        using var tempFile = TemporaryExcelFile.Create(".xlsx");
        File.WriteAllText(tempFile.Path, "this is not a valid xlsx zip package");

        var result = new ClosedXmlExcelWorkbookReader().ReadWorkbook(tempFile.Path);

        Assert.False(result.Success);
        Assert.Null(result.Workbook);
        Assert.Equal(ClosedXmlExcelWorkbookReader.ExcelReadFailedReasonCode, result.ReasonCode);
        Assert.Contains("could not be read", result.Message);
    }

    private sealed class TemporaryExcelFile : IDisposable
    {
        private TemporaryExcelFile(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryExcelFile Create(string extension)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"fuelrecon-excel-{Guid.NewGuid():N}{extension}");

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
