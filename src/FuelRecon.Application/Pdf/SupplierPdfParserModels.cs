using FuelRecon.Domain;

namespace FuelRecon.Application.Pdf;

public interface ISupplierPdfParser
{
    SupplierPdfParseResult Parse(string filePath, FuelPeriod period, BranchAliasResolver branchAliasResolver);
}

public sealed record SupplierPdfParseResult(
    bool Success,
    IReadOnlyList<SupplierTransaction> Entries,
    IReadOnlyList<SupplierPdfParseIssue> Issues,
    int PageCount,
    int CandidateRowCount,
    int ValidRowCount,
    int SkippedRowCount)
{
    public bool HasErrors => Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public bool HasWarnings => Issues.Any(issue => issue.Severity == ValidationSeverity.Warning);

    public static SupplierPdfParseResult From(
        IReadOnlyList<SupplierTransaction> entries,
        IReadOnlyList<SupplierPdfParseIssue> issues,
        int pageCount,
        int candidateRowCount,
        bool success) =>
        new(success, entries, issues, pageCount, candidateRowCount, entries.Count, candidateRowCount - entries.Count);
}

public sealed record SupplierPdfParseIssue(
    ValidationSeverity Severity,
    string ReasonCode,
    string Message,
    SourceReference? SourceReference = null);
