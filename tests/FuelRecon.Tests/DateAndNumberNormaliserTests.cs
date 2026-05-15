using System.Globalization;
using FuelRecon.Domain;

namespace FuelRecon.Tests;

public class DateAndNumberNormaliserTests
{
    [Fact]
    public void DateNormaliser_normalises_DateOnly_input()
    {
        var result = DateNormaliser.Normalise(new DateOnly(2026, 4, 1));

        Assert.True(result.Success);
        Assert.Equal("2026-04-01", result.RawValue);
        Assert.Equal(new DateOnly(2026, 4, 1), result.NormalisedValue);
        Assert.Null(result.ReasonCode);
    }

    [Fact]
    public void DateNormaliser_normalises_DateTime_input_to_date_only()
    {
        var result = DateNormaliser.Normalise(new DateTime(2026, 5, 15, 14, 30, 0, DateTimeKind.Local));

        Assert.True(result.Success);
        Assert.Equal(new DateOnly(2026, 5, 15), result.NormalisedValue);
        Assert.Null(result.ReasonCode);
    }

    [Theory]
    [InlineData("46112", 2026, 4, 1)]
    [InlineData("46156.75", 2026, 5, 15)]
    public void DateNormaliser_normalises_Excel_serial_dates(string rawSerial, int expectedYear, int expectedMonth, int expectedDay)
    {
        var result = DateNormaliser.NormaliseExcelSerial(decimal.Parse(rawSerial, CultureInfo.InvariantCulture));

        Assert.True(result.Success);
        Assert.Equal(rawSerial, result.RawValue);
        Assert.Equal(new DateOnly(expectedYear, expectedMonth, expectedDay), result.NormalisedValue);
        Assert.Null(result.ReasonCode);
    }

    [Theory]
    [InlineData("01/04/2026", 2026, 4, 1)]
    [InlineData("1 Apr 2026", 2026, 4, 1)]
    [InlineData("April 2026", 2026, 4, 1)]
    [InlineData("2026-04-30", 2026, 4, 30)]
    public void DateNormaliser_normalises_supported_text_dates(string rawValue, int expectedYear, int expectedMonth, int expectedDay)
    {
        var result = DateNormaliser.NormaliseText(rawValue);

        Assert.True(result.Success);
        Assert.Equal(rawValue, result.RawValue);
        Assert.Equal(new DateOnly(expectedYear, expectedMonth, expectedDay), result.NormalisedValue);
        Assert.Null(result.ReasonCode);
    }

    [Theory]
    [InlineData("22/04", 2026, 4, 22)]
    [InlineData("1/4", 2026, 4, 1)]
    [InlineData("06/4", 2026, 4, 6)]
    [InlineData("4/1/2026", 2026, 4, 1)]
    [InlineData("04/01/2026", 2026, 4, 1)]
    [InlineData("12/04/2026", 2026, 4, 12)]
    public void DateNormaliser_normalises_slash_dates_with_fuel_period_context(string rawValue, int expectedYear, int expectedMonth, int expectedDay)
    {
        var period = new FuelPeriod(2026, 4);
        var result = DateNormaliser.NormaliseText(rawValue, period);

        Assert.True(result.Success);
        Assert.Equal(rawValue, result.RawValue);
        Assert.Equal(new DateOnly(expectedYear, expectedMonth, expectedDay), result.NormalisedValue);
        Assert.Null(result.ReasonCode);
    }

    [Fact]
    public void DateNormaliser_normalises_excel_datetime_strings_with_unicode_spaces_when_invariant_parse_succeeds()
    {
        var raw = "4/15/2026 12:00:00\u202fAM";
        var period = new FuelPeriod(2026, 4);
        var result = DateNormaliser.NormaliseText(raw, period);

        Assert.True(result.Success);
        Assert.Equal(new DateOnly(2026, 4, 15), result.NormalisedValue);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("31/02/2026")]
    [InlineData("not a date")]
    public void DateNormaliser_returns_invalid_for_invalid_dates(string? rawValue)
    {
        var result = DateNormaliser.NormaliseText(rawValue);

        Assert.False(result.Success);
        Assert.Equal(rawValue ?? string.Empty, result.RawValue);
        Assert.Null(result.NormalisedValue);
        Assert.Equal(DateNormaliser.InvalidDateFormatReasonCode, result.ReasonCode);
    }

    [Fact]
    public void DateNormaliser_returns_ambiguous_for_two_digit_year_numeric_dates()
    {
        var result = DateNormaliser.NormaliseText("04/05/06");

        Assert.False(result.Success);
        Assert.Equal("04/05/06", result.RawValue);
        Assert.Null(result.NormalisedValue);
        Assert.Equal(DateNormaliser.AmbiguousDateFormatReasonCode, result.ReasonCode);
    }

