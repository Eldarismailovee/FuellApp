using FuelRecon.Domain;

namespace FuelRecon.Tests;

public class RegoNormaliserTests
{
    [Theory]
    [InlineData("abc123", "ABC123")]
    [InlineData("abc-123", "ABC123")]
    [InlineData(" Ab C-1 23 ", "ABC123")]
    [InlineData("nz-987", "NZ987")]
    public void Normalise_uppercases_and_removes_spaces_and_hyphens(string rawValue, string expectedNormalisedValue)
    {
        var result = RegoNormaliser.Normalise(rawValue);

        Assert.True(result.Success);
        Assert.Equal(rawValue, result.RawValue);
        Assert.Equal(expectedNormalisedValue, result.NormalisedValue);
        Assert.Null(result.ReasonCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("---")]
    [InlineData(" - - ")]
    public void Normalise_returns_invalid_result_for_empty_or_formatting_only_values(string? rawValue)
    {
        var result = RegoNormaliser.Normalise(rawValue);

        Assert.False(result.Success);
        Assert.Equal(rawValue ?? string.Empty, result.RawValue);
        Assert.Null(result.NormalisedValue);
        Assert.Equal(RegoNormaliser.InvalidReasonCode, result.ReasonCode);
    }

    [Theory]
    [InlineData("abc-123")]
    [InlineData(" AB C-1 23 ")]
    [InlineData("NZ987")]
    public void Normalise_is_idempotent_for_successful_results(string rawValue)
    {
        var first = RegoNormaliser.Normalise(rawValue);
        Assert.True(first.Success);
        Assert.NotNull(first.NormalisedValue);

        var second = RegoNormaliser.Normalise(first.NormalisedValue);

        Assert.True(second.Success);
        Assert.Equal(first.NormalisedValue, second.NormalisedValue);
    }

    [Fact]
    public void Normalise_matches_Rego_value_object_normalised_value()
    {
        const string rawValue = "abc-123";
        var result = RegoNormaliser.Normalise(rawValue);
        var rego = new Rego(rawValue);

        Assert.True(result.Success);
        Assert.Equal(rego.RawValue, result.RawValue);
        Assert.Equal(rego.NormalisedValue, result.NormalisedValue);
    }
}
