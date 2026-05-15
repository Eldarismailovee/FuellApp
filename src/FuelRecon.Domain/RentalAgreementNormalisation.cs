namespace FuelRecon.Domain;

/// <summary>
/// Supported RA normalisation rules. Conservative alphanumeric is the default because not all RAs are numeric.
/// </summary>
public enum RentalAgreementNormalisationMode
{
    ConservativeAlphanumeric = 0,
    NumericOnly = 1,
}

/// <summary>
/// Result of RA normalisation preserving the raw source value and a machine-readable reason code on failure.
/// </summary>
public sealed record RentalAgreementNormalisationResult
{
    private RentalAgreementNormalisationResult(
        string rawValue,
        string? normalisedValue,
        bool success,
        string? reasonCode)
    {
        RawValue = rawValue;
        NormalisedValue = normalisedValue;
        Success = success;
        ReasonCode = reasonCode;
    }

    public string RawValue { get; }

    public string? NormalisedValue { get; }

    public bool Success { get; }

    public string? ReasonCode { get; }

    public static RentalAgreementNormalisationResult Valid(string rawValue, string normalisedValue) =>
        new(rawValue, normalisedValue, success: true, reasonCode: null);

    public static RentalAgreementNormalisationResult Invalid(string rawValue, string reasonCode) =>
        new(rawValue, normalisedValue: null, success: false, reasonCode);
}

/// <summary>
/// Deterministic RA normaliser for imported source data.
/// </summary>
public static class RentalAgreementNormaliser
{
    public const string InvalidReasonCode = "InvalidRA";

    public static RentalAgreementNormalisationResult Normalise(
        string? rawValue,
        RentalAgreementNormalisationMode mode = RentalAgreementNormalisationMode.ConservativeAlphanumeric)
    {
        var preservedRawValue = rawValue ?? string.Empty;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return RentalAgreementNormalisationResult.Invalid(preservedRawValue, InvalidReasonCode);
        }

        var trimmedValue = RemoveTrailingExcelZeroSuffix(rawValue.Trim());
        var normalisedValue = mode switch
        {
            RentalAgreementNormalisationMode.ConservativeAlphanumeric => NormaliseConservativeAlphanumeric(trimmedValue),
            RentalAgreementNormalisationMode.NumericOnly => NormaliseNumericOnly(trimmedValue),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported RA normalisation mode."),
        };

        if (normalisedValue.Length == 0)
        {
            return RentalAgreementNormalisationResult.Invalid(preservedRawValue, InvalidReasonCode);
        }

        return RentalAgreementNormalisationResult.Valid(preservedRawValue, normalisedValue);
    }

    private static string RemoveTrailingExcelZeroSuffix(string value)
    {
        if (value.EndsWith(".0", StringComparison.Ordinal))
        {
            return value[..^2];
        }

        return value;
    }

    private static string NormaliseConservativeAlphanumeric(string value) =>
        new(value
            .Where(character => !char.IsWhiteSpace(character) && character != '-')
            .Select(char.ToUpperInvariant)
            .ToArray());

    private static string NormaliseNumericOnly(string value) =>
        new(value
            .Where(character => !char.IsWhiteSpace(character) && character != '-')
            .Where(char.IsDigit)
            .ToArray());
}
