namespace FuelRecon.Application.Pdf.Templates;

public abstract record PdfTemplateResolveResult;

/// <summary>
/// Active template configuration was found in the catalog.
/// </summary>
public sealed record PdfTemplateResolved(PdfTemplateConfiguration Configuration) : PdfTemplateResolveResult;

/// <summary>
/// No template matches the active template key (see <see cref="PdfTemplateErrorCategories.TemplateNotFound"/>).
/// </summary>
public sealed record PdfTemplateNotFound(string ActiveTemplateKey) : PdfTemplateResolveResult
{
    /// <summary>Error category aligned with persisted PDF export rows.</summary>
    public string ErrorCategory => PdfTemplateErrorCategories.TemplateNotFound;
}
