using FuelRecon.Application.BranchReports;
using FuelRecon.Application.Pdf.Export;
using FuelRecon.Application.Pdf.Templates;
using FuelRecon.Application.Persistence;
using FuelRecon.Domain;

namespace FuelRecon.Tests;

public class ExportBranchReportPdfUseCaseTests
{
    [Fact]
    public void Execute_does_not_invoke_renderer_when_active_template_is_missing()
    {
        var exports = new CaptureExportsRepository();
        var renderer = new ThrowingPdfRenderer();
        var useCase = new ExportBranchReportPdfUseCase(
            new InMemoryPdfTemplateService(
                PdfTemplateDefaults.ActiveBranchReportTemplateKey,
                new Dictionary<string, PdfTemplateConfiguration>(StringComparer.Ordinal)),
            new UnusedBranchReportRepository(),
            new UnusedRunRepository(),
            new UnusedItemRepository(),
            new UnusedApprovalRepository(),
            exports,
            new BranchReportService(),
            renderer);

        var tempDir = Path.Combine(AppContext.BaseDirectory, $"FuelRecon-pdf-unit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var response = useCase.Execute(
                new ExportBranchReportPdfRequest(
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    "unit-user",
                    new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero),
                    tempDir));

            Assert.Equal(PdfExportStatus.Failed, response.Status);
            Assert.Equal(PdfTemplateErrorCategories.TemplateNotFound, response.ErrorCategory);
            Assert.False(renderer.WasCalled);
            Assert.Single(exports.Saved);
            Assert.Equal(PdfTemplateErrorCategories.TemplateNotFound, exports.Saved[0].ErrorCategory);
            Assert.Null(exports.Saved[0].TemplateName);
            Assert.Equal("Active PDF template configuration was not found.", exports.Saved[0].ErrorMessage);
            Assert.NotNull(exports.Saved[0].ExportSettingsSnapshot);
            Assert.Contains("\"failureStage\":\"TemplateNotFound\"", exports.Saved[0].ExportSettingsSnapshot, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private sealed class ThrowingPdfRenderer : IBranchReportPdfRenderer
    {
        public bool WasCalled { get; private set; }

        public void Render(BranchReportPdfDocumentContent content, Stream output)
        {
            WasCalled = true;
            throw new InvalidOperationException("Renderer must not run when the template is missing.");
        }
    }

    private sealed class CaptureExportsRepository : IPdfExportRepository
    {
        public List<PdfExportRecord> Saved { get; } = [];

        public void Save(PdfExportRecord exportRecord) => Saved.Add(exportRecord);

        public PdfExportRecord? GetById(Guid id) => throw new NotSupportedException();

        public IReadOnlyList<PdfExportRecord> ListByBranchReport(Guid branchReportId) => throw new NotSupportedException();
    }

    private sealed class UnusedBranchReportRepository : IBranchReportRepository
    {
        public void Save(BranchReportVersion report, BranchSummary? summary = null) => throw new NotSupportedException();

        public BranchReportVersion? GetById(Guid id) => throw new NotSupportedException();

        public IReadOnlyList<BranchReportVersion> ListByRunAndBranch(Guid runId, CanonicalBranchId branchId) =>
            throw new NotSupportedException();

        public BranchReportPersistedMetrics? GetPersistedMetrics(Guid branchReportVersionId) => throw new NotSupportedException();
    }

    private sealed class UnusedRunRepository : IReconciliationRunRepository
    {
        public void Save(ReconciliationRun run) => throw new NotSupportedException();

        public ReconciliationRun? GetById(Guid id) => throw new NotSupportedException();

        public ReconciliationRun? GetLatestForPeriod(FuelPeriod period) => throw new NotSupportedException();
    }

    private sealed class UnusedItemRepository : IReconciliationItemRepository
    {
        public void Save(ReconciliationItem item) => throw new NotSupportedException();

        public void SaveMany(IEnumerable<ReconciliationItem> items) => throw new NotSupportedException();

        public ReconciliationItem? GetById(Guid id) => throw new NotSupportedException();

        public IReadOnlyList<ReconciliationItem> ListByRun(Guid runId) => throw new NotSupportedException();
    }

    private sealed class UnusedApprovalRepository : IBranchReportApprovalRepository
    {
        public void Save(BranchReportApprovalRecord approval) => throw new NotSupportedException();

        public BranchReportApprovalRecord? FindByBranchReport(Guid branchReportVersionId) => throw new NotSupportedException();
    }
}
