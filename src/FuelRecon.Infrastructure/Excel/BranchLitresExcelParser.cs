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

    /// <summary>
    /// Re-opens the workbook when <see cref="ClosedXmlExcelWorkbookReader"/> could not detect a branch-litres header row,
    /// using conservative sheet/name-specific column heuristics for real client layouts.
    /// </summary>
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
        columns = new BranchLitresColumns(null, null, null, null, null, null);
        rows = [];

        var colCount = lastCol - firstCol + 1;
        if (colCount < 1 || lastRow <= firstRow)
        {
            return false;
        }

        var scanEnd = Math.Min(lastRow, firstRow + ExcelHeaderRowDetector.MaxRowsToScan - 1);
        int? headerRowNumber = null;
        for (var r = firstRow; r <= scanEnd; r++)
        {
            var texts = ReadWeakRowTexts(worksheet, r, firstCol, lastCol);
            var mapped = BranchLitresColumns.From(texts);
            if (mapped.LitresColumnIndex is not null)
            {
                headerRowNumber = r;
                break;
            }
        }

        if (headerRowNumber is null)
        {
            return false;
        }

        var headerTexts = ReadWeakRowTexts(worksheet, headerRowNumber.Value, firstCol, lastCol);
        columns = BranchLitresColumns.From(headerTexts);

        if (columns.LitresColumnIndex is null && colCount > 4)
        {
            columns = columns with { LitresColumnIndex = Math.Min(4, colCount - 1) };
        }

        if (columns.LitresColumnIndex is null)
        {
            return false;
        }

        if (columns.DateColumnIndex is null && headerRowNumber.Value < lastRow)
        {
            var firstDataTexts = ReadWeakRowTexts(worksheet, headerRowNumber.Value + 1, firstCol, lastCol);
            var dateIx = FindFirstDateColumnIndex(firstDataTexts);
            if (dateIx is not null)
            {
                columns = columns with { DateColumnIndex = dateIx };
            }
        }

        var headerLabels = headerTexts.Select(static h => string.IsNullOrWhiteSpace(h) ? string.Empty : h.Trim()).ToArray();
        var built = BuildWeakBranchLitresRowsAfterHeader(
            sourceFile,
            sheetName,
            worksheet,
            headerRowNumber.Value,
            lastRow,
            firstCol,
            lastCol,
            headerLabels);

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
        columns = new BranchLitresColumns(null, null, null, null, null, null);
        rows = [];

        var colCount = lastCol - firstCol + 1;
        if (colCount < 3 || lastRow < firstRow)
        {
            return false;
        }

        var probeEnd = Math.Min(lastRow, firstRow + 25);
        var litresColumnIndex = Math.Min(3, colCount - 1);
        var foundLitres = false;

        for (var r = firstRow; r <= probeEnd; r++)
        {
            var texts = ReadWeakRowTexts(worksheet, r, firstCol, lastCol);
            if (!texts.Any(static t => !string.IsNullOrWhiteSpace(t)))
            {
                continue;
            }

            if (TryPickKerikeriLitresColumn(texts, colCount, out litresColumnIndex))
            {
                foundLitres = true;
                break;
            }
        }

        if (!foundLitres)
        {
            litresColumnIndex = colCount > 4 ? Math.Min(3, colCount - 1) : Math.Min(2, colCount - 1);
        }

        int? dateColumnIndex = null;
        int? raColumnIndex = null;
        for (var r = firstRow; r <= probeEnd; r++)
        {
            var texts = ReadWeakRowTexts(worksheet, r, firstCol, lastCol);
            if (!texts.Any(static t => !string.IsNullOrWhiteSpace(t)))
            {
                continue;
            }

            dateColumnIndex = FindFirstDateColumnIndex(texts);
            if (colCount > 0 && !string.IsNullOrWhiteSpace(texts[0]))
            {
                var raProbe = RentalAgreementNormaliser.Normalise(texts[0].Trim());
                if (raProbe.Success)
                {
                    raColumnIndex = 0;
                }
            }

            break;
        }

        columns = new BranchLitresColumns(null, dateColumnIndex, litresColumnIndex, raColumnIndex, null, null);

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
        columns = new BranchLitresColumns(null, null, null, null, null, null);
        rows = [];

        var colCount = lastCol - firstCol + 1;
        if (colCount < 2 || lastRow < firstRow)
        {
            return false;
        }

        var probeEnd = Math.Min(lastRow, firstRow + 25);
        int? dateColumnIndex = null;
        var litresColumnIndex = Math.Min(5, colCount - 1);

        for (var r = firstRow; r <= probeEnd; r++)
        {
            var texts = ReadWeakRowTexts(worksheet, r, firstCol, lastCol);
            if (!texts.Any(static t => !string.IsNullOrWhiteSpace(t)))
            {
                continue;
            }

            dateColumnIndex = FindFirstDateColumnIndex(texts);
            TryPickWhangareiLitresColumn(texts, dateColumnIndex, colCount, out litresColumnIndex);
            break;
        }

        columns = new BranchLitresColumns(null, dateColumnIndex, litresColumnIndex, null, null, null);

        var built = BuildWeakDataRowsWithoutHeaderRow(sourceFile, sheetName, worksheet, firstRow, lastRow, firstCol, colCount);
        if (built.Count == 0)
        {
            return false;
        }

        rows = built;
        return true;
    }

    private static bool TryPickKerikeriLitresColumn(string[] texts, int colCount, out int litresColumnIndex)
    {
        litresColumnIndex = Math.Min(3, colCount - 1);
        if (colCount > 3 && TryLitresNormalisePlausible(texts[3], out _))
        {
            litresColumnIndex = 3;
            return true;
        }

        if (colCount > 2 && TryLitresNormalisePlausible(texts[2], out _))
        {
            litresColumnIndex = 2;
            return true;
        }

        return false;
    }

    private static void TryPickWhangareiLitresColumn(
        string[] texts,
        int? dateColumnIndex,
        int colCount,
        out int litresColumnIndex)
    {
        litresColumnIndex = Math.Min(5, colCount - 1);
        var preferred = new[] { 5, 4, 3, 2, 1, 0 };
        foreach (var ix in preferred)
        {
            if (ix >= colCount)
            {
                continue;
            }

            if (dateColumnIndex is not null && ix == dateColumnIndex.Value)
            {
                continue;
            }

            if (!TryLitresNormalisePlausible(texts[ix], out var litres))
            {
                continue;
            }

            if (litres is > 0 and <= 50_000m)
            {
                litresColumnIndex = ix;
                return;
            }
        }
    }

    private static List<ExcelRowModel> BuildWeakBranchLitresRowsAfterHeader(
        string sourceFile,
        string sheetName,
        IXLWorksheet worksheet,
        int headerRowNumber,
        int lastRowNumber,
        int firstColumnNumber,
        int lastColumnNumber,
        IReadOnlyList<string> headers)
    {
        var columnCount = headers.Count;
        var rows = new List<ExcelRowModel>();
        for (var rowNumber = headerRowNumber + 1; rowNumber <= lastRowNumber; rowNumber++)
        {
            var cells = new ExcelCellModel[columnCount];
            for (var offset = 0; offset < columnCount; offset++)
            {
                var columnNumber = firstColumnNumber + offset;
                var headerLabel = string.IsNullOrWhiteSpace(headers[offset]) ? null : headers[offset].Trim();
                cells[offset] = new ExcelCellModel(
                    sourceFile,
                    sheetName,
                    rowNumber,
                    columnNumber,
                    headerLabel,
                    ReadWeakCellText(worksheet.Cell(rowNumber, columnNumber)));
            }

            if (cells.Any(static cell => !string.IsNullOrWhiteSpace(cell.RawText)))
            {
                rows.Add(new ExcelRowModel(sourceFile, sheetName, rowNumber, cells));
            }
        }

        return rows;
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

    private static string[] ReadWeakRowTexts(IXLWorksheet worksheet, int rowNumber, int firstCol, int lastCol)
    {
        var count = lastCol - firstCol + 1;
        var texts = new string[count];
        for (var i = 0; i < count; i++)
        {
            texts[i] = ReadWeakCellText(worksheet.Cell(rowNumber, firstCol + i));
        }

        return texts;
    }

    private static string ReadWeakCellText(IXLCell cell) => cell.CachedValue.ToString();

    private static int? FindFirstDateColumnIndex(IReadOnlyList<string> cellTexts)
    {
        for (var i = 0; i < cellTexts.Count; i++)
        {
            var raw = cellTexts[i]?.Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var result = DateNormaliser.NormaliseText(raw);
            if (result.Success && result.NormalisedValue is not null)
            {
                return i;
            }
        }

        return null;
    }

    private static bool TryLitresNormalisePlausible(string? raw, out decimal litres)
    {
        litres = 0m;
        var result = LitresNormaliser.Normalise(raw);
        if (!result.Success || result.NormalisedValue is null)
        {
            return false;
        }

        var value = result.NormalisedValue.Value.Value;
        if (value is <= 0 or > 100_000m)
        {
            return false;
        }

        litres = value;
        return true;
    }

    /// <summary>
    /// Rows that are effectively blank for transactional purposes are ignored without emitting validation noise
    /// (e.g. trailing empty rows or branch-only placeholders).
    /// </summary>
    private static bool ShouldSilentlySkipBranchLitresRow(ExcelRowModel row, BranchLitresColumns columns)
    {
        if (columns.LitresColumnIndex is null)
        {
            return false;
        }

        var litresText = TrimmedPresentText(row.GetCellRawText(columns.LitresColumnIndex));
        var dateText = TrimmedPresentText(row.GetCellRawText(columns.DateColumnIndex));
        var raText = TrimmedPresentText(row.GetCellRawText(columns.RentalAgreementColumnIndex));
        var regoText = TrimmedPresentText(row.GetCellRawText(columns.RegoColumnIndex));
        var noteText = TrimmedPresentText(row.GetCellRawText(columns.NoteColumnIndex));

        var hasTransactionalContent =
            dateText is not null
            || raText is not null
            || regoText is not null
            || noteText is not null;

        if (litresText is null && !hasTransactionalContent)
        {
            return true;
        }

        return false;
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

    /// <summary>
    /// When a branch column exists, its cell value is used (including empty, which fails resolution as before).
    /// When it is absent, the worksheet name is used as the branch alias for each row.
    /// </summary>
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
    /// <summary>
    /// Litres column is required. Branch/location columns are optional (sheet name is the alias when absent).
    /// Date, RA, rego, and note columns are optional in the header row; individual rows must still supply a parseable date or at least one identifier/reference where applicable.
    /// </summary>
    public bool HasMinimumRequiredColumns =>
        LitresColumnIndex is not null;

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
