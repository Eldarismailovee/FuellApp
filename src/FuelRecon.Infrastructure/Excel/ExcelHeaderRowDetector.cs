using ClosedXML.Excel;

namespace FuelRecon.Infrastructure.Excel;

internal static class ExcelHeaderRowDetector
{
    internal const int MaxRowsToScan = 80;

    internal static int? FindBranchLitresHeaderRow(
        IXLWorksheet worksheet,
        int firstRowNumber,
        int lastRowNumber,
        int firstColumnNumber,
        int lastColumnNumber)
    {
        var scanEnd = Math.Min(lastRowNumber, firstRowNumber + MaxRowsToScan - 1);

        int? bestRow = null;
        var bestScore = int.MinValue;

        for (var rowNumber = firstRowNumber; rowNumber <= scanEnd; rowNumber++)
        {
            var texts = ReadRowCellTexts(worksheet, rowNumber, firstColumnNumber, lastColumnNumber);
            var score = ScoreBranchLitresRow(texts);

            if (score > bestScore)
            {
                bestScore = score;
                bestRow = rowNumber;
            }
        }

        return bestScore >= 30 ? bestRow : null;
    }

    internal static int? FindCarsBillingHeaderRow(
        IXLWorksheet worksheet,
        int firstRowNumber,
        int lastRowNumber,
        int firstColumnNumber,
        int lastColumnNumber)
    {
        var scanEnd = Math.Min(lastRowNumber, firstRowNumber + MaxRowsToScan - 1);

        for (var rowNumber = firstRowNumber; rowNumber <= scanEnd; rowNumber++)
        {
            var texts = ReadRowCellTexts(worksheet, rowNumber, firstColumnNumber, lastColumnNumber);
            if (IsCarsBillingHeaderRow(texts))
            {
                return rowNumber;
            }
        }

        return null;
    }

    private static int ScoreBranchLitresRow(IReadOnlyList<string> cells)
    {
        var score = 0;

        foreach (var cell in cells)
        {
            var value = ExcelColumnHeaderMatcher.Normalise(cell);

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (Matches(value, ExcelParserKnownHeaders.BranchLitres.Date))
            {
                score += 15;
            }

            if (Matches(value, ExcelParserKnownHeaders.BranchLitres.Litres))
            {
                score += 15;
            }

            if (Matches(value, ExcelParserKnownHeaders.BranchLitres.Rego))
            {
                score += 10;
            }

            if (Matches(value, ExcelParserKnownHeaders.BranchLitres.RentalAgreement))
            {
                score += 10;
            }

            if (value.Any(char.IsLetter))
            {
                score += 2;
            }

            if (decimal.TryParse(value, out _))
            {
                score -= 5;
            }
        }

        return score;
    }

    private static bool IsCarsBillingHeaderRow(IReadOnlyList<string> cellTexts)
    {
        var hasIdentifier = RowMatchesAnyAlias(cellTexts, ExcelParserKnownHeaders.CarsBilling.RentalAgreement)
            || RowMatchesAnyAlias(cellTexts, ExcelParserKnownHeaders.CarsBilling.Rego);

        var hasBillingColumn = RowMatchesAnyAlias(cellTexts, ExcelParserKnownHeaders.CarsBilling.BilledLitres)
            || RowMatchesAnyAlias(cellTexts, ExcelParserKnownHeaders.CarsBilling.BilledAmount)
            || RowMatchesAnyAlias(cellTexts, ExcelParserKnownHeaders.CarsBilling.Status);

        return hasIdentifier && hasBillingColumn;
    }

    private static bool RowMatchesAnyAlias(IReadOnlyList<string> cellTexts, IReadOnlyCollection<string> aliases)
    {
        foreach (var cellText in cellTexts)
        {
            var cellKey = ExcelColumnHeaderMatcher.Normalise(cellText);
            if (cellKey.Length > 0 && Matches(cellKey, aliases))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Matches(string normalisedValue, IReadOnlyCollection<string> aliases) =>
        aliases
            .Select(ExcelColumnHeaderMatcher.Normalise)
            .Any(alias => alias == normalisedValue);

    private static string[] ReadRowCellTexts(
        IXLWorksheet worksheet,
        int rowNumber,
        int firstColumnNumber,
        int lastColumnNumber)
    {
        var columnCount = lastColumnNumber - firstColumnNumber + 1;
        var texts = new string[columnCount];

        for (var offset = 0; offset < columnCount; offset++)
        {
            var columnNumber = firstColumnNumber + offset;
            texts[offset] = ReadCellText(worksheet.Cell(rowNumber, columnNumber)).Trim();
        }

        return texts;
    }

    private static string ReadCellText(IXLCell cell) => cell.CachedValue.ToString();
}