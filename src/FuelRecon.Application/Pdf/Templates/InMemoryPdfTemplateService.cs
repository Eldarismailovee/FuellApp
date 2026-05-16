namespace FuelRecon.Application.Pdf.Templates;

/// <summary>
/// Built-in branch-report PDF template identifiers for MVP (in-memory catalog).
/// </summary>
public static class PdfTemplateDefaults
{
    public const string ActiveBranchReportTemplateKey = "BranchReport";

    public const string BranchReportTemplateVersion = "1.0.0";
}

/// <summary>
/// In-memory template catalog with a single active key (no filesystem; deterministic).
/// </summary>
public sealed class InMemoryPdfTemplateService : IPdfTemplateService
{
    private readonly string _activeTemplateKey;

    private readonly IReadOnlyDictionary<string, PdfTemplateConfiguration> _catalog;

    public InMemoryPdfTemplateService(
        string activeTemplateKey,
        IReadOnlyDictionary<string, PdfTemplateConfiguration> catalog)
    {
        if (string.IsNullOrWhiteSpace(activeTemplateKey))
        {
            throw new ArgumentException("Active template key cannot be empty.", nameof(activeTemplateKey));
        }

        ArgumentNullException.ThrowIfNull(catalog);

        _activeTemplateKey = activeTemplateKey.Trim();
        _catalog = catalog;
    }

    /// <summary>
    /// MVP default: active key <see cref="PdfTemplateDefaults.ActiveBranchReportTemplateKey"/> with one catalog entry.
    /// </summary>
    public static InMemoryPdfTemplateService CreateDefault() =>
        new(
            PdfTemplateDefaults.ActiveBranchReportTemplateKey,
            new Dictionary<string, PdfTemplateConfiguration>(StringComparer.Ordinal)
            {
                [PdfTemplateDefaults.ActiveBranchReportTemplateKey] = new PdfTemplateConfiguration(
                    PdfTemplateDefaults.ActiveBranchReportTemplateKey,
                    PdfTemplateDefaults.BranchReportTemplateVersion),
            });

    public PdfTemplateResolveResult GetActiveTemplate()
    {
        if (!_catalog.TryGetValue(_activeTemplateKey, out var configuration))
        {
            return new PdfTemplateNotFound(_activeTemplateKey);
        }

        return new PdfTemplateResolved(configuration);
    }
}
