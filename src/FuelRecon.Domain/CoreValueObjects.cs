using System.Globalization;

namespace FuelRecon.Domain;

/// <summary>
/// Calendar month and year used as the reconciliation period.
/// </summary>
public readonly record struct FuelPeriod
{
    private static readonly string[] ParseFormats =
    [
        "yyyy-MM",
        "yyyy/MM",
        "MM/yyyy",
        "MMMM yyyy",
        "MMM yyyy",
    ];

    public FuelPeriod(int year, int month)
    {
        if (year < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(year), year, "Year must be greater than zero.");
        }

        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), month, "Month must be between 1 and 12.");
        }

        Year = year;
        Month = month;
    }

    public int Year { get; }

    public int Month { get; }

    public static FuelPeriod FromDate(DateOnly date) => new(date.Year, date.Month);

    public static FuelPeriod Parse(string value)
    {
        if (TryParse(value, out var period))
        {
            return period;
        }

        throw new ArgumentException("Value must be a valid fuel period, such as 'April 2026' or '2026-04'.", nameof(value));
    }

    public static bool TryParse(string? value, out FuelPeriod period)
    {
        period = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!DateTime.TryParseExact(
                value.Trim(),
                ParseFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var parsed))
        {
            return false;
        }

        period = new FuelPeriod(parsed.Year, parsed.Month);
        return true;
    }

    public string ToSortableString() => $"{Year:D4}-{Month:D2}";

    public override string ToString() => $"{CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(Month)} {Year:D4}";
}

/// <summary>
/// Stable canonical branch identifier, independent of source-file branch aliases.
/// </summary>
public readonly record struct CanonicalBranchId
{
    public CanonicalBranchId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Canonical branch ID cannot be empty.", nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// Vehicle registration preserving both the raw source value and deterministic normalised value.
/// </summary>
/// <summary>
/// Vehicle registration preserving both the raw source value and deterministic normalised value.
/// </summary>
public sealed record Rego
{
    public Rego(string rawValue)
    {
        var result = RegoNormaliser.Normalise(rawValue);

        if (!result.Success || result.NormalisedValue is null)
        {
            throw new ArgumentException("Rego must contain at least one non-formatting character.", nameof(rawValue));
        }

        RawValue = rawValue;
        NormalisedValue = result.NormalisedValue;
    }

    public string RawValue { get; }

    public string NormalisedValue { get; }

    public static string Normalise(string value)
    {
        var result = RegoNormaliser.Normalise(value);

        if (!result.Success || result.NormalisedValue is null)
        {
            throw new ArgumentException("Rego must contain at least one non-formatting character.", nameof(value));
        }

        return result.NormalisedValue;
    }

    public override string ToString() => NormalisedValue;
}

/// <summary>
/// Rental agreement number preserving source text and a conservative alphanumeric-friendly normalised value.
/// </summary>
public sealed record RentalAgreementNumber
{
    public RentalAgreementNumber(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            throw new ArgumentException("Rental agreement number cannot be empty.", nameof(rawValue));
        }

        RawValue = rawValue;
        NormalisedValue = Normalise(rawValue);

        if (NormalisedValue.Length == 0)
        {
            throw new ArgumentException("Rental agreement number must contain at least one non-formatting character.", nameof(rawValue));
        }
    }

    public string RawValue { get; }

    public string NormalisedValue { get; }

    public static string Normalise(string value)
    {   
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        var trimmed = value.Trim();

        // Common Excel artefact: numeric RA values may arrive as "1234567.0".
        // Remove the trailing decimal suffix only when it is exactly ".0".
        if (trimmed.EndsWith(".0", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^2];
        }

        return new string(trimmed
            .Where(character => !char.IsWhiteSpace(character) && character != '-')
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    public override string ToString() => NormalisedValue;
}

/// <summary>
/// Monetary amount rounded to cents using deterministic midpoint handling.
/// </summary>
public readonly record struct MoneyAmount
{
    public MoneyAmount(decimal value)
    {
        Value = Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    public decimal Value { get; }

    public override string ToString() => Value.ToString("0.00", CultureInfo.InvariantCulture);
}

/// <summary>
/// Fuel volume in litres rounded to two decimals. Negative litres are not accepted for MVP imports.
/// </summary>
public readonly record struct Litres
{
    public Litres(decimal value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Litres cannot be negative.");
        }

        Value = Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    public decimal Value { get; }

    public override string ToString() => Value.ToString("0.00", CultureInfo.InvariantCulture);
}

/// <summary>
/// Location of a value in an imported source file, preserving optional sheet/row/page evidence.
/// </summary>
public sealed record SourceReference
{
    public SourceReference(
        string sourceFile,
        string? sheetName = null,
        int? rowNumber = null,
        int? pageNumber = null,
        string? referenceText = null)
    {
        if (string.IsNullOrWhiteSpace(sourceFile))
        {
            throw new ArgumentException("Source file cannot be empty.", nameof(sourceFile));
        }

        if (rowNumber is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rowNumber), rowNumber, "Row number must be greater than zero when provided.");
        }

        if (pageNumber is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber), pageNumber, "Page number must be greater than zero when provided.");
        }

        SourceFile = sourceFile.Trim();
        SheetName = TrimToNull(sheetName);
        RowNumber = rowNumber;
        PageNumber = pageNumber;
        ReferenceText = TrimToNull(referenceText);
    }

    public string SourceFile { get; }

    public string? SheetName { get; }

    public int? RowNumber { get; }

    public int? PageNumber { get; }

    public string? ReferenceText { get; }

    public override string ToString()
    {
        var parts = new List<string> { SourceFile };

        if (SheetName is not null)
        {
            parts.Add($"sheet {SheetName}");
        }

        if (RowNumber is not null)
        {
            parts.Add($"row {RowNumber}");
        }

        if (PageNumber is not null)
        {
            parts.Add($"page {PageNumber}");
        }

        if (ReferenceText is not null)
        {
            parts.Add(ReferenceText);
        }

        return string.Join(", ", parts);
    }

    private static string? TrimToNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}

/// <summary>
/// Stable correlation identifier for linking domain records to logs or audit entries.
/// </summary>
public readonly record struct CorrelationId
{
    public CorrelationId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Correlation ID cannot be empty.", nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; }

    public static CorrelationId New() => FromGuid(Guid.NewGuid());

    public static CorrelationId FromGuid(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Correlation ID GUID cannot be empty.", nameof(value));
        }

        return new CorrelationId(value.ToString("N"));
    }

    public override string ToString() => Value;
}

/// <summary>
/// File checksum captured during import to identify exact source-file bytes.
/// </summary>
public sealed record FileChecksum
{
    public FileChecksum(string algorithm, string value)
    {
        if (string.IsNullOrWhiteSpace(algorithm))
        {
            throw new ArgumentException("Checksum algorithm cannot be empty.", nameof(algorithm));
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Checksum value cannot be empty.", nameof(value));
        }

        Algorithm = algorithm.Trim().ToUpperInvariant();
        Value = value.Trim();
    }

    public string Algorithm { get; }

    public string Value { get; }

    public override string ToString() => $"{Algorithm}:{Value}";
}
