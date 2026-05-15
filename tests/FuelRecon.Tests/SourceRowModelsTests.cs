using FuelRecon.Domain;

namespace FuelRecon.Tests;

public class SourceRowModelsTests
{
    [Fact]
    public void SupplierTransaction_constructs_with_source_reference_and_resolved_values()
    {
        var id = Guid.Parse("d5236f1d-9f04-4d4d-a5ef-641275100001");
        var period = new FuelPeriod(2026, 4);
        var branchId = new CanonicalBranchId("TAUPO");
        var sourceReference = new SourceReference("mobil.pdf", pageNumber: 3, referenceText: "line 42");
        var litres = new Litres(42.125m);
        var amount = new MoneyAmount(123.456m);

        var transaction = new SupplierTransaction(
            id,
            " Mobil ",
            period,
            new DateOnly(2026, 4, 15),
            litres,
            sourceReference,
            branchId,
            rawBranchText: " Hertz Taupo ",
            rawSiteText: " Mobil Taupo ",
            cardholder: " Driver One ",
            voucherOrInvoiceReference: " INV-123 ",
            product: " Diesel ",
            amount: amount,
            validationIssueCodes: [" SupplierBranchUnresolved ", "", "LowConfidenceMatch"]);

        Assert.Equal(id, transaction.Id);
        Assert.Equal("Mobil", transaction.SupplierName);
        Assert.Equal(period, transaction.Period);
        Assert.Equal(branchId, transaction.BranchId);
        Assert.Equal(new DateOnly(2026, 4, 15), transaction.TransactionDate);
        Assert.Equal("Hertz Taupo", transaction.RawBranchText);
        Assert.Equal("Mobil Taupo", transaction.RawSiteText);
        Assert.Equal("Driver One", transaction.Cardholder);
        Assert.Equal("INV-123", transaction.VoucherOrInvoiceReference);
        Assert.Equal("Diesel", transaction.Product);
        Assert.Equal(42.13m, transaction.Litres.Value);
        Assert.Equal(123.46m, transaction.Amount?.Value);
        Assert.Same(sourceReference, transaction.SourceReference);
        Assert.Equal(["SupplierBranchUnresolved", "LowConfidenceMatch"], transaction.ValidationIssueCodes);
    }

