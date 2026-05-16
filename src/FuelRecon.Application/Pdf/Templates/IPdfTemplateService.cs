namespace FuelRecon.Application.Pdf.Templates;

/// <summary>
/// Provides the active PDF template metadata used for branch report export (TASK-11.01; rendering is out of scope).
/// </summary>
public interface IPdfTemplateService
{
    /// <summary>
    /// Loads the configured active template from the backing catalog.
    /// </summary>
    PdfTemplateResolveResult GetActiveTemplate();
}
