namespace FuelRecon.Domain;

/// <summary>
/// Parsed supplier fuel transaction with raw supplier text, resolved domain values where available,
/// and source-file traceability.
/// </summary>
public sealed record SupplierTransaction
{
    public SupplierTransaction(
        Guid id,
        string supplierName,
        FuelPeriod period,
        DateOnly transactionDate,
        Litres litres,
        SourceReference sourceReference,
        CanonicalBranchId? branchId = null,
        string? rawBranchText = null,
        string? rawSiteText = null,
        string? cardholder = null,
        string? voucherOrInvoiceReference = null,
        string? product = null,
        MoneyAmount? amount = null,
        IEnumerable<string>? validationIssueCodes = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Supplier transaction ID cannot be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(supplierName))
        {
            throw new ArgumentException("Supplier name cannot be empty.", nameof(supplierName));
        }

        Id = id;
        SupplierName = supplierName.Trim();
        Period = period;
        BranchId = branchId;
        TransactionDate = transactionDate;
        RawBranchText = TrimToNull(rawBranchText);
        RawSiteText = TrimToNull(rawSiteText);
        Cardholder = TrimToNull(cardholder);
        VoucherOrInvoiceReference = TrimToNull(voucherOrInvoiceReference);
        Product = TrimToNull(product);
        Litres = litres;
        Amount = amount;
        SourceReference = sourceReference ?? throw new ArgumentNullException(nameof(sourceReference));
        ValidationIssueCodes = NormaliseIssueCodes(validationIssueCodes);
    }

    public Guid Id { get; }

    public string SupplierName { get; }

    public FuelPeriod Period { get; }

    public CanonicalBranchId? BranchId { get; }

    public DateOnly TransactionDate { get; }

    public string? RawBranchText { get; }

    public string? RawSiteText { get; }

    public string? Cardholder { get; }

    public string? VoucherOrInvoiceReference { get; }

    public string? Product { get; }

    public Litres Litres { get; }

    public MoneyAmount? Amount { get; }

    public SourceReference SourceReference { get; }

    public IReadOnlyList<string> ValidationIssueCodes { get; }

    private static string? TrimToNull(string? value) => SourceRowModelHelpers.TrimToNull(value);

    private static IReadOnlyList<string> NormaliseIssueCodes(IEnumerable<string>? issueCodes) =>
        SourceRowModelHelpers.NormaliseIssueCodes(issueCodes);
}

/// <summary>
/// Parsed branch litres row preserving RA/rego value objects and source-file traceability.
/// </summary>
public sealed record BranchLitresEntry
{
    public BranchLitresEntry(
        Guid id,
        FuelPeriod period,
        CanonicalBranchId branchId,
        DateOnly date,
        Litres litres,
        SourceReference sourceReference,
        RentalAgreementNumber? rentalAgreementNumber = null,
        Rego? rego = null,
        string? noteOrReference = null,
        IEnumerable<string>? validationIssueCodes = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Branch litres entry ID cannot be empty.", nameof(id));
        }

        Id = id;
        Period = period;
        BranchId = branchId;
        Date = date;
        RentalAgreementNumber = rentalAgreementNumber;
        Rego = rego;
        NoteOrReference = SourceRowModelHelpers.TrimToNull(noteOrReference);
        Litres = litres;
        SourceReference = sourceReference ?? throw new ArgumentNullException(nameof(sourceReference));
        ValidationIssueCodes = SourceRowModelHelpers.NormaliseIssueCodes(validationIssueCodes);
    }

    public Guid Id { get; }

    public FuelPeriod Period { get; }

    public CanonicalBranchId BranchId { get; }

    public DateOnly Date { get; }

    public RentalAgreementNumber? RentalAgreementNumber { get; }

    public Rego? Rego { get; }

    public string? NoteOrReference { get; }

    public Litres Litres { get; }

    public SourceReference SourceReference { get; }

    public IReadOnlyList<string> ValidationIssueCodes { get; }
}

/// <summary>
/// Parsed Cars+ billing row with optional billing fields and source-file traceability.
/// </summary>
public sealed record CarsBillingEntry
{
    public CarsBillingEntry(
        Guid id,
        FuelPeriod period,
        SourceReference sourceReference,
        CanonicalBranchId? branchId = null,
        DateOnly? date = null,
        RentalAgreementNumber? rentalAgreementNumber = null,
        Rego? rego = null,
        Litres? billedLitres = null,
        MoneyAmount? billedAmount = null,
        string? billingStatus = null,
        IEnumerable<string>? validationIssueCodes = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Cars billing entry ID cannot be empty.", nameof(id));
        }

        Id = id;
        Period = period;
        BranchId = branchId;
        Date = date;
        RentalAgreementNumber = rentalAgreementNumber;
        Rego = rego;
        BilledLitres = billedLitres;
        BilledAmount = billedAmount;
        BillingStatus = SourceRowModelHelpers.TrimToNull(billingStatus);
        SourceReference = sourceReference ?? throw new ArgumentNullException(nameof(sourceReference));
        ValidationIssueCodes = SourceRowModelHelpers.NormaliseIssueCodes(validationIssueCodes);
    }

    public Guid Id { get; }

    public FuelPeriod Period { get; }

    public CanonicalBranchId? BranchId { get; }

    public DateOnly? Date { get; }

    public RentalAgreementNumber? RentalAgreementNumber { get; }

    public Rego? Rego { get; }

    public Litres? BilledLitres { get; }

    public MoneyAmount? BilledAmount { get; }

    public string? BillingStatus { get; }

    public SourceReference SourceReference { get; }

    public IReadOnlyList<string> ValidationIssueCodes { get; }
}

internal static class SourceRowModelHelpers
{
    internal static string? TrimToNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    internal static IReadOnlyList<string> NormaliseIssueCodes(IEnumerable<string>? issueCodes)
    {
        if (issueCodes is null)
        {
            return Array.Empty<string>();
        }

        return issueCodes
            .Select(TrimToNull)
            .Where(issueCode => issueCode is not null)
            .Select(issueCode => issueCode!)
            .ToArray();
    }
}
