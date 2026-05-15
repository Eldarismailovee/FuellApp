using System.Globalization;

namespace FuelRecon.Domain;

/// <summary>
/// Result of date normalisation preserving raw source value and a machine-readable reason code on failure.
/// </summary>
public sealed record DateNormalisationResult
{
    private DateNormalisationResult(string rawValue, DateOnly? normalisedValue, bool success, string? reasonCode)
    {
        RawValue = rawValue;
        NormalisedValue = normalisedValue;
        Success = success;
        ReasonCode = reasonCode;
    }

    public string RawValue { get; }

    public DateOnly? NormalisedValue { get; }

    public bool Success { get; }

    public string? ReasonCode { get; }

    public static DateNormalisationResult Valid(string rawValue, DateOnly normalisedValue) =>
        new(rawValue, normalisedValue, success: true, reasonCode: null);

    public static DateNormalisationResult Invalid(string rawValue, string reasonCode) =>
        new(rawValue, normalisedValue: null, success: false, reasonCode);
}

/// <summary>
/// Result of litres normalisation preserving raw source value and a machine-readable reason code on failure.
/// </summary>
public sealed record LitresNormalisationResult
{
    private LitresNormalisationResult(string rawValue, Litres? normalisedValue, bool success, string? reasonCode)
    {
        RawValue = rawValue;
        NormalisedValue = normalisedValue;
        Success = success;
        ReasonCode = reasonCode;
    }

    public string RawValue { get; }

    public Litres? NormalisedValue { get; }

    public bool Success { get; }

    public string? ReasonCode { get; }

    public static LitresNormalisationResult Valid(string rawValue, Litres normalisedValue) =>
        new(rawValue, normalisedValue, success: true, reasonCode: null);

    public static LitresNormalisationResult Invalid(string rawValue, string reasonCode) =>
        new(rawValue, normalisedValue: null, success: false, reasonCode);
}

/// <summary>
/// Result of money amount normalisation preserving raw source value and a machine-readable reason code on failure.
/// </summary>
public sealed record MoneyAmountNormalisationResult
{
    private MoneyAmountNormalisationResult(string rawValue, MoneyAmount? normalisedValue, bool success, string? reasonCode)
    {
        RawValue = rawValue;
        NormalisedValue = normalisedValue;
        Success = success;
        ReasonCode = reasonCode;
    }

    public string RawValue { get; }

    public MoneyAmount? NormalisedValue { get; }

    public bool Success { get; }

    public string? ReasonCode { get; }

    public static MoneyAmountNormalisationResult Valid(string rawValue, MoneyAmount normalisedValue) =>
        new(rawValue, normalisedValue, success: true, reasonCode: null);

    public static MoneyAmountNormalisationResult Invalid(string rawValue, string reasonCode) =>
        new(rawValue, normalisedValue: null, success: false, reasonCode);
}

/// <summary>
/// Deterministic date normaliser for imported source values.
/// </summary>
public static class DateNormaliser
{
    public const string InvalidDateFormatReasonCode = "InvalidDateFormat";

    public const string AmbiguousDateFormatReasonCode = "AmbiguousDateFormat";

    private static readonly string[] FullDateFormats =
    [
        "d/M/yyyy",
        "dd/MM/yyyy",
        "d-M-yyyy",
        "dd-MM-yyyy",
        "yyyy-M-d",
        "yyyy-MM-dd",
        "d MMM yyyy",
        "dd MMM yyyy",
        "d MMMM yyyy",
        "dd MMMM yyyy",
    ];

    private static readonly string[] MonthYearFormats =
    [
        "MMM yyyy",
        "MMMM yyyy",
    ];

