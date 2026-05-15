using System.Globalization;
using FuelRecon.Domain;

namespace FuelRecon.Tests;

public class CoreValueObjectsTests
{
    [Theory]
    [InlineData(2026, 4, "April 2026", "2026-04")]
    [InlineData(2026, 5, "May 2026", "2026-05")]
    public void FuelPeriod_constructs_from_valid_year_and_month(int year, int month, string expectedDisplay, string expectedSortable)
    {
        var period = new FuelPeriod(year, month);

        Assert.Equal(year, period.Year);
        Assert.Equal(month, period.Month);
        Assert.Equal(expectedDisplay, period.ToString());
        Assert.Equal(expectedSortable, period.ToSortableString());
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    public void FuelPeriod_rejects_invalid_year_or_month(int year, int month)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FuelPeriod(year, month));
    }

    [Theory]
    [InlineData("April 2026", 2026, 4)]
    [InlineData("May 2026", 2026, 5)]
    [InlineData("2026-04", 2026, 4)]
    [InlineData("2026/05", 2026, 5)]
    public void FuelPeriod_parses_supported_period_formats(string value, int expectedYear, int expectedMonth)
    {
        var period = FuelPeriod.Parse(value);

        Assert.Equal(expectedYear, period.Year);
        Assert.Equal(expectedMonth, period.Month);
    }

    [Fact]
    public void FuelPeriod_try_parse_returns_false_for_invalid_period()
    {
        var result = FuelPeriod.TryParse("not a period", out var period);

        Assert.False(result);
        Assert.Equal(default, period);
    }

    [Fact]
    public void CanonicalBranchId_trims_valid_value()
    {
        var branchId = new CanonicalBranchId("  TAUPO  ");

        Assert.Equal("TAUPO", branchId.Value);
        Assert.Equal("TAUPO", branchId.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CanonicalBranchId_rejects_empty_value(string value)
    {
        Assert.Throws<ArgumentException>(() => new CanonicalBranchId(value));
    }

    [Theory]
    [InlineData("abc-123", "ABC123")]
    [InlineData(" AB C-1 23 ", "ABC123")]
    [InlineData("nz 987", "NZ987")]
    public void Rego_preserves_raw_value_and_normalises_registration(string rawValue, string expectedNormalised)
    {
        var rego = new Rego(rawValue);

        Assert.Equal(rawValue, rego.RawValue);
        Assert.Equal(expectedNormalised, rego.NormalisedValue);
        Assert.Equal(expectedNormalised, rego.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("---")]
    public void Rego_rejects_empty_or_formatting_only_values(string rawValue)
    {
        Assert.Throws<ArgumentException>(() => new Rego(rawValue));
    }

    [Theory]
    [InlineData(" RA-123.0 ", "RA1230")]
    [InlineData("ab 12-34", "AB1234")]
    [InlineData("A/B-42", "A/B42")]
    public void RentalAgreementNumber_preserves_raw_value_and_normalises_conservatively(string rawValue, string expectedNormalised)
    {
        var rentalAgreementNumber = new RentalAgreementNumber(rawValue);

        Assert.Equal(rawValue, rentalAgreementNumber.RawValue);
        Assert.Equal(expectedNormalised, rentalAgreementNumber.NormalisedValue);
        Assert.Equal(expectedNormalised, rentalAgreementNumber.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" - . - ")]
    public void RentalAgreementNumber_rejects_empty_or_formatting_only_values(string rawValue)
    {
        Assert.Throws<ArgumentException>(() => new RentalAgreementNumber(rawValue));
    }

    [Theory]
    [InlineData("123.454", "123.45")]
    [InlineData("123.455", "123.46")]
    [InlineData("-1.235", "-1.24")]
    public void MoneyAmount_rounds_to_two_decimals_with_stable_midpoint_handling(string rawValue, string expectedValue)
    {
        var amount = new MoneyAmount(decimal.Parse(rawValue, CultureInfo.InvariantCulture));

        Assert.Equal(decimal.Parse(expectedValue, CultureInfo.InvariantCulture), amount.Value);
        Assert.Equal(expectedValue, amount.ToString());
    }

    [Theory]
    [InlineData("45.124", "45.12")]
    [InlineData("45.125", "45.13")]
    [InlineData("0", "0.00")]
    public void Litres_rounds_to_two_decimals_and_formats_with_two_decimal_places(string rawValue, string expectedValue)
    {
        var litres = new Litres(decimal.Parse(rawValue, CultureInfo.InvariantCulture));

        Assert.Equal(decimal.Parse(expectedValue, CultureInfo.InvariantCulture), litres.Value);
        Assert.Equal(expectedValue, litres.ToString());
    }

    [Fact]
    public void Litres_rejects_negative_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Litres(-0.01m));
    }

    [Fact]
    public void SourceReference_stores_source_file_and_optional_location()
    {
        var reference = new SourceReference(
            " branch.xlsx ",
            sheetName: " Sheet1 ",
            rowNumber: 12,
            pageNumber: 2,
            referenceText: " RA column ");

        Assert.Equal("branch.xlsx", reference.SourceFile);
        Assert.Equal("Sheet1", reference.SheetName);
        Assert.Equal(12, reference.RowNumber);
        Assert.Equal(2, reference.PageNumber);
        Assert.Equal("RA column", reference.ReferenceText);
        Assert.Equal("branch.xlsx, sheet Sheet1, row 12, page 2, RA column", reference.ToString());
    }

    [Fact]
    public void SourceReference_converts_blank_optional_strings_to_null()
    {
        var reference = new SourceReference("source.pdf", sheetName: " ", referenceText: " ");

        Assert.Null(reference.SheetName);
        Assert.Null(reference.ReferenceText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SourceReference_rejects_empty_source_file(string sourceFile)
    {
        Assert.Throws<ArgumentException>(() => new SourceReference(sourceFile));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    [InlineData(null, 0)]
    [InlineData(null, -1)]
    public void SourceReference_rejects_non_positive_row_or_page_numbers(int? rowNumber, int? pageNumber)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SourceReference(
            "source.xlsx",
            rowNumber: rowNumber,
            pageNumber: pageNumber));
    }

    [Fact]
    public void CorrelationId_wraps_non_empty_string()
    {
        var correlationId = new CorrelationId(" abc-123 ");

        Assert.Equal("abc-123", correlationId.Value);
        Assert.Equal("abc-123", correlationId.ToString());
    }

    [Fact]
    public void CorrelationId_can_be_created_from_guid()
    {
        var guid = Guid.Parse("f17025b1-0c2b-49fe-979d-f01be8229073");
        var correlationId = CorrelationId.FromGuid(guid);

        Assert.Equal("f17025b10c2b49fe979df01be8229073", correlationId.Value);
    }

    [Fact]
    public void CorrelationId_rejects_empty_values()
    {
        Assert.Throws<ArgumentException>(() => new CorrelationId(" "));
        Assert.Throws<ArgumentException>(() => CorrelationId.FromGuid(Guid.Empty));
    }

    [Fact]
    public void FileChecksum_trims_and_stores_algorithm_and_value()
    {
        var checksum = new FileChecksum(" sha256 ", " abcdef ");

        Assert.Equal("SHA256", checksum.Algorithm);
        Assert.Equal("abcdef", checksum.Value);
        Assert.Equal("SHA256:abcdef", checksum.ToString());
    }

    [Theory]
    [InlineData("", "value")]
    [InlineData("   ", "value")]
    [InlineData("SHA256", "")]
    [InlineData("SHA256", "   ")]
    public void FileChecksum_rejects_empty_algorithm_or_value(string algorithm, string value)
    {
        Assert.Throws<ArgumentException>(() => new FileChecksum(algorithm, value));
    }
}
