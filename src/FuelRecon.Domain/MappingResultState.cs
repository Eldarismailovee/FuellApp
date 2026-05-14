namespace FuelRecon.Domain;

/// <summary>
/// How column/layout mapping was established for an imported file.
/// </summary>
public enum MappingResultState
{
    AutoMapped = 0,
    TemplateMapped = 1,
    ManualMapped = 2,
    IncompleteMapping = 3,
}
