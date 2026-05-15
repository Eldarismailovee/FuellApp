using FuelRecon.Domain;

namespace FuelRecon.Tests;

public class RentalAgreementNormaliserTests
{
    [Theory]
    [InlineData("123-456", "123456")]
    [InlineData(" 123 456 ", "123456")]
    [InlineData("RA-123.0", "123")]
    [InlineData("AB 001-23", "00123")]
    public void NumericOnly_mode_normalises_valid_numeric_ra(string rawValue, string expectedNormalisedValue)
    {
        var result = RentalAgreementNormaliser.Normalise(rawValue, RentalAgreementNormalisationMode.NumericOnly);

        Assert.True(result.Success);
        Assert.Equal(rawValue, result.RawValue);
        Assert.Equal(expectedNormalisedValue, result.NormalisedValue);
        Assert.Null(result.ReasonCode);
    }

    [Theory]
    [InlineData("ra-123", "RA123")]
    [InlineData(" ab 12-34 ", "AB1234")]
    [InlineData("A/B-42", "A/B42")]
    [InlineData("ra.123", "RA.123")]
    public void ConservativeAlphanumeric_mode_preserves_alphanumeric_ra_without_assuming_numeric_only(
        string rawValue,
        string expectedNormalisedValue)
    {
        var result = RentalAgreementNormaliser.Normalise(
            rawValue,
            RentalAgreementNormalisationMode.ConservativeAlphanumeric);

        Assert.True(result.Success);
        Assert.Equal(rawValue, result.RawValue);
        Assert.Equal(expectedNormalisedValue, result.NormalisedValue);
        Assert.Null(result.ReasonCode);
    }

    [Theory]
    [InlineData("123456.0", "123456", RentalAgreementNormalisationMode.ConservativeAlphanumeric)]
    [InlineData("123456.0", "123456", RentalAgreementNormalisationMode.NumericOnly)]
    [InlineData("RA-123.0", "RA123", RentalAgreementNormalisationMode.ConservativeAlphanumeric)]
    [InlineData("RA-123.0", "123", RentalAgreementNormalisationMode.NumericOnly)]
    public void Normalisation_removes_trailing_excel_dot_zero_suffix_only(
        string rawValue,
        string expectedNormalisedValue,
        RentalAgreementNormalisationMode mode)
    {
        var result = RentalAgreementNormaliser.Normalise(rawValue, mode);

        Assert.True(result.Success);
        Assert.Equal(expectedNormalisedValue, result.NormalisedValue);
    }

    [Fact]
    public void ConservativeAlphanumeric_mode_does_not_remove_non_excel_decimal_suffix()
    {
        var result = RentalAgreementNormaliser.Normalise(
            "RA-123.00",
            RentalAgreementNormalisationMode.ConservativeAlphanumeric);

        Assert.True(result.Success);
        Assert.Equal("RA123.00", result.NormalisedValue);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" - .0 ")]
    public void ConservativeAlphanumeric_mode_returns_invalid_result_for_empty_or_formatting_only_values(string? rawValue)
    {
        var result = RentalAgreementNormaliser.Normalise(
            rawValue,
            RentalAgreementNormalisationMode.ConservativeAlphanumeric);

        Assert.False(result.Success);
        Assert.Equal(rawValue ?? string.Empty, result.RawValue);
        Assert.Null(result.NormalisedValue);
        Assert.Equal(RentalAgreementNormaliser.InvalidReasonCode, result.ReasonCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("RA-ABC")]
    [InlineData(" - .0 ")]
    public void NumericOnly_mode_returns_invalid_result_when_no_digits_remain(string? rawValue)
    {
        var result = RentalAgreementNormaliser.Normalise(rawValue, RentalAgreementNormalisationMode.NumericOnly);

        Assert.False(result.Success);
        Assert.Equal(rawValue ?? string.Empty, result.RawValue);
        Assert.Null(result.NormalisedValue);
        Assert.Equal("InvalidRA", result.ReasonCode);
    }

    [Theory]
    [InlineData(" ra-123.0 ", RentalAgreementNormalisationMode.ConservativeAlphanumeric)]
    [InlineData(" ra-123.0 ", RentalAgreementNormalisationMode.NumericOnly)]
    [InlineData("AB 12-34", RentalAgreementNormalisationMode.ConservativeAlphanumeric)]
    [InlineData("AB 12-34", RentalAgreementNormalisationMode.NumericOnly)]
    public void Normalisation_is_idempotent_for_successful_results(
        string rawValue,
        RentalAgreementNormalisationMode mode)
    {
        var first = RentalAgreementNormaliser.Normalise(rawValue, mode);
        Assert.True(first.Success);
        Assert.NotNull(first.NormalisedValue);

        var second = RentalAgreementNormaliser.Normalise(first.NormalisedValue, mode);

        Assert.True(second.Success);
        Assert.Equal(first.NormalisedValue, second.NormalisedValue);
    }

    [Fact]
    public void Default_mode_is_conservative_alphanumeric()
    {
        var defaultModeResult = RentalAgreementNormaliser.Normalise("ra-123");
        var explicitModeResult = RentalAgreementNormaliser.Normalise(
            "ra-123",
            RentalAgreementNormalisationMode.ConservativeAlphanumeric);

        Assert.True(defaultModeResult.Success);
        Assert.Equal(explicitModeResult.NormalisedValue, defaultModeResult.NormalisedValue);
    }
}
