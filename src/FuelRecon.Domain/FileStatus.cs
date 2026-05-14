namespace FuelRecon.Domain;

/// <summary>
/// Lifecycle state of an imported source file.
/// </summary>
public enum FileStatus
{
    None = 0,
    Imported = 1,
    Parsing = 2,
    Parsed = 3,
    Validating = 4,
    Valid = 5,
    Invalid = 6,
    Failed = 7,
}