    [Theory]
    [InlineData("45.124", "45.12")]
    [InlineData("45.125", "45.13")]
    [InlineData("45,12", "45.12")]
    [InlineData("1,234.56", "1234.56")]
    [InlineData("1.234,56", "1234.56")]
    [InlineData(" 12 345.678 ", "12345.68")]
    public void LitresNormaliser_parses_and_rounds_deterministic_decimal_formats(string rawValue, string expectedValue)
    {
        var result = LitresNormaliser.Normalise(rawValue);

        Assert.True(result.Success);
        Assert.Equal(rawValue, result.RawValue);
        Assert.Equal(decimal.Parse(expectedValue, CultureInfo.InvariantCulture), result.NormalisedValue?.Value);
        Assert.Null(result.ReasonCode);
    }

    [Theory]
    [InlineData(null, "NormalizationFailed_Litres")]
    [InlineData("", "NormalizationFailed_Litres")]
    [InlineData("abc", "NormalizationFailed_Litres")]
    [InlineData("$10.00", "NormalizationFailed_Litres")]
    [InlineData("-1.00", "NormalizationFailed_Litres")]
    [InlineData("1,234", "AmbiguousNumericFormat")]
    public void LitresNormaliser_returns_reason_code_for_invalid_or_ambiguous_values(string? rawValue, string expectedReasonCode)
    {
        var result = LitresNormaliser.Normalise(rawValue);

        Assert.False(result.Success);
        Assert.Equal(rawValue ?? string.Empty, result.RawValue);
        Assert.Null(result.NormalisedValue);
        Assert.Equal(expectedReasonCode, result.ReasonCode);
    }

    [Theory]
    [InlineData("$123.454", "123.45")]
    [InlineData("$123.455", "123.46")]
    [InlineData("$1,234.56", "1234.56")]
    [InlineData("$1.234,56", "1234.56")]
    [InlineData("$ 1 234,50", "1234.50")]
    [InlineData("-$12.345", "-12.35")]
    [InlineData("$-12.345", "-12.35")]
    public void MoneyAmountNormaliser_parses_and_rounds_money_formats(string rawValue, string expectedValue)
    {
        var result = MoneyAmountNormaliser.Normalise(rawValue);

        Assert.True(result.Success);
        Assert.Equal(rawValue, result.RawValue);
        Assert.Equal(decimal.Parse(expectedValue, CultureInfo.InvariantCulture), result.NormalisedValue?.Value);
        Assert.Null(result.ReasonCode);
    }

    [Theory]
    [InlineData(null, "NormalizationFailed_Amount")]
    [InlineData("", "NormalizationFailed_Amount")]
    [InlineData("abc", "NormalizationFailed_Amount")]
    [InlineData("$", "NormalizationFailed_Amount")]
    [InlineData("$1,234", "AmbiguousNumericFormat")]
    public void MoneyAmountNormaliser_returns_reason_code_for_invalid_or_ambiguous_values(string? rawValue, string expectedReasonCode)
    {
        var result = MoneyAmountNormaliser.Normalise(rawValue);

        Assert.False(result.Success);
        Assert.Equal(rawValue ?? string.Empty, result.RawValue);
        Assert.Null(result.NormalisedValue);
        Assert.Equal(expectedReasonCode, result.ReasonCode);
    }

    [Theory]
    [InlineData("45.125")]
    [InlineData("1,234.56")]
    public void LitresNormalisation_is_idempotent_for_successful_results(string rawValue)
    {
        var first = LitresNormaliser.Normalise(rawValue);
        Assert.True(first.Success);
        Assert.NotNull(first.NormalisedValue);

        var second = LitresNormaliser.Normalise(first.NormalisedValue.Value.ToString());

        Assert.True(second.Success);
        Assert.Equal(first.NormalisedValue.Value.Value, second.NormalisedValue?.Value);
    }

    [Theory]
    [InlineData("$123.455")]
    [InlineData("$1,234.56")]
    public void MoneyAmountNormalisation_is_idempotent_for_successful_results(string rawValue)
    {
        var first = MoneyAmountNormaliser.Normalise(rawValue);
        Assert.True(first.Success);
        Assert.NotNull(first.NormalisedValue);

        var second = MoneyAmountNormaliser.Normalise(first.NormalisedValue.Value.ToString());

        Assert.True(second.Success);
        Assert.Equal(first.NormalisedValue.Value.Value, second.NormalisedValue?.Value);
    }
}
