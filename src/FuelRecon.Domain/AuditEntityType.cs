namespace FuelRecon.Domain;

/// <summary>
/// Entity type referenced by a business audit record.
/// </summary>
public enum AuditEntityType
{
    Period = 0,
    ImportBatch = 1,
    ImportedFile = 2,
    ValidationResult = 3,
    SupplierTransaction = 4,
    BranchLitresEntry = 5,
    CarsBillingEntry = 6,
    ReconciliationRun = 7,
    ReconciliationItem = 8,
    ManualAction = 9,
    BranchReport = 10,
    PdfExport = 11,
    SettingsSnapshot = 12,
}
