using FuelRecon.Application.Pdf.Export;
using FuelRecon.Application.Persistence;
using FuelRecon.Domain;

namespace FuelRecon.Tests;

public class ListBranchReportPdfExportsUseCaseTests
{
    private static readonly Guid ReportId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Execute_throws_when_branch_report_version_id_is_empty()
    {
        var useCase = new ListBranchReportPdfExportsUseCase(new FakePdfExportHistoryRepository());

        Assert.Throws<ArgumentException>(() => useCase.Execute(new ListBranchReportPdfExportsRequest(Guid.Empty)));
    }

    [Fact]
    public void Execute_returns_empty_history_when_repository_has_no_rows()
    {
        var useCase = new ListBranchReportPdfExportsUseCase(new FakePdfExportHistoryRepository());

        var response = useCase.Execute(new ListBranchReportPdfExportsRequest(ReportId));

        Assert.Empty(response.Exports);
    }

    [Fact]
    public void Execute_returns_rows_newest_first_aligned_with_repository_ordering()
    {
        var repo = new FakePdfExportHistoryRepository();
        repo.Save(
            new PdfExportRecord(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                ReportId,
                new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero),
                "older-user",
                PdfExportStatus.Failed,
                errorCategory: "Old"));

        repo.Save(
            new PdfExportRecord(
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                ReportId,
                new DateTimeOffset(2026, 5, 2, 10, 0, 0, TimeSpan.Zero),
                "newer-user",
                PdfExportStatus.Succeeded,
                filePath: "/tmp/out.pdf",
                templateName: "BranchReport",
                templateVersion: "1.0.0"));

        var useCase = new ListBranchReportPdfExportsUseCase(repo);

        var response = useCase.Execute(new ListBranchReportPdfExportsRequest(ReportId));

        Assert.Equal(2, response.Exports.Count);
        Assert.Equal(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), response.Exports[0].ExportId);
        Assert.Equal(PdfExportStatus.Succeeded, response.Exports[0].Status);
        Assert.Equal("/tmp/out.pdf", response.Exports[0].FilePath);
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), response.Exports[1].ExportId);
        Assert.Equal(PdfExportStatus.Failed, response.Exports[1].Status);
        Assert.Equal("Old", response.Exports[1].ErrorCategory);
    }

    private sealed class FakePdfExportHistoryRepository : IPdfExportRepository
    {
        public List<PdfExportRecord> Stored { get; } = [];

        public void Save(PdfExportRecord exportRecord) => Stored.Add(exportRecord);

        public PdfExportRecord? GetById(Guid id) => Stored.FirstOrDefault(e => e.Id == id);

        public IReadOnlyList<PdfExportRecord> ListByBranchReport(Guid branchReportVersionId) =>
            Stored.Where(e => e.BranchReportVersionId == branchReportVersionId)
                .OrderByDescending(e => e.ExportedAtUtc)
                .ThenByDescending(e => e.Id.ToString("D"), StringComparer.Ordinal)
                .ToArray();
    }
}
