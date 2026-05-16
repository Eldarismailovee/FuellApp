using FuelRecon.Application.Pdf.Templates;

namespace FuelRecon.Tests;

public class PdfTemplateServiceTests
{
    [Fact]
    public void CreateDefault_loads_active_branch_report_template_with_stable_name_and_version()
    {
        var service = InMemoryPdfTemplateService.CreateDefault();

        var result = service.GetActiveTemplate();

        var resolved = Assert.IsType<PdfTemplateResolved>(result);
        Assert.Equal(PdfTemplateDefaults.ActiveBranchReportTemplateKey, resolved.Configuration.TemplateName);
        Assert.Equal(PdfTemplateDefaults.BranchReportTemplateVersion, resolved.Configuration.TemplateVersion);
    }

    [Fact]
    public void GetActiveTemplate_returns_TemplateNotFound_when_catalog_lacks_active_key()
    {
        var catalog = new Dictionary<string, PdfTemplateConfiguration>(StringComparer.Ordinal)
        {
            ["Other"] = new PdfTemplateConfiguration("Other", "0.0.1"),
        };

        var service = new InMemoryPdfTemplateService(PdfTemplateDefaults.ActiveBranchReportTemplateKey, catalog);

        var result = service.GetActiveTemplate();

        var missing = Assert.IsType<PdfTemplateNotFound>(result);
        Assert.Equal(PdfTemplateDefaults.ActiveBranchReportTemplateKey, missing.ActiveTemplateKey);
        Assert.Equal(PdfTemplateErrorCategories.TemplateNotFound, missing.ErrorCategory);
    }

    [Fact]
    public void PdfTemplateConfiguration_rejects_blank_template_name_or_version()
    {
        Assert.Throws<ArgumentException>(() => new PdfTemplateConfiguration(" ", "1"));
        Assert.Throws<ArgumentException>(() => new PdfTemplateConfiguration("Name", " "));
    }

    [Fact]
    public void Constructor_rejects_blank_active_template_key()
    {
        var catalog = new Dictionary<string, PdfTemplateConfiguration>(StringComparer.Ordinal)
        {
            [PdfTemplateDefaults.ActiveBranchReportTemplateKey] =
                new PdfTemplateConfiguration(
                    PdfTemplateDefaults.ActiveBranchReportTemplateKey,
                    PdfTemplateDefaults.BranchReportTemplateVersion),
        };

        Assert.Throws<ArgumentException>(() => new InMemoryPdfTemplateService("   ", catalog));
    }
}
