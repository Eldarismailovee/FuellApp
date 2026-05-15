using FuelRecon.Domain;

namespace FuelRecon.Application.Validation;

public sealed record PersistImportValidationResultRequest(
    ImportValidationResult ValidationResult,
    FuelPeriod Period,
    string ImportedBy,
    IReadOnlyDictionary<InputSlot, FileChecksum>? FileChecksums = null,
    DateTimeOffset? ImportedAtUtc = null);

public sealed record PersistImportValidationResultResponse(
    Guid ImportBatchId,
    IReadOnlyDictionary<InputSlot, Guid> ImportedFileIds);
