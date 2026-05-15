using System.Text;
using FuelRecon.Application.Pdf;
using FuelRecon.Infrastructure.Pdf;

namespace FuelRecon.GoldenFiles;

public class PdfExtractionDiagnosticsTests
{
    private static readonly string[] SampleFileNames =
    [
        "Farmlands Statement April.PDF",
        "Mobile - Taupo.pdf",
    ];

    [Fact]
    public void Write_pdf_extraction_diagnostics_for_available_supplier_samples()
    {
        var repositoryRoot = FindRepositoryRoot();
        var samplesDirectory = Path.Combine(repositoryRoot, "samples", "client-raw");
        var outputDirectory = Path.Combine(repositoryRoot, "artifacts", "pdf-extraction");
        var reader = new PdfPigDocumentReader();

        foreach (var sampleFileName in SampleFileNames)
        {
            var samplePath = Path.Combine(samplesDirectory, sampleFileName);
            if (!File.Exists(samplePath))
            {
                continue;
            }

            var result = reader.ReadDocument(samplePath);

            Assert.True(result.Success, result.Message);
            Assert.NotNull(result.Document);
            Assert.NotEmpty(result.Document.Pages);

            Directory.CreateDirectory(outputDirectory);

            var outputPath = Path.Combine(outputDirectory, $"{ToSafeFileName(sampleFileName)}.txt");
            File.WriteAllText(outputPath, FormatDiagnosticText(result.Document.SourceFile, result.Document.Pages), Encoding.UTF8);

            Assert.True(File.Exists(outputPath));
        }
    }

    private static string FormatDiagnosticText(string sourceFile, IReadOnlyList<PdfPageModel> pages)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"SourceFile: {sourceFile}");
        builder.AppendLine();

        foreach (var page in pages)
        {
            builder.AppendLine($"--- Page {page.PageNumber} ---");

            foreach (var line in page.Lines.Where(line => !string.IsNullOrWhiteSpace(line)))
            {
                builder.AppendLine(line);
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string ToSafeFileName(string fileName)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(fileName.Length);

        foreach (var character in fileName)
        {
            builder.Append(invalidCharacters.Contains(character) ? '_' : character);
        }

        return builder.ToString();
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
