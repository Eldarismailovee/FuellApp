namespace FuelRecon.Domain;

/// <summary>
/// System-assigned reconciliation outcome for a reconciliation item (not raw import rows).
/// </summary>
public enum ReconciliationStatus
{
    Matched = 0,
    Unbilled = 1,
    Variance = 2,
    DuplicatePossible = 3,
    MissingRA = 4,
    RegoMismatch = 5,
    SupplierOnly = 6,
    CarsOnly = 7,
    ReviewRequired = 8,
}