    [Fact]
    public void SupplierTransaction_allows_optional_values_to_be_absent()
    {
        var transaction = new SupplierTransaction(
            Guid.Parse("d5236f1d-9f04-4d4d-a5ef-641275100002"),
            "Farmlands",
            new FuelPeriod(2026, 5),
            new DateOnly(2026, 5, 1),
            new Litres(1.2m),
            new SourceReference("farmlands.pdf", pageNumber: 1));

        Assert.Null(transaction.BranchId);
        Assert.Null(transaction.RawBranchText);
        Assert.Null(transaction.RawSiteText);
        Assert.Null(transaction.Cardholder);
        Assert.Null(transaction.VoucherOrInvoiceReference);
        Assert.Null(transaction.Product);
        Assert.Null(transaction.Amount);
        Assert.Empty(transaction.ValidationIssueCodes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SupplierTransaction_rejects_empty_supplier_name(string supplierName)
    {
        Assert.Throws<ArgumentException>(() => new SupplierTransaction(
            Guid.Parse("d5236f1d-9f04-4d4d-a5ef-641275100003"),
            supplierName,
            new FuelPeriod(2026, 4),
            new DateOnly(2026, 4, 1),
            new Litres(10m),
            new SourceReference("supplier.pdf")));
    }

    [Fact]
    public void SupplierTransaction_rejects_empty_id_and_null_source_reference()
    {
        Assert.Throws<ArgumentException>(() => new SupplierTransaction(
            Guid.Empty,
            "Mobil",
            new FuelPeriod(2026, 4),
            new DateOnly(2026, 4, 1),
            new Litres(10m),
            new SourceReference("supplier.pdf")));

        Assert.Throws<ArgumentNullException>(() => new SupplierTransaction(
            Guid.Parse("d5236f1d-9f04-4d4d-a5ef-641275100004"),
            "Mobil",
            new FuelPeriod(2026, 4),
            new DateOnly(2026, 4, 1),
            new Litres(10m),
            null!));
    }

    [Fact]
    public void BranchLitresEntry_constructs_with_raw_and_normalised_ra_and_rego()
    {
        var id = Guid.Parse("d5236f1d-9f04-4d4d-a5ef-641275100005");
        var period = new FuelPeriod(2026, 4);
        var branchId = new CanonicalBranchId("ROTORUA");
        var rentalAgreementNumber = new RentalAgreementNumber(" RA-123 ");
        var rego = new Rego("abc-123");
        var sourceReference = new SourceReference("branch.xlsx", sheetName: "April", rowNumber: 12);

        var entry = new BranchLitresEntry(
            id,
            period,
            branchId,
            new DateOnly(2026, 4, 20),
            new Litres(55.555m),
            sourceReference,
            rentalAgreementNumber,
            rego,
            noteOrReference: " customer note ",
            validationIssueCodes: ["InvalidRA"]);

        Assert.Equal(id, entry.Id);
        Assert.Equal(period, entry.Period);
        Assert.Equal(branchId, entry.BranchId);
        Assert.Equal(new DateOnly(2026, 4, 20), entry.Date);
        Assert.Equal(" RA-123 ", entry.RentalAgreementNumber?.RawValue);
        Assert.Equal("RA123", entry.RentalAgreementNumber?.NormalisedValue);
        Assert.Equal("abc-123", entry.Rego?.RawValue);
        Assert.Equal("ABC123", entry.Rego?.NormalisedValue);
        Assert.Equal("customer note", entry.NoteOrReference);
        Assert.Equal(55.56m, entry.Litres.Value);
        Assert.Same(sourceReference, entry.SourceReference);
        Assert.Equal(["InvalidRA"], entry.ValidationIssueCodes);
    }

    [Fact]
    public void BranchLitresEntry_allows_optional_ra_rego_and_note_to_be_absent()
    {
        var entry = new BranchLitresEntry(
            Guid.Parse("d5236f1d-9f04-4d4d-a5ef-641275100006"),
            new FuelPeriod(2026, 5),
            new CanonicalBranchId("TAUPO"),
            new DateOnly(2026, 5, 2),
            new Litres(22m),
            new SourceReference("branch.xlsx", sheetName: "May", rowNumber: 2));

        Assert.Null(entry.RentalAgreementNumber);
        Assert.Null(entry.Rego);
        Assert.Null(entry.NoteOrReference);
        Assert.Empty(entry.ValidationIssueCodes);
    }

    [Fact]
    public void BranchLitresEntry_rejects_empty_id_and_null_source_reference()
    {
        Assert.Throws<ArgumentException>(() => new BranchLitresEntry(
            Guid.Empty,
            new FuelPeriod(2026, 4),
            new CanonicalBranchId("TAUPO"),
            new DateOnly(2026, 4, 1),
            new Litres(10m),
            new SourceReference("branch.xlsx")));

        Assert.Throws<ArgumentNullException>(() => new BranchLitresEntry(
            Guid.Parse("d5236f1d-9f04-4d4d-a5ef-641275100007"),
            new FuelPeriod(2026, 4),
            new CanonicalBranchId("TAUPO"),
            new DateOnly(2026, 4, 1),
            new Litres(10m),
            null!));
    }

    [Fact]
    public void CarsBillingEntry_constructs_with_optional_billing_fields_present()
    {
        var id = Guid.Parse("d5236f1d-9f04-4d4d-a5ef-641275100008");
        var period = new FuelPeriod(2026, 4);
        var branchId = new CanonicalBranchId("QUEENSTOWN");
        var rentalAgreementNumber = new RentalAgreementNumber("AB-987");
        var rego = new Rego("qq-777");
        var billedLitres = new Litres(31.234m);
        var billedAmount = new MoneyAmount(98.765m);
        var sourceReference = new SourceReference("cars.xlsx", sheetName: "Export", rowNumber: 25);

        var entry = new CarsBillingEntry(
            id,
            period,
            sourceReference,
            branchId,
            date: new DateOnly(2026, 4, 21),
            rentalAgreementNumber: rentalAgreementNumber,
            rego: rego,
            billedLitres: billedLitres,
            billedAmount: billedAmount,
            billingStatus: " Closed ",
            validationIssueCodes: ["MissingCharge", " AmountVariance "]);

        Assert.Equal(id, entry.Id);
        Assert.Equal(period, entry.Period);
        Assert.Equal(branchId, entry.BranchId);
        Assert.Equal(new DateOnly(2026, 4, 21), entry.Date);
        Assert.Equal("AB-987", entry.RentalAgreementNumber?.RawValue);
        Assert.Equal("AB987", entry.RentalAgreementNumber?.NormalisedValue);
        Assert.Equal("qq-777", entry.Rego?.RawValue);
        Assert.Equal("QQ777", entry.Rego?.NormalisedValue);
        Assert.Equal(31.23m, entry.BilledLitres?.Value);
        Assert.Equal(98.77m, entry.BilledAmount?.Value);
        Assert.Equal("Closed", entry.BillingStatus);
        Assert.Same(sourceReference, entry.SourceReference);
        Assert.Equal(["MissingCharge", "AmountVariance"], entry.ValidationIssueCodes);
    }

    [Fact]
    public void CarsBillingEntry_allows_optional_values_to_be_absent()
    {
        var entry = new CarsBillingEntry(
            Guid.Parse("d5236f1d-9f04-4d4d-a5ef-641275100009"),
            new FuelPeriod(2026, 5),
            new SourceReference("cars.xlsx", sheetName: "May", rowNumber: 4));

        Assert.Null(entry.BranchId);
        Assert.Null(entry.Date);
        Assert.Null(entry.RentalAgreementNumber);
        Assert.Null(entry.Rego);
        Assert.Null(entry.BilledLitres);
        Assert.Null(entry.BilledAmount);
        Assert.Null(entry.BillingStatus);
        Assert.Empty(entry.ValidationIssueCodes);
    }

    [Fact]
    public void CarsBillingEntry_rejects_empty_id_and_null_source_reference()
    {
        Assert.Throws<ArgumentException>(() => new CarsBillingEntry(
            Guid.Empty,
            new FuelPeriod(2026, 4),
            new SourceReference("cars.xlsx")));

        Assert.Throws<ArgumentNullException>(() => new CarsBillingEntry(
            Guid.Parse("d5236f1d-9f04-4d4d-a5ef-641275100010"),
            new FuelPeriod(2026, 4),
            null!));
    }
}
