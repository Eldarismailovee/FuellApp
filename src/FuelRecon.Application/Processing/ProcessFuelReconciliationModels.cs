using FuelRecon.Application.Reconciliation;
using FuelRecon.Application.Validation;
using FuelRecon.Domain;

namespace FuelRecon.Application.Processing;

public sealed record ProcessFuelReconciliationRequest(
    FuelPeriod Period,
    string? SupplierPdfPath,
    string? BranchLitresExcelPath,
    string? CarsBillingExcelPath,
    BranchAliasResolver BranchAliasResolver,
    string ImportedBy,
    string RunBy,
    IReadOnlyDictionary<InputSlot, FileChecksum>? FileChecksums = null,
    ReconciliationRulesOptions? ReconciliationRules = null,
    DateTimeOffset? ImportedAtUtc = null);

public sealed record ProcessFuelReconciliationResult(
    bool Success,
    ImportValidationResult ImportValidationResult,
    PersistImportValidationResultResponse ImportPersistenceResponse,
    ReconciliationEngineResult? ReconciliationEngineResult,
    PersistReconciliationResultResponse? ReconciliationPersistenceResponse);
