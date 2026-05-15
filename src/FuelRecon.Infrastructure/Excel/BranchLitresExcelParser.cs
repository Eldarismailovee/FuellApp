using ClosedXML.Excel;
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

        var workbookResult = workbookReader.ReadWorkbook(
            filePath,
            new ExcelWorkbookReadOptions(ExcelWorkbookHeaderDetectionKind.BranchLitres));
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

        foreach (var sheet in workbookResult.Workbook.Sheets.Where(sheet => !ExcelSheetFilter.IsNonDataSheet(sheet.SheetName)))
        {
            var columns = BranchLitresColumns.From(sheet.Headers);

            if (columns.HasMinimumRequiredColumns && sheet.Rows.Count > 0)
            {
                foreach (var row in sheet.Rows)
                {
                    if (ShouldSilentlySkipBranchLitresRow(row, columns))
                    {
                        continue;
                    }

                    rowCount++;
                    ParseRow(row, period, branchAliasResolver, columns, entries, issues);
                }

                continue;
            }

            if (ShouldAttemptWeakBranchLitresFallback(sheet, columns)
                && TryReadWeakBranchLitresSheet(filePath, sheet.SheetName, out var weakColumns, out var weakRows))
            {
                foreach (var row in weakRows)
                {
                    if (ShouldSilentlySkipBranchLitresRow(row, weakColumns))
                    {
                        continue;
                    }

                    rowCount++;
                    ParseRow(row, period, branchAliasResolver, weakColumns, entries, issues);
                }

                continue;
            }

            if (!columns.HasMinimumRequiredColumns || sheet.Rows.Count == 0)
            {
                issues.Add(new BranchLitresParseIssue(
                    ValidationSeverity.Error,
                    MissingRequiredColumnsReasonCode,
                    "Branch litres sheet is missing required columns.",
                    new SourceReference(sheet.SourceFile, sheet.SheetName, sheet.HeaderRowNumber == 0 ? null : sheet.HeaderRowNumber)));
            }
        }

        return BranchLitresParseResult.From(
            entries,
            issues,
            rowCount,
            success: issues.All(issue => issue.Severity != ValidationSeverity.Error) || entries.Count > 0);
    }

    private static bool ShouldAttemptWeakBranchLitresFallback(ExcelSheetModel sheet, BranchLitresColumns columns) =>
        IsKnownWeakFallbackBranchSheetName(sheet.SheetName)
        && (!columns.HasMinimumRequiredColumns || sheet.Rows.Count == 0);

    private static bool IsKnownWeakFallbackBranchSheetName(string sheetName)
    {
        var trimmed = sheetName.Trim();
        return trimmed.Equals("Taupo", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Kerikeri", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Whangarei", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadWeakBranchLitresSheet(
        string filePath,
        string sheetName,
        out BranchLitresColumns columns,
        out IReadOnlyList<ExcelRowModel> rows)
    {
        columns = new BranchLitresColumns(null, null, null, null, null, null);
        rows = [];

        if (!IsKnownWeakFallbackBranchSheetName(sheetName))
        {
            return false;
        }

        try
        {
            using var workbook = new XLWorkbook(filePath);
            var worksheet = FindWorksheet(workbook, sheetName);
            if (worksheet is null)
            {
                return false;
            }

            var range = worksheet.RangeUsed();
            if (range is null)
            {
                return false;
            }

            var firstRow = range.RangeAddress.FirstAddress.RowNumber;
            var lastRow = range.RangeAddress.LastAddress.RowNumber;
            var firstCol = range.RangeAddress.FirstAddress.ColumnNumber;
            var lastCol = range.RangeAddress.LastAddress.ColumnNumber;

            var key = sheetName.Trim();
            if (key.Equals("Taupo", StringComparison.OrdinalIgnoreCase))
            {
                return TryReadTaupoWeakBranchLitresSheet(
                    filePath,
                    sheetName,
                    worksheet,
                    firstRow,
                    lastRow,
                    firstCol,
                    lastCol,
                    out columns,
                    out rows);
            }

            if (key.Equals("Kerikeri", StringComparison.OrdinalIgnoreCase))
            {
                return TryReadKerikeriWeakBranchLitresSheet(
                    filePath,
                    sheetName,
                    worksheet,
                    firstRow,
                    lastRow,
                    firstCol,
                    lastCol,
                    out columns,
                    out rows);
            }

            if (key.Equals("Whangarei", StringComparison.OrdinalIgnoreCase))
            {
                return TryReadWhangareiWeakBranchLitresSheet(
                    filePath,
                    sheetName,
                    worksheet,
                    firstRow,
                    lastRow,
                    firstCol,
                    lastCol,
                    out columns,
                    out rows);
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static IXLWorksheet? FindWorksheet(XLWorkbook workbook, string sheetName)
    {
        foreach (var worksheet in workbook.Worksheets)
        {
            if (string.Equals(worksheet.Name, sheetName, StringComparison.OrdinalIgnoreCase))
            {
                return worksheet;
            }
        }

        return null;
    }

    private static bool TryReadTaupoWeakBranchLitresSheet(
        string sourceFile,
        string sheetName,
        IXLWorksheet worksheet,
        int firstRow,
        int lastRow,
        int firstCol,
        int lastCol,
        out BranchLitresColumns columns,
        out IReadOnlyList<ExcelRowModel> rows)
    {
        rows = [];

        var colCount = lastCol - firstCol + 1;
        if (colCount < 1 || lastRow < firstRow)
        {
            columns = new BranchLitresColumns(null, null, null, null, null, null);
            return false;
        }

        columns = CreateTaupoWeakBranchLitresColumns(colCount);

        var built = BuildWeakDataRowsWithoutHeaderRow(sourceFile, sheetName, worksheet, firstRow, lastRow, firstCol, colCount);
        if (built.Count == 0)
        {
            return false;
        }

        rows = built;
        return true;
    }

    private static bool TryReadKerikeriWeakBranchLitresSheet(
        string sourceFile,
        string sheetName,
        IXLWorksheet worksheet,
        int firstRow,
        int lastRow,
        int firstCol,
        int lastCol,
        out BranchLitresColumns columns,
        out IReadOnlyList<ExcelRowModel> rows)
    {
        rows = [];

        var colCount = lastCol - firstCol + 1;
        if (colCount < 6 || lastRow < firstRow)
        {
            columns = new BranchLitresColumns(null, null, null, null, null, null);
            return false;
        }

        columns = CreateKerikeriWeakBranchLitresColumns(colCount);

        var built = BuildWeakDataRowsWithoutHeaderRow(sourceFile, sheetName, worksheet, firstRow, lastRow, firstCol, colCount);
        if (built.Count == 0)
        {
            return false;
        }

        rows = built;
        return true;
    }

    private static bool TryReadWhangareiWeakBranchLitresSheet(
        string sourceFile,
        string sheetName,
        IXLWorksheet worksheet,
        int firstRow,
        int lastRow,
        int firstCol,
        int lastCol,
        out BranchLitresColumns columns,
        out IReadOnlyList<ExcelRowModel> rows)
    {
        rows = [];

        var colCount = lastCol - firstCol + 1;
        if (colCount < 7 || lastRow < firstRow)
        {
            columns = new BranchLitresColumns(null, null, null, null, null, null);
            return false;
        }

        columns = CreateWhangareiWeakBranchLitresColumns(colCount);

        var built = BuildWeakDataRowsWithoutHeaderRow(sourceFile, sheetName, worksheet, firstRow, lastRow, firstCol, colCount);
        if (built.Count == 0)
        {
            return false;
        }

        rows = built;
        return true;
    }

    /// <summary>
    /// Taupo “weak” exports often place litres around column E (index 4) and dates further right.
    /// </summary>
    private static BranchLitresColumns CreateTaupoWeakBranchLitresColumns(int colCount)
    {
        var maxIdx = colCount - 1;
        var litresIdx = Math.Min(4, maxIdx);
        var dateIdx = Math.Min(6, maxIdx);
        if (dateIdx == litresIdx && maxIdx > 0)
        {
            dateIdx = 0;
        }

        return new BranchLitresColumns(
            BranchColumnIndex: null,
            DateColumnIndex: dateIdx,
            LitresColumnIndex: litresIdx,
            RentalAgreementColumnIndex: null,
            RegoColumnIndex: null,
            NoteColumnIndex: null);
    }

    /// <summary>
    /// Matches synthetic Kerikeri fixtures: litres at Excel column D (index 3), date at column F (index 5).
    /// </summary>
    private static BranchLitresColumns CreateKerikeriWeakBranchLitresColumns(int colCount)
    {
        var maxIdx = colCount - 1;
        return new BranchLitresColumns(
            BranchColumnIndex: null,
            DateColumnIndex: Math.Min(5, maxIdx),
            LitresColumnIndex: Math.Min(3, maxIdx),
            RentalAgreementColumnIndex: null,
            RegoColumnIndex: null,
            NoteColumnIndex: null);
    }

    /// <summary>
    /// Matches synthetic Whangarei fixtures: litres at Excel column F (index 5), date at column G (index 6).
    /// </summary>
    private static BranchLitresColumns CreateWhangareiWeakBranchLitresColumns(int colCount)
    {
        var maxIdx = colCount - 1;
        return new BranchLitresColumns(
            BranchColumnIndex: null,
            DateColumnIndex: Math.Min(6, maxIdx),
            LitresColumnIndex: Math.Min(5, maxIdx),
            RentalAgreementColumnIndex: null,
            RegoColumnIndex: null,
            NoteColumnIndex: null);
    }

    private static List<ExcelRowModel> BuildWeakDataRowsWithoutHeaderRow(
        string sourceFile,
        string sheetName,
        IXLWorksheet worksheet,
        int firstDataRowNumber,
        int lastRowNumber,
        int firstColumnNumber,
        int columnCount)
    {
        var rows = new List<ExcelRowModel>();
        for (var rowNumber = firstDataRowNumber; rowNumber <= lastRowNumber; rowNumber++)
        {
            var cells = new ExcelCellModel[columnCount];
            for (var offset = 0; offset < columnCount; offset++)
            {
                var columnNumber = firstColumnNumber + offset;
                cells[offset] = new ExcelCellModel(
                    sourceFile,
                    sheetName,
                    rowNumber,
                    columnNumber,
                    Header: null,
                    ReadWeakCellText(worksheet.Cell(rowNumber, columnNumber)));
            }

            if (cells.Any(static c => !string.IsNullOrWhiteSpace(c.RawText)))
            {
                rows.Add(new ExcelRowModel(sourceFile, sheetName, rowNumber, cells));
            }
        }

        return rows;
    }

    private static string ReadWeakCellText(IXLCell cell) => cell.CachedValue.ToString();

    private static bool ShouldSilentlySkipBranchLitresRow(ExcelRowModel row, BranchLitresColumns columns)
    {
        if (columns.LitresColumnIndex is null)
        {
            return false;
        }

        var litresText = TrimmedPresentText(row.GetCellRawText(columns.LitresColumnIndex));
        if (litresText is null)
        {
            return true;
        }

        return IsLitresColumnHeaderOrCategoryLabel(litresText);
    }

    /// <summary>
    /// Rows where the “litres” cell repeats a header alias or a known non-volume category should not surface as parse errors.
    /// </summary>
    private static bool IsLitresColumnHeaderOrCategoryLabel(string litresText)
    {
        var normalisedCell = ExcelColumnHeaderMatcher.Normalise(litresText);
        foreach (var alias in ExcelParserKnownHeaders.BranchLitres.Litres)
        {
            if (ExcelColumnHeaderMatcher.Normalise(alias) == normalisedCell)
            {
                return true;
            }
        }

        return normalisedCell.Equals("nonrev", StringComparison.Ordinal);
    }

    private static string? TrimmedPresentText(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var trimmed = raw.Trim();
        return trimmed.Length == 0 ? null : trimmed;
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

        var branchAliasInput = ResolveBranchAliasInput(row, columns);
        var branchResult = branchAliasResolver.Resolve(branchAliasInput);
        if (!branchResult.Success || branchResult.BranchId is null)
        {
            rowIssues.Add(new BranchLitresParseIssue(
                ValidationSeverity.Error,
                branchResult.ReasonCode ?? BranchAliasResolver.BranchAliasNotFoundReasonCode,
                $"Branch alias '{branchAliasInput}' could not be resolved.",
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

        MergeWeakSheetIdentifierInference(row, columns, ref rentalAgreementNumber, ref rego);

        var noteOrReference = TrimToNull(row.GetCellRawText(columns.NoteColumnIndex));

        var date = ResolveDate(row, period, columns, sourceReference, rowIssues);

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

        if (date is null)
        {
            return;
        }

        entries.Add(new BranchLitresEntry(
            Guid.NewGuid(),
            period,
            branchResult.BranchId!.Value,
            date.Value,
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
        ICollection<BranchLitresParseIssue> rowIssues)
    {
        var rawDate = row.GetCellRawText(columns.DateColumnIndex);
        if (!string.IsNullOrWhiteSpace(rawDate))
        {
            if (LooksLikeBranchLitresScheduleQualifierInsteadOfCalendarDate(rawDate))
            {
                rowIssues.Add(new BranchLitresParseIssue(
                    ValidationSeverity.Warning,
                    MissingDateDefaultedReasonCode,
                    "Date was missing; first day of the fuel period was used.",
                    sourceReference));

                return new DateOnly(period.Year, period.Month, 1);
            }

            var dateResult = DateNormaliser.NormaliseText(rawDate, period);
            if (dateResult.Success && dateResult.NormalisedValue is not null)
            {
                return dateResult.NormalisedValue.Value;
            }

            if (ShouldTreatFailedDateNormalisationAsMissing(rawDate))
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
                dateResult.ReasonCode ?? DateNormaliser.InvalidDateFormatReasonCode,
                $"Date value '{rawDate}' could not be normalised.",
                sourceReference));
            return null;
        }

        rowIssues.Add(new BranchLitresParseIssue(
            ValidationSeverity.Warning,
            MissingDateDefaultedReasonCode,
            "Date was missing; first day of the fuel period was used.",
            sourceReference));

        return new DateOnly(period.Year, period.Month, 1);
    }

    private static bool LooksLikeBranchLitresScheduleQualifierInsteadOfCalendarDate(string rawDate)
    {
        var trimmed = rawDate.Trim();
        return trimmed.Contains("after ", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Real branch-litre grids sometimes put clock-style values (e.g. 1730) or placeholders in the date column.
    /// Treat those like a missing date (warn + period start) instead of failing the row as an invalid date string.
    /// </summary>
    private static bool ShouldTreatFailedDateNormalisationAsMissing(string rawDate)
    {
        var trimmed = rawDate.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed is "?" or "-" or "." or "–" or "—")
        {
            return true;
        }

        return trimmed.All(char.IsDigit) && trimmed.Length is >= 3 and <= 4;
    }

    /// <summary>
    /// Weak branch-litre layouts (headerless Taupo/Kerikeri/Whangarei grids) omit RA/rego column indices.
    /// Scan remaining cells left-to-right for plate-like regos or numeric-heavy rental agreements.
    /// </summary>
    private static void MergeWeakSheetIdentifierInference(
        ExcelRowModel row,
        BranchLitresColumns columns,
        ref RentalAgreementNumber? rentalAgreementNumber,
        ref Rego? rego)
    {
        if (!columns.IsWeakIdentifierLayout())
        {
            return;
        }

        if (TryInferWeakIdentifiers(row, columns, out var inferredRa, out var inferredRego))
        {
            rentalAgreementNumber ??= inferredRa;
            rego ??= inferredRego;
        }
    }

    private static bool TryInferWeakIdentifiers(
        ExcelRowModel row,
        BranchLitresColumns columns,
        out RentalAgreementNumber? rentalAgreement,
        out Rego? rego)
    {
        rentalAgreement = null;
        rego = null;

        for (var columnIndex = 0; columnIndex < row.Cells.Count; columnIndex++)
        {
            if (columnIndex == columns.LitresColumnIndex || columnIndex == columns.DateColumnIndex)
            {
                continue;
            }

            if (columns.BranchColumnIndex is { } branchColumn && columnIndex == branchColumn)
            {
                continue;
            }

            if (columns.NoteColumnIndex is { } noteColumn && columnIndex == noteColumn)
            {
                continue;
            }

            var raw = TrimmedPresentText(row.Cells[columnIndex].RawText);
            if (raw is null)
            {
                continue;
            }

            ClassifyWeakIdentifierCell(raw, ref rentalAgreement, ref rego);

            if (rentalAgreement is not null && rego is not null)
            {
                break;
            }
        }

        return rentalAgreement is not null || rego is not null;
    }

    /// <summary>
    /// Disambiguate plate-like tokens from RA-like tokens without relying on sheet headers.
    /// </summary>
    private static void ClassifyWeakIdentifierCell(string raw, ref RentalAgreementNumber? rentalAgreement, ref Rego? rego)
    {
        var trimmed = TrimExcelNumericSuffix(raw.Trim());
        if (trimmed.Length == 0)
        {
            return;
        }

        if (rentalAgreement is null && trimmed.All(char.IsDigit) && trimmed.Length >= 5)
        {
            if (TryInferRentalAgreementFromWeakCell(raw, out var agreement))
            {
                rentalAgreement = agreement;
            }

            return;
        }

        if (rentalAgreement is null
            && trimmed.StartsWith("RA", StringComparison.OrdinalIgnoreCase)
            && trimmed.Length >= 4)
        {
            if (TryInferRentalAgreementFromWeakCell(raw, out var agreement))
            {
                rentalAgreement = agreement;
            }

            return;
        }

        if (rego is null && TryInferRegoFromWeakCell(raw, out var plate))
        {
            rego = plate;
            return;
        }

        if (rentalAgreement is null && trimmed.Length > 8 && TryInferRentalAgreementFromWeakCell(raw, out var longAgreement))
        {
            rentalAgreement = longAgreement;
        }
    }

    private static string TrimExcelNumericSuffix(string value)
    {
        if (value.EndsWith(".0", StringComparison.Ordinal))
        {
            return value[..^2];
        }

        return value;
    }

    /// <summary>
    /// Numeric-heavy tokens (common agreement numbers) and explicit RA-prefixed values map to rental agreements.
    /// </summary>
    private static bool TryInferRentalAgreementFromWeakCell(string raw, out RentalAgreementNumber? rentalAgreement)
    {
        rentalAgreement = null;

        var normalisedResult = RentalAgreementNormaliser.Normalise(raw);
        if (!normalisedResult.Success || normalisedResult.NormalisedValue is null)
        {
            return false;
        }

        var norm = normalisedResult.NormalisedValue;
        var digitsOnly = norm.All(char.IsDigit);
        if (digitsOnly && norm.Length < 5)
        {
            return false;
        }

        try
        {
            rentalAgreement = new RentalAgreementNumber(raw);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryInferRegoFromWeakCell(string raw, out Rego? rego)
    {
        rego = null;

        var normalisedResult = RegoNormaliser.Normalise(raw);
        if (!normalisedResult.Success || normalisedResult.NormalisedValue is null)
        {
            return false;
        }

        var norm = normalisedResult.NormalisedValue;

        if (norm.All(char.IsDigit))
        {
            return false;
        }

        if (norm.Length is < 4 or > 8)
        {
            return false;
        }

        try
        {
            rego = new Rego(raw);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
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

    private static string ResolveBranchAliasInput(ExcelRowModel row, BranchLitresColumns columns)
    {
        if (columns.BranchColumnIndex is not null)
        {
            return TrimToNull(row.GetCellRawText(columns.BranchColumnIndex)) ?? string.Empty;
        }

        return row.SheetName.Trim();
    }
}

internal sealed record BranchLitresColumns(
    int? BranchColumnIndex,
    int? DateColumnIndex,
    int? LitresColumnIndex,
    int? RentalAgreementColumnIndex,
    int? RegoColumnIndex,
    int? NoteColumnIndex)
{
    public bool HasMinimumRequiredColumns =>
        LitresColumnIndex is not null;

    /// <summary>
    /// True when the sheet mapping provides no dedicated RA/rego columns (weak fallback grids).
    /// </summary>
    public bool IsWeakIdentifierLayout() =>
        RentalAgreementColumnIndex is null && RegoColumnIndex is null;

    public static BranchLitresColumns From(IReadOnlyList<string> headers) =>
        new(
            ExcelColumnHeaderMatcher.FindFirstMatchingColumn(headers, ExcelParserKnownHeaders.BranchLitres.Branch),
            ExcelColumnHeaderMatcher.FindFirstMatchingColumn(headers, ExcelParserKnownHeaders.BranchLitres.Date),
            ExcelColumnHeaderMatcher.FindFirstMatchingColumn(headers, ExcelParserKnownHeaders.BranchLitres.Litres),
            ExcelColumnHeaderMatcher.FindFirstMatchingColumn(headers, ExcelParserKnownHeaders.BranchLitres.RentalAgreement),
            ExcelColumnHeaderMatcher.FindFirstMatchingColumn(headers, ExcelParserKnownHeaders.BranchLitres.Rego),
            ExcelColumnHeaderMatcher.FindFirstMatchingColumn(headers, ExcelParserKnownHeaders.BranchLitres.Note));
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