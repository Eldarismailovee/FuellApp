using FuelRecon.Domain;

namespace FuelRecon.Application.Validation;

public sealed record ValidateImportBatchRequest(
    FuelPeriod Period,
    string? SupplierPdfPath,
    string? BranchLitresExcelPath,
    string? CarsBillingExcelPath,
    BranchAliasResolver BranchAliasResolver);

public sealed record ImportValidationResult(
    bool Success,
    IReadOnlyList<ImportFileValidationResult> Files,
    IReadOnlyList<SupplierTransaction> SupplierTransactions,
    IReadOnlyList<BranchLitresEntry> BranchLitresEntries,
    IReadOnlyList<CarsBillingEntry> CarsBillingEntries)
{
    public bool HasBlockingErrors => Files.Any(file => file.HasErrors || file.Status is FileStatus.Invalid or FileStatus.Failed);
}

public sealed record ImportFileValidationResult(
    InputSlot InputSlot,
    string? FilePath,
    FileStatus Status,
    int RowCount,
    int ValidRowCount,
    int SkippedRowCount,
    IReadOnlyList<ImportValidationIssue> Issues)
{
    public bool HasErrors => Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public bool HasWarnings => Issues.Any(issue => issue.Severity == ValidationSeverity.Warning);
}

public sealed record ImportValidationIssue(
    ValidationSeverity Severity,
    string ReasonCode,
    string Message,
    SourceReference? SourceReference = null);
