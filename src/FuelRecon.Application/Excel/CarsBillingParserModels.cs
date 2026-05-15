using FuelRecon.Domain;

namespace FuelRecon.Application.Excel;

public interface ICarsBillingExcelParser
{
    CarsBillingParseResult Parse(string filePath, FuelPeriod period, BranchAliasResolver branchAliasResolver);
}

public sealed record CarsBillingParseResult(
    bool Success,
    IReadOnlyList<CarsBillingEntry> Entries,
    IReadOnlyList<CarsBillingParseIssue> Issues,
    int RowCount,
    int ValidRowCount,
    int SkippedRowCount)
{
    public bool HasErrors => Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public bool HasWarnings => Issues.Any(issue => issue.Severity == ValidationSeverity.Warning);

    public static CarsBillingParseResult From(
        IReadOnlyList<CarsBillingEntry> entries,
        IReadOnlyList<CarsBillingParseIssue> issues,
        int rowCount,
        bool success) =>
        new(success, entries, issues, rowCount, entries.Count, rowCount - entries.Count);
}

public sealed record CarsBillingParseIssue(
    ValidationSeverity Severity,
    string ReasonCode,
    string Message,
    SourceReference? SourceReference = null);
