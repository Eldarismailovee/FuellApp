using FuelRecon.Application.Excel;
using FuelRecon.Domain;

namespace FuelRecon.Infrastructure.Excel;

public sealed class CarsBillingExcelParser(IExcelWorkbookReader workbookReader) : ICarsBillingExcelParser
{
    public const string MissingRequiredColumnsReasonCode = "MissingRequiredColumns";

    public const string MissingIdentifierReasonCode = "MissingIdentifier";

    public const string MissingBillingValueReasonCode = "MissingCharge";

    public CarsBillingParseResult Parse(string filePath, FuelPeriod period, BranchAliasResolver branchAliasResolver)
    {
        ArgumentNullException.ThrowIfNull(branchAliasResolver);

        var workbookResult = workbookReader.ReadWorkbook(
            filePath,
            new ExcelWorkbookReadOptions(ExcelWorkbookHeaderDetectionKind.CarsBilling));
        if (!workbookResult.Success || workbookResult.Workbook is null)
        {
            return CarsBillingParseResult.From(
                [],
                [
                    new CarsBillingParseIssue(
                        ValidationSeverity.Error,
                        workbookResult.ReasonCode ?? "WorkbookReadFailed",
                        workbookResult.Message)
                ],
                rowCount: 0,
                success: false);
        }

        var entries = new List<CarsBillingEntry>();
        var issues = new List<CarsBillingParseIssue>();
        var rowCount = 0;

        foreach (var sheet in workbookResult.Workbook.Sheets.Where(sheet => !ExcelSheetFilter.IsNonDataSheet(sheet.SheetName)))
        {
            var columns = CarsBillingColumns.From(sheet.Headers);
            if (!columns.HasMinimumRequiredColumns)
            {
                issues.Add(new CarsBillingParseIssue(
                    ValidationSeverity.Error,
                    MissingRequiredColumnsReasonCode,
                    "Cars+ sheet is missing required identifier or billing columns.",
                    new SourceReference(sheet.SourceFile, sheet.SheetName, sheet.HeaderRowNumber == 0 ? null : sheet.HeaderRowNumber)));
                continue;
            }

            foreach (var row in sheet.Rows)
            {
                rowCount++;
                ParseRow(row, period, branchAliasResolver, columns, entries, issues);
            }
        }

        return CarsBillingParseResult.From(
            entries,
            issues,
            rowCount,
            success: issues.All(issue => issue.Severity != ValidationSeverity.Error) || entries.Count > 0);
    }

    private static void ParseRow(
        ExcelRowModel row,
        FuelPeriod period,
        BranchAliasResolver branchAliasResolver,
        CarsBillingColumns columns,
        ICollection<CarsBillingEntry> entries,
        ICollection<CarsBillingParseIssue> issues)
    {
        var sourceReference = new SourceReference(row.SourceFile, row.SheetName, row.RowNumber);
        var rowIssues = new List<CarsBillingParseIssue>();

        var branchId = ResolveBranch(row, branchAliasResolver, columns, sourceReference, rowIssues);
        var date = ResolveDate(row, columns, sourceReference, rowIssues);
        var rentalAgreementNumber = NormaliseRentalAgreement(row.GetCellRawText(columns.RentalAgreementColumnIndex), sourceReference, rowIssues);
        var rego = NormaliseRego(row.GetCellRawText(columns.RegoColumnIndex), sourceReference, rowIssues);
        var billedLitres = NormaliseLitres(row.GetCellRawText(columns.BilledLitresColumnIndex), sourceReference, rowIssues);
        var billedAmount = NormaliseAmount(row.GetCellRawText(columns.BilledAmountColumnIndex), sourceReference, rowIssues);
        var billingStatus = TrimToNull(row.GetCellRawText(columns.StatusColumnIndex));

        if (rentalAgreementNumber is null && rego is null)
        {
            rowIssues.Add(new CarsBillingParseIssue(
                ValidationSeverity.Error,
                MissingIdentifierReasonCode,
                "Cars+ row requires at least one identifier: RA or rego.",
                sourceReference));
        }

        if (billedLitres is null && billedAmount is null && string.IsNullOrWhiteSpace(billingStatus))
        {
            rowIssues.Add(new CarsBillingParseIssue(
                ValidationSeverity.Error,
                MissingBillingValueReasonCode,
                "Cars+ row requires billed litres, billed amount or billing status.",
                sourceReference));
        }

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

        entries.Add(new CarsBillingEntry(
            Guid.NewGuid(),
            period,
            sourceReference,
            branchId,
            date,
            rentalAgreementNumber,
            rego,
            billedLitres,
            billedAmount,
            billingStatus,
            rowIssues.Select(issue => issue.ReasonCode)));
    }

    private static CanonicalBranchId? ResolveBranch(
        ExcelRowModel row,
        BranchAliasResolver branchAliasResolver,
        CarsBillingColumns columns,
        SourceReference sourceReference,
        ICollection<CarsBillingParseIssue> rowIssues)
    {
        var rawBranch = row.GetCellRawText(columns.BranchColumnIndex);
        if (string.IsNullOrWhiteSpace(rawBranch))
        {
            return null;
        }

        var branchResult = branchAliasResolver.Resolve(rawBranch);
        if (branchResult.Success && branchResult.BranchId is not null)
        {
            return branchResult.BranchId.Value;
        }

        rowIssues.Add(new CarsBillingParseIssue(
            ValidationSeverity.Error,
            branchResult.ReasonCode ?? BranchAliasResolver.BranchAliasNotFoundReasonCode,
            $"Branch alias '{rawBranch}' could not be resolved.",
            sourceReference));
        return null;
    }

