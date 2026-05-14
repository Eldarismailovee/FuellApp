namespace FuelRecon.Domain;

/// <summary>
/// User resolution state for an exception or review item.
/// </summary>
public enum ResolutionStatus
{
    None = 0,
    Open = 1,
    Resolved = 2,
    Deferred = 3,
}
