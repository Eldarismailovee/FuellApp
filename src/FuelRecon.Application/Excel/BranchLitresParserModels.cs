using FuelRecon.Domain;

namespace FuelRecon.Application.Excel;

public interface IBranchLitresExcelParser
{
    BranchLitresParseResult Parse(string filePath, FuelPeriod period, BranchAliasResolver branchAliasResolver);
}

public sealed record BranchLitresParseResult(
    bool Success,
    IReadOnlyList<BranchLitresEntry> Entries,
    IReadOnlyList<BranchLitresParseIssue> Issues,
    int RowCount,
    int ValidRowCount,
    int SkippedRowCount)
{
    public static BranchLitresParseResult From(
        IReadOnlyList<BranchLitresEntry> entries,
        IReadOnlyList<BranchLitresParseIssue> issues,
        int rowCount,
        bool success) =>
        new(success, entries, issues, rowCount, entries.Count, rowCount - entries.Count);
}

public sealed record BranchLitresParseIssue(
    ValidationSeverity Severity,
    string ReasonCode,
    string Message,
    SourceReference? SourceReference = null);
