namespace FuelRecon.Domain;

/// <summary>
/// Period-level workflow state driving available actions and primary UI CTA.
/// </summary>
public enum PeriodLifecycleStatus
{
    Draft = 0,
    FilesImported = 1,
    Validated = 2,
    Reconciled = 3,
    ReviewRequired = 4,
    Reviewed = 5,
    Approved = 6,
    Closed = 7,
    Reopened = 8,
}
