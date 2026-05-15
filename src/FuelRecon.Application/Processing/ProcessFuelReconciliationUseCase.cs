using FuelRecon.Application.Reconciliation;
using FuelRecon.Application.Validation;

namespace FuelRecon.Application.Processing;

public sealed class ProcessFuelReconciliationUseCase(
    IValidateImportBatchUseCase validateImportBatchUseCase,
    IPersistImportValidationResultUseCase persistImportValidationResultUseCase,
    IReconciliationEngine reconciliationEngine,
    IPersistReconciliationResultUseCase persistReconciliationResultUseCase)
{
    public ProcessFuelReconciliationResult Execute(ProcessFuelReconciliationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.BranchAliasResolver);

        var validationResult = validateImportBatchUseCase.Execute(new ValidateImportBatchRequest(
            request.Period,
            request.SupplierPdfPath,
            request.BranchLitresExcelPath,
            request.CarsBillingExcelPath,
            request.BranchAliasResolver));

        var importPersistenceResponse = persistImportValidationResultUseCase.Execute(new PersistImportValidationResultRequest(
            validationResult,
            request.Period,
            request.ImportedBy,
            request.FileChecksums,
            request.ImportedAtUtc));

        if (validationResult.HasBlockingErrors || !validationResult.Success)
        {
            return new ProcessFuelReconciliationResult(
                Success: false,
                validationResult,
                importPersistenceResponse,
                ReconciliationEngineResult: null,
                ReconciliationPersistenceResponse: null);
        }

        var reconciliationResult = reconciliationEngine.Reconcile(new ReconciliationEngineInput(
            request.Period,
            validationResult.SupplierTransactions,
            validationResult.BranchLitresEntries,
            validationResult.CarsBillingEntries,
            request.ReconciliationRules));

        var reconciliationPersistenceResponse = persistReconciliationResultUseCase.Execute(
            new PersistReconciliationResultRequest(reconciliationResult, request.RunBy));

        return new ProcessFuelReconciliationResult(
            Success: true,
            validationResult,
            importPersistenceResponse,
            reconciliationResult,
            reconciliationPersistenceResponse);
    }
}
