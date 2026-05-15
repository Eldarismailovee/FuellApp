using System.Text;
using FuelRecon.Infrastructure.Excel;

namespace FuelRecon.GoldenFiles;

public class ExcelWorkbookLayoutDiagnosticsTests
{
    private static readonly string[] SampleRelativePaths =
    [
        Path.Combine("samples", "client-raw", "branch litres.xlsx"),
        Path.Combine("samples", "client-raw", "cars+ statement.xlsx"),
    ];

    [Fact]
    public void Write_excel_layout_diagnostics_for_available_client_samples()
    {
        var repositoryRoot = FindRepositoryRoot();
        var outputDirectory = Path.Combine(repositoryRoot, "artifacts", "excel-layouts");
        Directory.CreateDirectory(outputDirectory);

        var reader = new ClosedXmlExcelWorkbookReader();

        foreach (var relativePath in SampleRelativePaths)
        {
            var samplePath = Path.Combine(repositoryRoot, relativePath);
            if (!File.Exists(samplePath))
            {
                continue;
            }

            var result = reader.ReadWorkbook(samplePath);
            Assert.True(result.Success, result.Message);
            Assert.NotNull(result.Workbook);

            var builder = new StringBuilder();
            builder.AppendLine($"SourceFile: {samplePath}");
            builder.AppendLine();

            foreach (var sheet in result.Workbook.Sheets)
            {
                var skipped = ExcelSheetFilter.IsNonDataSheet(sheet.SheetName);
                builder.AppendLine($"Sheet: {sheet.SheetName}");
                builder.AppendLine($"NonDataSheetFiltered: {skipped}");
                builder.AppendLine($"HeaderRowNumber: {sheet.HeaderRowNumber}");
                builder.AppendLine($"Headers ({sheet.Headers.Count}):");
                for (var index = 0; index < sheet.Headers.Count; index++)
                {
                    builder.AppendLine($"  [{index}] {sheet.Headers[index]}");
                }

                builder.AppendLine($"DataRowCount: {sheet.Rows.Count}");
                builder.AppendLine();
            }

            var outputPath = Path.Combine(outputDirectory, $"{Path.GetFileName(samplePath)}.txt");
            File.WriteAllText(outputPath, builder.ToString(), Encoding.UTF8);
            Assert.True(File.Exists(outputPath));
        }
    }

    private static string FindRepositoryRoot()
    {
        var candidates = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
        };

        foreach (var candidate in candidates)
        {
            var directory = new DirectoryInfo(candidate);

            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "FuelRecon.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing FuelRecon.slnx.");
    }
}
