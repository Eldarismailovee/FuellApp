namespace FuelRecon.Domain;

/// <summary>
/// Result of rego normalisation preserving the raw source value and a machine-readable reason code on failure.
/// </summary>
public sealed record RegoNormalisationResult
{
    private RegoNormalisationResult(
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

    public static RegoNormalisationResult Valid(string rawValue, string normalisedValue) =>
        new(rawValue, normalisedValue, success: true, reasonCode: null);

    public static RegoNormalisationResult Invalid(string rawValue, string reasonCode) =>
        new(rawValue, normalisedValue: null, success: false, reasonCode);
}

/// <summary>
/// Deterministic rego normaliser for imported source data.
/// </summary>
public static class RegoNormaliser
{
    public const string InvalidReasonCode = "InvalidRego";

    public static RegoNormalisationResult Normalise(string? rawValue)
    {
        var preservedRawValue = rawValue ?? string.Empty;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return RegoNormalisationResult.Invalid(preservedRawValue, InvalidReasonCode);
        }

        var normalisedValue = new string(rawValue
            .Trim()
            .Where(character => !char.IsWhiteSpace(character) && character != '-')
            .Select(char.ToUpperInvariant)
            .ToArray());

        if (normalisedValue.Length == 0)
        {
            return RegoNormalisationResult.Invalid(preservedRawValue, InvalidReasonCode);
        }

        return RegoNormalisationResult.Valid(preservedRawValue, normalisedValue);
    }
}