    private static DateOnly? ResolveDate(
        ExcelRowModel row,
        CarsBillingColumns columns,
        SourceReference sourceReference,
        ICollection<CarsBillingParseIssue> rowIssues)
    {
        var rawDate = row.GetCellRawText(columns.DateColumnIndex);
        if (string.IsNullOrWhiteSpace(rawDate))
        {
            return null;
        }

        var dateResult = DateNormaliser.NormaliseText(rawDate);
        if (dateResult.Success && dateResult.NormalisedValue is not null)
        {
            return dateResult.NormalisedValue.Value;
        }

        rowIssues.Add(new CarsBillingParseIssue(
            ValidationSeverity.Error,
            dateResult.ReasonCode ?? DateNormaliser.InvalidDateFormatReasonCode,
            $"Date value '{rawDate}' could not be normalised.",
            sourceReference));
        return null;
    }

    private static RentalAgreementNumber? NormaliseRentalAgreement(
        string? rawValue,
        SourceReference sourceReference,
        ICollection<CarsBillingParseIssue> rowIssues)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        var result = RentalAgreementNormaliser.Normalise(rawValue);
        if (!result.Success || result.NormalisedValue is null)
        {
            rowIssues.Add(new CarsBillingParseIssue(
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
            rowIssues.Add(new CarsBillingParseIssue(
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
        ICollection<CarsBillingParseIssue> rowIssues)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        var result = RegoNormaliser.Normalise(rawValue);
        if (!result.Success || result.NormalisedValue is null)
        {
            rowIssues.Add(new CarsBillingParseIssue(
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
            rowIssues.Add(new CarsBillingParseIssue(
                ValidationSeverity.Error,
                RegoNormaliser.InvalidReasonCode,
                $"Rego value '{rawValue}' could not be normalised.",
                sourceReference));
            return null;
        }
    }

    private static Litres? NormaliseLitres(
        string? rawValue,
        SourceReference sourceReference,
        ICollection<CarsBillingParseIssue> rowIssues)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        var result = LitresNormaliser.Normalise(rawValue);
        if (result.Success && result.NormalisedValue is not null)
        {
            return result.NormalisedValue.Value;
        }

        rowIssues.Add(new CarsBillingParseIssue(
            ValidationSeverity.Error,
            result.ReasonCode ?? LitresNormaliser.FailureReasonCode,
            $"Billed litres value '{rawValue}' could not be normalised.",
            sourceReference));
        return null;
    }

    private static MoneyAmount? NormaliseAmount(
        string? rawValue,
        SourceReference sourceReference,
        ICollection<CarsBillingParseIssue> rowIssues)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        var result = MoneyAmountNormaliser.Normalise(rawValue);
        if (result.Success && result.NormalisedValue is not null)
        {
            return result.NormalisedValue.Value;
        }

        rowIssues.Add(new CarsBillingParseIssue(
            ValidationSeverity.Error,
            result.ReasonCode ?? MoneyAmountNormaliser.FailureReasonCode,
            $"Billed amount value '{rawValue}' could not be normalised.",
            sourceReference));
        return null;
    }

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed record CarsBillingColumns(
    int? BranchColumnIndex,
    int? DateColumnIndex,
    int? RentalAgreementColumnIndex,
    int? RegoColumnIndex,
    int? BilledLitresColumnIndex,
    int? BilledAmountColumnIndex,
    int? StatusColumnIndex)
{
    public bool HasMinimumRequiredColumns =>
        (RentalAgreementColumnIndex is not null || RegoColumnIndex is not null)
        && (BilledLitresColumnIndex is not null || BilledAmountColumnIndex is not null || StatusColumnIndex is not null);

    public static CarsBillingColumns From(IReadOnlyList<string> headers) =>
        new(
            ExcelColumnHeaderMatcher.FindFirstMatchingColumn(headers, ExcelParserKnownHeaders.CarsBilling.Branch),
            ExcelColumnHeaderMatcher.FindFirstMatchingColumn(headers, ExcelParserKnownHeaders.CarsBilling.Date),
            ExcelColumnHeaderMatcher.FindFirstMatchingColumn(headers, ExcelParserKnownHeaders.CarsBilling.RentalAgreement),
            ExcelColumnHeaderMatcher.FindFirstMatchingColumn(headers, ExcelParserKnownHeaders.CarsBilling.Rego),
            ExcelColumnHeaderMatcher.FindFirstMatchingColumn(headers, ExcelParserKnownHeaders.CarsBilling.BilledLitres),
            ExcelColumnHeaderMatcher.FindFirstMatchingColumn(headers, ExcelParserKnownHeaders.CarsBilling.BilledAmount),
            ExcelColumnHeaderMatcher.FindFirstMatchingColumn(headers, ExcelParserKnownHeaders.CarsBilling.Status));
}
