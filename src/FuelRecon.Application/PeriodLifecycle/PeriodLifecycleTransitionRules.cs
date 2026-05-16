using FuelRecon.Domain;

namespace FuelRecon.Application.PeriodLifecycle;

/// <summary>
/// Validates transitions for <see cref="PeriodLifecycleStatus"/> rows stored on <c>Periods</c>.
/// </summary>
public static class PeriodLifecycleTransitionRules
{
    private static readonly HashSet<(PeriodLifecycleStatus From, PeriodLifecycleStatus To)> Allowed =
    [
        (PeriodLifecycleStatus.Draft, PeriodLifecycleStatus.FilesImported),

        (PeriodLifecycleStatus.FilesImported, PeriodLifecycleStatus.Validated),
        (PeriodLifecycleStatus.FilesImported, PeriodLifecycleStatus.Draft),

        (PeriodLifecycleStatus.Validated, PeriodLifecycleStatus.Reconciled),
        (PeriodLifecycleStatus.Validated, PeriodLifecycleStatus.FilesImported),

        (PeriodLifecycleStatus.Reconciled, PeriodLifecycleStatus.ReviewRequired),
        (PeriodLifecycleStatus.Reconciled, PeriodLifecycleStatus.Reviewed),

        (PeriodLifecycleStatus.ReviewRequired, PeriodLifecycleStatus.Reviewed),
        (PeriodLifecycleStatus.ReviewRequired, PeriodLifecycleStatus.Reconciled),

        (PeriodLifecycleStatus.Reviewed, PeriodLifecycleStatus.Approved),
        (PeriodLifecycleStatus.Reviewed, PeriodLifecycleStatus.ReviewRequired),

        (PeriodLifecycleStatus.Approved, PeriodLifecycleStatus.Closed),

        (PeriodLifecycleStatus.Closed, PeriodLifecycleStatus.Reopened),

        (PeriodLifecycleStatus.Reopened, PeriodLifecycleStatus.Draft),
        (PeriodLifecycleStatus.Reopened, PeriodLifecycleStatus.FilesImported),
        (PeriodLifecycleStatus.Reopened, PeriodLifecycleStatus.Validated),
    ];

    public static bool IsAllowed(PeriodLifecycleStatus from, PeriodLifecycleStatus to) =>
        from != to && Allowed.Contains((from, to));

    /// <summary>
    /// Throws <see cref="ArgumentException"/> when <paramref name="from"/> cannot transition to <paramref name="to"/>.
    /// </summary>
    public static void Validate(PeriodLifecycleStatus from, PeriodLifecycleStatus to)
    {
        if (from == to)
        {
            throw new ArgumentException(
                $"{PeriodLifecycleAuditReasonCodes.SameStateTransitionNotAllowed}: Period lifecycle is already '{from}'.",
                nameof(to));
        }

        if (!Allowed.Contains((from, to)))
        {
            throw new ArgumentException(
                $"{PeriodLifecycleAuditReasonCodes.InvalidTransition}: Cannot transition period lifecycle from '{from}' to '{to}'.",
                nameof(to));
        }
    }
}
