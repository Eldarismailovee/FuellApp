using FuelRecon.Application.PeriodLifecycle;
using FuelRecon.Domain;

namespace FuelRecon.Tests;

public class PeriodLifecycleTransitionRulesTests
{
    [Theory]
    [InlineData(PeriodLifecycleStatus.Draft, PeriodLifecycleStatus.FilesImported)]
    [InlineData(PeriodLifecycleStatus.FilesImported, PeriodLifecycleStatus.Validated)]
    [InlineData(PeriodLifecycleStatus.Validated, PeriodLifecycleStatus.Reconciled)]
    [InlineData(PeriodLifecycleStatus.Reconciled, PeriodLifecycleStatus.ReviewRequired)]
    [InlineData(PeriodLifecycleStatus.Reconciled, PeriodLifecycleStatus.Reviewed)]
    [InlineData(PeriodLifecycleStatus.ReviewRequired, PeriodLifecycleStatus.Reviewed)]
    [InlineData(PeriodLifecycleStatus.Reviewed, PeriodLifecycleStatus.Approved)]
    [InlineData(PeriodLifecycleStatus.Approved, PeriodLifecycleStatus.Closed)]
    [InlineData(PeriodLifecycleStatus.Closed, PeriodLifecycleStatus.Reopened)]
    [InlineData(PeriodLifecycleStatus.Reopened, PeriodLifecycleStatus.Draft)]
    public void Validate_accepts_documented_forward_transitions(PeriodLifecycleStatus from, PeriodLifecycleStatus to)
    {
        PeriodLifecycleTransitionRules.Validate(from, to);
        Assert.True(PeriodLifecycleTransitionRules.IsAllowed(from, to));
    }

    [Fact]
    public void Validate_rejects_same_state()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            PeriodLifecycleTransitionRules.Validate(PeriodLifecycleStatus.Draft, PeriodLifecycleStatus.Draft));

        Assert.Contains(PeriodLifecycleAuditReasonCodes.SameStateTransitionNotAllowed, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_rejects_skipping_forward_steps()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            PeriodLifecycleTransitionRules.Validate(PeriodLifecycleStatus.Draft, PeriodLifecycleStatus.Closed));

        Assert.Contains(PeriodLifecycleAuditReasonCodes.InvalidTransition, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IsAllowed_returns_false_for_same_state()
    {
        Assert.False(PeriodLifecycleTransitionRules.IsAllowed(PeriodLifecycleStatus.Validated, PeriodLifecycleStatus.Validated));
    }
}
