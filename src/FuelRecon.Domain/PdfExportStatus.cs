namespace FuelRecon.Domain;

/// <summary>
/// Status of a single PDF export attempt (each attempt is an immutable history record).
/// </summary>
public enum PdfExportStatus
{
    Pending = 0,
    InProgress = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
}
