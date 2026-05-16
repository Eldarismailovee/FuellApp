namespace FuelRecon.Application.Pdf.Templates;

/// <summary>
/// Resolved active PDF template metadata for branch export (layout identity only; no rendering in TASK-11.01).
/// </summary>
public sealed record PdfTemplateConfiguration(string TemplateName, string TemplateVersion)
{
    public string TemplateName { get; } = ValidateName(TemplateName, nameof(TemplateName));

    public string TemplateVersion { get; } = ValidateName(TemplateVersion, nameof(TemplateVersion));

    private static string ValidateName(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", paramName);
        }

        return value.Trim();
    }
}
