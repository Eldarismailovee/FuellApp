using FuelRecon.Domain;

namespace FuelRecon.Application.Persistence;

public sealed record SettingsSnapshotRecord(
    string Id,
    DateTimeOffset CreatedAtUtc,
    string CreatedBy,
    string SnapshotJson,
    string? Description = null);

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
