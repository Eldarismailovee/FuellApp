using FuelRecon.Application.Excel;
using FuelRecon.Domain;

namespace FuelRecon.Infrastructure.Excel;

public sealed class BranchLitresExcelParser(IExcelWorkbookReader workbookReader) : IBranchLitresExcelParser
{
    public const string MissingRequiredColumnsReasonCode = "MissingRequiredColumns";

    public const string MissingDateDefaultedReasonCode = "MissingDateDefaultedToPeriodStart";

    public BranchLitresParseResult Parse(string filePath, FuelPeriod period, BranchAliasResolver branchAliasResolver)
    {
        ArgumentNullException.ThrowIfNull(branchAliasResolver);

        var workbookResult = workbookReader.ReadWorkbook(filePath);
        if (!workbookResult.Success || workbookResult.Workbook is null)
        {
            return BranchLitresParseResult.From(
                [],
                [
                    new BranchLitresParseIssue(
                        ValidationSeverity.Error,
                        workbookResult.ReasonCode ?? "WorkbookReadFailed",
                        workbookResult.Message)
                ],
                rowCount: 0,
                success: false);
        }

        var entries = new List<BranchLitresEntry>();
        var issues = new List<BranchLitresParseIssue>();
        var rowCount = 0;

        foreach (var sheet in workbookResult.Workbook.Sheets)
        {
            var columns = BranchLitresColumns.From(sheet.Headers);
            if (!columns.HasMinimumRequiredColumns)
            {
                issues.Add(new BranchLitresParseIssue(
                    ValidationSeverity.Error,
                    MissingRequiredColumnsReasonCode,
                    "Branch litres sheet is missing required columns.",
                    new SourceReference(sheet.SourceFile, sheet.SheetName, sheet.HeaderRowNumber == 0 ? null : sheet.HeaderRowNumber)));
                continue;
            }

            foreach (var row in sheet.Rows)
            {
                rowCount++;
                ParseRow(row, period, branchAliasResolver, columns, entries, issues);
            }
        }

        return BranchLitresParseResult.From(
            entries,
            issues,
            rowCount,
            success: issues.All(issue => issue.Severity != ValidationSeverity.Error) || entries.Count > 0);
    }

    private static void ParseRow(
        ExcelRowModel row,
        FuelPeriod period,
        BranchAliasResolver branchAliasResolver,
        BranchLitresColumns columns,
        ICollection<BranchLitresEntry> entries,
        ICollection<BranchLitresParseIssue> issues)
    {
        var sourceReference = new SourceReference(row.SourceFile, row.SheetName, row.RowNumber);
        var rowIssues = new List<BranchLitresParseIssue>();

        var rawBranch = row.GetCellRawText(columns.BranchColumnIndex);
        var branchResult = branchAliasResolver.Resolve(rawBranch);
        if (!branchResult.Success || branchResult.BranchId is null)
        {
            rowIssues.Add(new BranchLitresParseIssue(
                ValidationSeverity.Error,
                branchResult.ReasonCode ?? BranchAliasResolver.BranchAliasNotFoundReasonCode,
                $"Branch alias '{rawBranch}' could not be resolved.",
                sourceReference));
        }

        var rawLitres = row.GetCellRawText(columns.LitresColumnIndex);
        var litresResult = LitresNormaliser.Normalise(rawLitres);
        if (!litresResult.Success || litresResult.NormalisedValue is null)
        {
            rowIssues.Add(new BranchLitresParseIssue(
                ValidationSeverity.Error,
                litresResult.ReasonCode ?? LitresNormaliser.FailureReasonCode,
                $"Litres value '{rawLitres}' could not be normalised.",
                sourceReference));
        }

        var rawRa = row.GetCellRawText(columns.RentalAgreementColumnIndex);
        var rentalAgreementNumber = NormaliseRentalAgreement(rawRa, sourceReference, rowIssues);

        var rawRego = row.GetCellRawText(columns.RegoColumnIndex);
        var rego = NormaliseRego(rawRego, sourceReference, rowIssues);

        var noteOrReference = TrimToNull(row.GetCellRawText(columns.NoteColumnIndex));

        var date = ResolveDate(row, period, columns, sourceReference, rentalAgreementNumber, rego, noteOrReference, rowIssues);

        if (rowIssues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            foreach (var issue in rowIssues)
            {
                issues.Add(issue);
            }

            return;
        }

        foreach (var issue in rowIssues)
        {
            issues.Add(issue);
        }

        entries.Add(new BranchLitresEntry(
            Guid.NewGuid(),
            period,
            branchResult.BranchId!.Value,
            date!.Value,
            litresResult.NormalisedValue!.Value,
            sourceReference,
            rentalAgreementNumber,
            rego,
            noteOrReference,
            rowIssues.Select(issue => issue.ReasonCode)));
    }

