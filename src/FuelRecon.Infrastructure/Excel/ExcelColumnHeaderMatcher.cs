namespace FuelRecon.Infrastructure.Excel;

/// <summary>
/// Case-insensitive header matching with punctuation stripped so real-world layouts tolerate spacing and symbols.
/// </summary>
internal static class ExcelColumnHeaderMatcher
{
    public static int? FindFirstMatchingColumn(IReadOnlyList<string> headers, IReadOnlyCollection<string> aliases)
    {
        var normalisedAliases = aliases
            .Select(Normalise)
            .Where(static key => key.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        for (var index = 0; index < headers.Count; index++)
        {
            var headerKey = Normalise(headers[index]);
            if (normalisedAliases.Contains(headerKey))
            {
                return index;
            }
        }

        return null;
    }

    internal static string Normalise(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return string.Empty;
        }

        var span = header.Trim().AsSpan();
        Span<char> buffer = stackalloc char[span.Length];
        var written = 0;

        foreach (var character in span)
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer[written++] = char.ToLowerInvariant(character);
            }
        }

        return written == 0 ? string.Empty : new string(buffer[..written]);
    }
}
