using FuelRecon.Application.Persistence;

namespace FuelRecon.Application.Reconciliation;

public sealed class PersistReconciliationResultUseCase(
    IReconciliationRunRepository reconciliationRunRepository,
    IReconciliationItemRepository reconciliationItemRepository)
{
    public PersistReconciliationResultResponse Execute(PersistReconciliationResultRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Result);

        reconciliationRunRepository.Save(request.Result.Run);

        if (request.Result.Items.Count > 0)
        {
            reconciliationItemRepository.SaveMany(request.Result.Items);
        }

        return new PersistReconciliationResultResponse(
            request.Result.Run.Id,
            request.Result.Items.Count);
    }
}