    private static DateOnly? ResolveDate(
        ExcelRowModel row,
        FuelPeriod period,
        BranchLitresColumns columns,
        SourceReference sourceReference,
        RentalAgreementNumber? rentalAgreementNumber,
        Rego? rego,
        string? noteOrReference,
        ICollection<BranchLitresParseIssue> rowIssues)
    {
        var rawDate = row.GetCellRawText(columns.DateColumnIndex);
        if (!string.IsNullOrWhiteSpace(rawDate))
        {
            var dateResult = DateNormaliser.NormaliseText(rawDate);
            if (dateResult.Success && dateResult.NormalisedValue is not null)
            {
                return dateResult.NormalisedValue.Value;
            }

            rowIssues.Add(new BranchLitresParseIssue(
                ValidationSeverity.Error,
                dateResult.ReasonCode ?? DateNormaliser.InvalidDateFormatReasonCode,
                $"Date value '{rawDate}' could not be normalised.",
                sourceReference));
            return null;
        }

        if (rentalAgreementNumber is not null || rego is not null || !string.IsNullOrWhiteSpace(noteOrReference))
        {
            rowIssues.Add(new BranchLitresParseIssue(
                ValidationSeverity.Warning,
                MissingDateDefaultedReasonCode,
                "Date was missing; first day of the fuel period was used.",
                sourceReference));
            return new DateOnly(period.Year, period.Month, 1);
        }

        rowIssues.Add(new BranchLitresParseIssue(
            ValidationSeverity.Error,
            DateNormaliser.InvalidDateFormatReasonCode,
            "Branch litres row requires a date or at least one identifier/reference.",
            sourceReference));
        return null;
    }

    private static RentalAgreementNumber? NormaliseRentalAgreement(
        string? rawValue,
        SourceReference sourceReference,
        ICollection<BranchLitresParseIssue> rowIssues)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        var result = RentalAgreementNormaliser.Normalise(rawValue);
        if (!result.Success || result.NormalisedValue is null)
        {
            rowIssues.Add(new BranchLitresParseIssue(
                ValidationSeverity.Error,
                result.ReasonCode ?? RentalAgreementNormaliser.InvalidReasonCode,
                $"Rental agreement value '{rawValue}' could not be normalised.",
                sourceReference));
            return null;
        }

        try
        {
            return new RentalAgreementNumber(rawValue);
        }
        catch (ArgumentException)
        {
            rowIssues.Add(new BranchLitresParseIssue(
                ValidationSeverity.Error,
                RentalAgreementNormaliser.InvalidReasonCode,
                $"Rental agreement value '{rawValue}' could not be normalised.",
                sourceReference));
            return null;
        }
    }

    private static Rego? NormaliseRego(
        string? rawValue,
        SourceReference sourceReference,
        ICollection<BranchLitresParseIssue> rowIssues)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        var result = RegoNormaliser.Normalise(rawValue);
        if (!result.Success || result.NormalisedValue is null)
        {
            rowIssues.Add(new BranchLitresParseIssue(
                ValidationSeverity.Error,
                result.ReasonCode ?? RegoNormaliser.InvalidReasonCode,
                $"Rego value '{rawValue}' could not be normalised.",
                sourceReference));
            return null;
        }

        try
        {
            return new Rego(rawValue);
        }
        catch (ArgumentException)
        {
            rowIssues.Add(new BranchLitresParseIssue(
                ValidationSeverity.Error,
                RegoNormaliser.InvalidReasonCode,
                $"Rego value '{rawValue}' could not be normalised.",
                sourceReference));
            return null;
        }
    }

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed record BranchLitresColumns(
    int? BranchColumnIndex,
    int? DateColumnIndex,
    int? LitresColumnIndex,
    int? RentalAgreementColumnIndex,
    int? RegoColumnIndex,
    int? NoteColumnIndex)
{
    private static readonly string[] BranchHeaders = ["Branch", "Location", "Depot"];
    private static readonly string[] DateHeaders = ["Date", "Fuel Date", "Transaction Date"];
    private static readonly string[] LitresHeaders = ["Litres", "L", "Qty", "Quantity"];
    private static readonly string[] RentalAgreementHeaders = ["RA", "Rental Agreement", "Rental Agreement Number"];
    private static readonly string[] RegoHeaders = ["Rego", "Registration", "Plate"];
    private static readonly string[] NoteHeaders = ["Note", "Notes", "Reference", "Description"];

    public bool HasMinimumRequiredColumns =>
        BranchColumnIndex is not null
        && LitresColumnIndex is not null
        && (DateColumnIndex is not null
            || RentalAgreementColumnIndex is not null
            || RegoColumnIndex is not null
            || NoteColumnIndex is not null);

    public static BranchLitresColumns From(IReadOnlyList<string> headers) =>
        new(
            FindHeaderIndex(headers, BranchHeaders),
            FindHeaderIndex(headers, DateHeaders),
            FindHeaderIndex(headers, LitresHeaders),
            FindHeaderIndex(headers, RentalAgreementHeaders),
            FindHeaderIndex(headers, RegoHeaders),
            FindHeaderIndex(headers, NoteHeaders));

    private static int? FindHeaderIndex(IReadOnlyList<string> headers, IReadOnlyCollection<string> variants)
    {
        for (var index = 0; index < headers.Count; index++)
        {
            if (variants.Any(variant => string.Equals(headers[index].Trim(), variant, StringComparison.OrdinalIgnoreCase)))
            {
                return index;
            }
        }

        return null;
    }
}

internal static class ExcelRowModelExtensions
{
    internal static string? GetCellRawText(this ExcelRowModel row, int? zeroBasedColumnIndex)
    {
        if (zeroBasedColumnIndex is null || zeroBasedColumnIndex.Value >= row.Cells.Count)
        {
            return null;
        }

        return row.Cells[zeroBasedColumnIndex.Value].RawText;
    }
}
