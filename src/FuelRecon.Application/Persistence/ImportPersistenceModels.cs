using FuelRecon.Domain;

namespace FuelRecon.Application.Persistence;

public sealed record ImportBatchRecord(
    Guid Id,
    FuelPeriod Period,
    DateTimeOffset CreatedAtUtc,
    string CreatedBy,
    string Status,
    string? SourceDescription = null,
    string? SettingsSnapshotId = null);

public sealed record ImportedFileRecord(
    Guid Id,
    Guid ImportBatchId,
    FuelPeriod Period,
    InputSlot InputSlot,
    string OriginalFileName,
    FileStatus FileStatus,
    FileChecksum Checksum,
    DateTimeOffset ImportedAtUtc,
    string? StoredFilePath = null,
    string? ParserName = null,
    string? ParserVersion = null,
    DateTimeOffset? CompletedAtUtc = null,
    string? FailureReasonCode = null,
    string? FailureMessage = null);