    public static DateNormalisationResult Normalise(DateOnly value) =>
        DateNormalisationResult.Valid(value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), value);

    public static DateNormalisationResult Normalise(DateTime value)
    {
        var normalisedValue = DateOnly.FromDateTime(value);
        return DateNormalisationResult.Valid(value.ToString("O", CultureInfo.InvariantCulture), normalisedValue);
    }

    public static DateNormalisationResult NormaliseExcelSerial(decimal serialDate)
    {
        var rawValue = serialDate.ToString(CultureInfo.InvariantCulture);

        if (serialDate <= 0)
        {
            return DateNormalisationResult.Invalid(rawValue, InvalidDateFormatReasonCode);
        }

        try
        {
            var excelDateTime = DateTime.FromOADate((double)serialDate + 1);
            return DateNormalisationResult.Valid(rawValue, DateOnly.FromDateTime(excelDateTime));
        }
        catch (ArgumentException)
        {
            return DateNormalisationResult.Invalid(rawValue, InvalidDateFormatReasonCode);
        }
    }

    public static DateNormalisationResult NormaliseText(string? rawValue)
    {
        var preservedRawValue = rawValue ?? string.Empty;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return DateNormalisationResult.Invalid(preservedRawValue, InvalidDateFormatReasonCode);
        }

        var trimmedValue = rawValue.Trim();

        if (LooksLikeAmbiguousTwoDigitYearDate(trimmedValue))
        {
            return DateNormalisationResult.Invalid(preservedRawValue, AmbiguousDateFormatReasonCode);
        }

        if (DateTime.TryParseExact(
                trimmedValue,
                FullDateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var fullDate))
        {
            return DateNormalisationResult.Valid(preservedRawValue, DateOnly.FromDateTime(fullDate));
        }

        if (DateTime.TryParseExact(
                trimmedValue,
                MonthYearFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var monthYear))
        {
            return DateNormalisationResult.Valid(preservedRawValue, new DateOnly(monthYear.Year, monthYear.Month, 1));
        }

        return DateNormalisationResult.Invalid(preservedRawValue, InvalidDateFormatReasonCode);
    }

    private static bool LooksLikeAmbiguousTwoDigitYearDate(string value)
    {
        var separator = value.Contains('/') ? '/' : value.Contains('-') ? '-' : '\0';
        if (separator == '\0')
        {
            return false;
        }

        var parts = value.Split(separator);
        return parts.Length == 3
            && parts[0].Length is 1 or 2
            && parts[1].Length is 1 or 2
            && parts[2].Length == 2
            && parts.All(part => part.All(char.IsDigit));
    }
}

/// <summary>
/// Deterministic litres normaliser for imported source values.
/// </summary>
public static class LitresNormaliser
{
    public const string FailureReasonCode = "NormalizationFailed_Litres";

    public const string AmbiguousNumericFormatReasonCode = "AmbiguousNumericFormat";

    public static LitresNormalisationResult Normalise(string? rawValue)
    {
        var preservedRawValue = rawValue ?? string.Empty;
        var parseResult = NumericTextParser.TryParseDecimal(
            preservedRawValue,
            allowCurrencySymbol: false,
            failureReasonCode: FailureReasonCode);

        if (!parseResult.Success)
        {
            return LitresNormalisationResult.Invalid(preservedRawValue, parseResult.ReasonCode ?? FailureReasonCode);
        }

        try
        {
            return LitresNormalisationResult.Valid(preservedRawValue, new Litres(parseResult.Value));
        }
        catch (ArgumentOutOfRangeException)
        {
            return LitresNormalisationResult.Invalid(preservedRawValue, FailureReasonCode);
        }
    }
}

/// <summary>
/// Deterministic money amount normaliser for imported source values.
/// </summary>
public static class MoneyAmountNormaliser
{
    public const string FailureReasonCode = "NormalizationFailed_Amount";

    public const string AmbiguousNumericFormatReasonCode = "AmbiguousNumericFormat";

    public static MoneyAmountNormalisationResult Normalise(string? rawValue)
    {
        var preservedRawValue = rawValue ?? string.Empty;
        var parseResult = NumericTextParser.TryParseDecimal(
            preservedRawValue,
            allowCurrencySymbol: true,
            failureReasonCode: FailureReasonCode);

        if (!parseResult.Success)
        {
            return MoneyAmountNormalisationResult.Invalid(preservedRawValue, parseResult.ReasonCode ?? FailureReasonCode);
        }

        return MoneyAmountNormalisationResult.Valid(preservedRawValue, new MoneyAmount(parseResult.Value));
    }
}

internal readonly record struct NumericParseResult(bool Success, decimal Value, string? ReasonCode);

