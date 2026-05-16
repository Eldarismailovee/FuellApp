using FuelRecon.Domain;

namespace FuelRecon.Application.Persistence;

public sealed record SettingsSnapshotRecord(
    string Id,
    DateTimeOffset CreatedAtUtc,
    string CreatedBy,
    string SnapshotJson,
    string? Description = null);

/// <summary>
/// Persisted workflow row for a fuel period (<c>Periods</c> table).
/// </summary>
public sealed record PeriodLifecycleRecord(
    FuelPeriod Period,
    PeriodLifecycleStatus LifecycleStatus,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    DateTimeOffset? ReopenedAtUtc,
    string? ReopenReason);

public sealed record AuditRecord(
    Guid Id,
    DateTimeOffset CreatedAtUtc,
    string Actor,
    AuditActionType ActionType,
    AuditEntityType EntityType,
    string EntityId,
    string Origin,
    string? OldValuesJson = null,
    string? NewValuesJson = null,
    string? Note = null,
    string? ReasonCode = null,
    CorrelationId? CorrelationId = null,
    string? ContextJson = null);
