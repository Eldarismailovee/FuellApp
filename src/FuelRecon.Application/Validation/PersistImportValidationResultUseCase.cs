using FuelRecon.Application.Persistence;
using FuelRecon.Domain;

namespace FuelRecon.Application.Validation;

public sealed class PersistImportValidationResultUseCase(
    IImportBatchRepository importBatchRepository,
    IImportedFileRepository importedFileRepository,
    ISupplierTransactionRepository supplierTransactionRepository,
    IBranchLitresRepository branchLitresRepository,
    ICarsBillingRepository carsBillingRepository)
{
    private static readonly FileChecksum PlaceholderChecksum = new("UNAVAILABLE", "UNAVAILABLE");

    public PersistImportValidationResultResponse Execute(PersistImportValidationResultRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ValidationResult);

        if (string.IsNullOrWhiteSpace(request.ImportedBy))
        {
            throw new ArgumentException("Imported by cannot be empty.", nameof(request));
        }

        var importedAtUtc = request.ImportedAtUtc ?? DateTimeOffset.UtcNow;
        var importBatchId = Guid.NewGuid();

        var batch = new ImportBatchRecord(
            importBatchId,
            request.Period,
            importedAtUtc,
            request.ImportedBy,
            request.ValidationResult.Success ? "Validated" : "ValidationFailed",
            SourceDescription: "Import validation result");

        importBatchRepository.Save(batch);

        var importedFileIds = new Dictionary<InputSlot, Guid>();

        foreach (var fileResult in GetMandatoryFileResults(request.ValidationResult))
        {
            var importedFileId = Guid.NewGuid();
            importedFileIds[fileResult.InputSlot] = importedFileId;

            importedFileRepository.Save(new ImportedFileRecord(
                importedFileId,
                importBatchId,
                request.Period,
                fileResult.InputSlot,
                OriginalFileName: GetOriginalFileName(fileResult),
                fileResult.Status,
                GetChecksum(fileResult.InputSlot, request.FileChecksums),
                importedAtUtc,
                StoredFilePath: fileResult.FilePath,
                CompletedAtUtc: importedAtUtc,
                FailureReasonCode: fileResult.HasErrors ? fileResult.Issues.First(issue => issue.Severity == ValidationSeverity.Error).ReasonCode : null,
                FailureMessage: fileResult.HasErrors ? fileResult.Issues.First(issue => issue.Severity == ValidationSeverity.Error).Message : null));
        }

        if (importedFileIds.TryGetValue(InputSlot.SupplierStatement, out var supplierFileId)
            && request.ValidationResult.SupplierTransactions.Count > 0)
        {
            supplierTransactionRepository.SaveMany(
                importBatchId,
                supplierFileId,
                request.ValidationResult.SupplierTransactions);
        }

        if (importedFileIds.TryGetValue(InputSlot.BranchLitres, out var branchFileId)
            && request.ValidationResult.BranchLitresEntries.Count > 0)
        {
            branchLitresRepository.SaveMany(
                importBatchId,
                branchFileId,
                request.ValidationResult.BranchLitresEntries);
        }

        if (importedFileIds.TryGetValue(InputSlot.CarsBilling, out var carsFileId)
            && request.ValidationResult.CarsBillingEntries.Count > 0)
        {
            carsBillingRepository.SaveMany(
                importBatchId,
                carsFileId,
                request.ValidationResult.CarsBillingEntries);
        }

        return new PersistImportValidationResultResponse(importBatchId, importedFileIds);
    }

    private static IReadOnlyList<ImportFileValidationResult> GetMandatoryFileResults(ImportValidationResult validationResult)
    {
        var bySlot = validationResult.Files.ToDictionary(file => file.InputSlot);

        return
        [
            GetOrCreateMissingFile(bySlot, InputSlot.SupplierStatement),
            GetOrCreateMissingFile(bySlot, InputSlot.BranchLitres),
            GetOrCreateMissingFile(bySlot, InputSlot.CarsBilling),
        ];
    }

    private static ImportFileValidationResult GetOrCreateMissingFile(
        IReadOnlyDictionary<InputSlot, ImportFileValidationResult> bySlot,
        InputSlot inputSlot)
    {
        if (bySlot.TryGetValue(inputSlot, out var fileResult))
        {
            return fileResult;
        }

        return new ImportFileValidationResult(
            inputSlot,
            FilePath: null,
            FileStatus.Invalid,
            RowCount: 0,
            ValidRowCount: 0,
            SkippedRowCount: 0,
            [
                new ImportValidationIssue(
                    ValidationSeverity.Error,
                    ValidateImportBatchUseCase.MissingMandatoryFileReasonCode,
                    $"{inputSlot} file is mandatory.")
            ]);
    }

    private static string GetOriginalFileName(ImportFileValidationResult fileResult)
    {
        if (!string.IsNullOrWhiteSpace(fileResult.FilePath))
        {
            return Path.GetFileName(fileResult.FilePath);
        }

        return $"missing-{fileResult.InputSlot}";
    }

    private static FileChecksum GetChecksum(
        InputSlot inputSlot,
        IReadOnlyDictionary<InputSlot, FileChecksum>? fileChecksums)
    {
        if (fileChecksums is not null && fileChecksums.TryGetValue(inputSlot, out var checksum))
        {
            return checksum;
        }

        return PlaceholderChecksum;
    }
}
