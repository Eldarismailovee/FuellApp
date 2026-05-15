namespace FuelRecon.Domain;

/// <summary>
/// User resolution state for an exception or review item.
/// Stored separately from the original system reconciliation status.
/// </summary>
public enum ResolutionStatus
{
    Unresolved = 0,
    InReview = 1,
    Resolved = 2,
    ManuallyLinked = 3,
    ApprovedWithException = 4,
    ExcludedByRule = 5,
}