using FuelRecon.Domain;

namespace FuelRecon.Application.Persistence;

public interface IImportBatchRepository
{
    void Save(ImportBatchRecord batch);

    ImportBatchRecord? GetById(Guid id);
}

public interface IImportedFileRepository
{
    void Save(ImportedFileRecord file);

    ImportedFileRecord? GetById(Guid id);

    IReadOnlyList<ImportedFileRecord> ListByImportBatch(Guid importBatchId);
}

public interface ISupplierTransactionRepository
{
    void Save(Guid importBatchId, Guid importedFileId, SupplierTransaction transaction);

    void SaveMany(Guid importBatchId, Guid importedFileId, IEnumerable<SupplierTransaction> transactions);

    SupplierTransaction? GetById(Guid id);

    IReadOnlyList<SupplierTransaction> ListByImportBatch(Guid importBatchId);
}

public interface IBranchLitresRepository
{
    void Save(Guid importBatchId, Guid importedFileId, BranchLitresEntry entry);

    void SaveMany(Guid importBatchId, Guid importedFileId, IEnumerable<BranchLitresEntry> entries);

    BranchLitresEntry? GetById(Guid id);

    IReadOnlyList<BranchLitresEntry> ListByImportBatch(Guid importBatchId);
}

public interface ICarsBillingRepository
{
    void Save(Guid importBatchId, Guid importedFileId, CarsBillingEntry entry);

    void SaveMany(Guid importBatchId, Guid importedFileId, IEnumerable<CarsBillingEntry> entries);

    CarsBillingEntry? GetById(Guid id);

    IReadOnlyList<CarsBillingEntry> ListByImportBatch(Guid importBatchId);
}
