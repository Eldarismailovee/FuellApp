namespace FuelRecon.Application.Reconciliation;

public sealed record PersistReconciliationResultRequest(
    ReconciliationEngineResult Result,
    string? Actor = null);

public sealed record PersistReconciliationResultResponse(
    Guid RunId,
    int SavedItemCount);
