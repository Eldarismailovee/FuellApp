namespace FuelRecon.Application.PeriodLifecycle;

/// <summary>
/// Machine-readable audit reason codes for period lifecycle transitions.
/// </summary>
public static class PeriodLifecycleAuditReasonCodes
{
    public const string TransitionApplied = nameof(TransitionApplied);

    public const string InvalidTransition = nameof(InvalidTransition);

    public const string SameStateTransitionNotAllowed = nameof(SameStateTransitionNotAllowed);

    public const string ReopenReasonRequired = nameof(ReopenReasonRequired);
}
