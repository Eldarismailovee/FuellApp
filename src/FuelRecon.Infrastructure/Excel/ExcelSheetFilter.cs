namespace FuelRecon.Infrastructure.Excel;

/// <summary>
/// Filters workbook tabs that are unlikely to contain transactional rows (scratch pads, summaries, etc.).
/// </summary>
public static class ExcelSheetFilter
{
    public static bool IsNonDataSheet(string? sheetName)
    {
        if (string.IsNullOrWhiteSpace(sheetName))
        {
            return false;
        }

        var compact = ExcelColumnHeaderMatcher.Normalise(sheetName);
        if (compact.Length == 0)
        {
            return false;
        }

        return compact switch
        {
            "scratch" or "notes" or "note" or "summary" or "totals" or "total" => true,
            _ => compact.EndsWith("summary", StringComparison.Ordinal)
                || compact.EndsWith("totals", StringComparison.Ordinal)
                || compact.EndsWith("scratch", StringComparison.Ordinal),
        };
    }

    /// <summary>
    /// Cars+ exports sometimes land on a worksheet named "scratch"; still skip obvious summary tabs.
    /// </summary>
    public static bool IsNonDataSheetForCarsBilling(string? sheetName)
    {
        if (string.IsNullOrWhiteSpace(sheetName))
        {
            return false;
        }

        var compact = ExcelColumnHeaderMatcher.Normalise(sheetName);
        if (compact.Length == 0)
        {
            return false;
        }

        return compact switch
        {
            "notes" or "note" or "summary" or "totals" or "total" => true,
            _ => compact.EndsWith("summary", StringComparison.Ordinal)
                || compact.EndsWith("totals", StringComparison.Ordinal),
        };
    }
}
