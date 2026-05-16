using System.Text.Json;
using FuelRecon.Application.Persistence;
using FuelRecon.Domain;

namespace FuelRecon.Application.PeriodLifecycle;

public sealed record TransitionPeriodLifecycleRequest(
    FuelPeriod Period,
    PeriodLifecycleStatus TargetStatus,
    string Actor,
    DateTimeOffset TransitionedAtUtc,
    string? Note = null,
    CorrelationId? CorrelationId = null);

public sealed record TransitionPeriodLifecycleResponse(PeriodLifecycleRecord Period);

public interface ITransitionPeriodLifecycleUseCase
{
    TransitionPeriodLifecycleResponse Execute(TransitionPeriodLifecycleRequest request);
}

public sealed class TransitionPeriodLifecycleUseCase(
    IPeriodLifecycleRepository periodLifecycleRepository,
    IAuditRepository auditRepository) : ITransitionPeriodLifecycleUseCase
{
    private const string AuditOrigin = "FuelRecon.Application.PeriodLifecycle";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public TransitionPeriodLifecycleResponse Execute(TransitionPeriodLifecycleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Actor);

        if (request.TargetStatus == PeriodLifecycleStatus.Reopened
            && string.IsNullOrWhiteSpace(request.Note))
        {
            throw new ArgumentException(
                $"{PeriodLifecycleAuditReasonCodes.ReopenReasonRequired}: A reopen reason note is required.",
                nameof(request.Note));
        }

        periodLifecycleRepository.EnsurePeriod(request.Period, request.TransitionedAtUtc);

        var current = periodLifecycleRepository.Get(request.Period);
        if (current is null)
        {
            throw new InvalidOperationException(
                $"Period '{request.Period.ToSortableString()}' could not be loaded after ensure.");
        }

        PeriodLifecycleTransitionRules.Validate(current.LifecycleStatus, request.TargetStatus);

        var next = ApplyTransition(current, request.TargetStatus, request.TransitionedAtUtc, request.Note);

        periodLifecycleRepository.Save(next);

        var entityId = request.Period.ToSortableString();
        var oldValues = SerializeLifecyclePayload(current.LifecycleStatus, current.ReopenReason);
        var newValues = SerializeLifecyclePayload(next.LifecycleStatus, next.ReopenReason);

        auditRepository.Save(
            new AuditRecord(
                Guid.NewGuid(),
                request.TransitionedAtUtc,
                request.Actor.Trim(),
                AuditActionType.LifecycleTransition,
                AuditEntityType.Period,
                entityId,
                AuditOrigin,
                OldValuesJson: oldValues,
                NewValuesJson: newValues,
                Note: request.Note?.Trim(),
                ReasonCode: PeriodLifecycleAuditReasonCodes.TransitionApplied,
                CorrelationId: request.CorrelationId));

        return new TransitionPeriodLifecycleResponse(next);
    }

    private static PeriodLifecycleRecord ApplyTransition(
        PeriodLifecycleRecord current,
        PeriodLifecycleStatus target,
        DateTimeOffset transitionedAtUtc,
        string? transitionNote)
    {
        var closedAt = current.ClosedAtUtc;
        var reopenedAt = current.ReopenedAtUtc;
        var reopenReason = current.ReopenReason;

        if (target == PeriodLifecycleStatus.Closed)
        {
            closedAt = transitionedAtUtc;
            reopenedAt = null;
            reopenReason = null;
        }
        else if (target == PeriodLifecycleStatus.Reopened)
        {
            reopenedAt = transitionedAtUtc;
            reopenReason = transitionNote!.Trim();
        }
        else if (current.LifecycleStatus == PeriodLifecycleStatus.Reopened && target != PeriodLifecycleStatus.Reopened)
        {
            reopenedAt = null;
            reopenReason = null;
        }

        return current with
        {
            LifecycleStatus = target,
            UpdatedAtUtc = transitionedAtUtc,
            ClosedAtUtc = closedAt,
            ReopenedAtUtc = reopenedAt,
            ReopenReason = reopenReason,
        };
    }

    private static string SerializeLifecyclePayload(PeriodLifecycleStatus status, string? reopenReason)
    {
        var fields = new SortedDictionary<string, string?>(StringComparer.Ordinal)
        {
            ["lifecycleStatus"] = status.ToString(),
        };

        if (!string.IsNullOrWhiteSpace(reopenReason))
        {
            fields["reopenReason"] = reopenReason.Trim();
        }

        return JsonSerializer.Serialize(fields, JsonOptions);
    }
}