internal static class NumericTextParser
{
    internal static NumericParseResult TryParseDecimal(string rawValue, bool allowCurrencySymbol, string failureReasonCode)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return Failure(failureReasonCode, null);
        }

        var cleanedValue = rawValue.Trim();

        if (allowCurrencySymbol)
        {
            cleanedValue = cleanedValue.Replace("$", string.Empty, StringComparison.Ordinal);
        }
        else if (cleanedValue.Contains('$', StringComparison.Ordinal))
        {
            return Failure(failureReasonCode, null);
        }

        cleanedValue = RemoveSpaces(cleanedValue);

        if (cleanedValue.Length == 0)
        {
            return Failure(failureReasonCode, null);
        }

        if (cleanedValue.Count(character => character is '+' or '-') > 1
            || (cleanedValue.Contains('+', StringComparison.Ordinal) && cleanedValue[0] != '+')
            || (cleanedValue.Contains('-', StringComparison.Ordinal) && cleanedValue[0] != '-'))
        {
            return Failure(failureReasonCode, null);
        }

        var unsignedValue = cleanedValue.TrimStart('+', '-');
        if (unsignedValue.Length == 0)
        {
            return Failure(failureReasonCode, null);
        }

        if (unsignedValue.Any(character => !char.IsDigit(character) && character is not '.' and not ','))
        {
            return Failure(failureReasonCode, null);
        }

        var decimalText = ToInvariantDecimalText(cleanedValue);
        if (decimalText is null)
        {
            return Failure(failureReasonCode, "AmbiguousNumericFormat");
        }

        if (!decimal.TryParse(decimalText, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return Failure(failureReasonCode, null);
        }

        return new NumericParseResult(Success: true, value, ReasonCode: null);
    }

    private static string? ToInvariantDecimalText(string value)
    {
        var commaCount = value.Count(character => character == ',');
        var dotCount = value.Count(character => character == '.');

        if (commaCount == 0 && dotCount == 0)
        {
            return value;
        }

        if (commaCount > 0 && dotCount > 0)
        {
            var decimalSeparator = value.LastIndexOf(',') > value.LastIndexOf('.') ? ',' : '.';
            var thousandsSeparator = decimalSeparator == ',' ? '.' : ',';
            return BuildInvariantDecimalText(value, decimalSeparator, thousandsSeparator);
        }

        var separator = commaCount > 0 ? ',' : '.';
        var separatorCount = Math.Max(commaCount, dotCount);

        if (separatorCount == 1)
        {
            var separatorIndex = value.IndexOf(separator);
            var digitsAfterSeparator = value.Length - separatorIndex - 1;

            if (digitsAfterSeparator == 0)
            {
                return null;
            }

            // A lone comma followed by three digits is culture-dependent: thousands or decimal.
            if (separator == ',' && digitsAfterSeparator == 3)
            {
                return null;
            }

            return separator == ',' ? value.Replace(',', '.') : value;
        }

        return GroupsAreValidThousands(value, separator)
            ? value.Replace(separator.ToString(), string.Empty, StringComparison.Ordinal)
            : null;
    }

    private static string? BuildInvariantDecimalText(string value, char decimalSeparator, char thousandsSeparator)
    {
        var decimalIndex = value.LastIndexOf(decimalSeparator);
        if (decimalIndex < 0 || decimalIndex == value.Length - 1)
        {
            return null;
        }

        var integerPart = value[..decimalIndex];
        var fractionalPart = value[(decimalIndex + 1)..];

        if (fractionalPart.Length == 0 || !fractionalPart.All(char.IsDigit))
        {
            return null;
        }

        if (!GroupsAreValidThousands(integerPart, thousandsSeparator))
        {
            return null;
        }

        return integerPart.Replace(thousandsSeparator.ToString(), string.Empty, StringComparison.Ordinal)
            + "."
            + fractionalPart;
    }

    private static bool GroupsAreValidThousands(string value, char separator)
    {
        var unsignedValue = value.TrimStart('+', '-');
        var groups = unsignedValue.Split(separator);

        if (groups.Length < 2 || groups[0].Length is < 1 or > 3 || !groups[0].All(char.IsDigit))
        {
            return false;
        }

        return groups.Skip(1).All(group => group.Length == 3 && group.All(char.IsDigit));
    }

    private static string RemoveSpaces(string value) =>
        new(value.Where(character => !char.IsWhiteSpace(character)).ToArray());

    private static NumericParseResult Failure(string failureReasonCode, string? reasonCode) =>
        new(Success: false, Value: 0, ReasonCode: reasonCode ?? failureReasonCode);

}
