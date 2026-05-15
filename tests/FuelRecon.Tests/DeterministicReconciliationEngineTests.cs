using FuelRecon.Application.Reconciliation;
using FuelRecon.Domain;

namespace FuelRecon.Tests;

public class DeterministicReconciliationEngineTests
{
    [Fact]
    public void Reconcile_matches_branch_and_cars_by_ra()
    {
        var branch = BranchEntry("10000000-0000-0000-0000-000000000001", ra: "RA-123", litres: 42.1m);
        var cars = CarsEntry("20000000-0000-0000-0000-000000000001", ra: "RA-123", litres: 42.2m);

        var result = Reconcile(branches: [branch], cars: [cars]);

        var item = Assert.Single(result.Items);
        Assert.Equal(ReconciliationStatus.Matched, item.SystemStatus);
        Assert.Equal(ResolutionStatus.Resolved, item.ResolutionStatus);
        Assert.Equal(branch.Id, item.BranchLitresEntryId);
        Assert.Equal(cars.Id, item.CarsBillingEntryId);
        Assert.Contains("MatchedByRA", item.ReasonCodes);
        Assert.Equal(branch.SourceReference, item.BranchSourceReference);
        Assert.Equal(cars.SourceReference, item.CarsSourceReference);
    }

    [Fact]
    public void Reconcile_creates_unbilled_when_cars_billing_is_missing()
    {
        var branch = BranchEntry("10000000-0000-0000-0000-000000000002", ra: "RA-999", litres: 15m);

        var result = Reconcile(branches: [branch], cars: []);

        var item = Assert.Single(result.Items);
        Assert.Equal(ReconciliationStatus.Unbilled, item.SystemStatus);
        Assert.Equal(branch.Id, item.BranchLitresEntryId);
        Assert.Null(item.CarsBillingEntryId);
        Assert.Contains("Unbilled", item.ReasonCodes);
    }

    [Fact]
    public void Reconcile_creates_cars_only_when_cars_billing_has_no_usage()
    {
        var cars = CarsEntry("20000000-0000-0000-0000-000000000002", ra: "RA-777", litres: 10m);

        var result = Reconcile(branches: [], cars: [cars]);

        var item = Assert.Single(result.Items);
        Assert.Equal(ReconciliationStatus.CarsOnly, item.SystemStatus);
        Assert.Equal(cars.Id, item.CarsBillingEntryId);
        Assert.Null(item.BranchLitresEntryId);
        Assert.Contains("CarsOnly", item.ReasonCodes);
    }

    [Fact]
    public void Reconcile_can_match_by_rego_date_and_litres_when_ra_is_absent()
    {
        var branch = BranchEntry("10000000-0000-0000-0000-000000000003", ra: null, rego: "abc-123", litres: 20m);
        var cars = CarsEntry("20000000-0000-0000-0000-000000000003", ra: null, rego: "ABC123", date: new DateOnly(2026, 4, 2), litres: 20.4m);

        var result = Reconcile(branches: [branch], cars: [cars]);

        var item = Assert.Single(result.Items);
        Assert.Equal(ReconciliationStatus.Matched, item.SystemStatus);
        Assert.Equal(cars.Id, item.CarsBillingEntryId);
        Assert.Contains("FallbackRegoDateLitres", item.ReasonCodes);
        Assert.Equal(ConfidenceBucket.Medium, item.ConfidenceBucket);
    }

    [Fact]
    public void Reconcile_marks_duplicate_candidates_as_duplicate_possible()
    {
        var branch = BranchEntry("10000000-0000-0000-0000-000000000004", ra: "RA-DUP", litres: 10m);
        var firstCars = CarsEntry("20000000-0000-0000-0000-000000000004", ra: "RA-DUP", litres: 10m);
        var secondCars = CarsEntry("20000000-0000-0000-0000-000000000005", ra: "RA-DUP", litres: 10m);

        var result = Reconcile(branches: [branch], cars: [firstCars, secondCars]);

        var item = Assert.Single(result.Items);
        Assert.Equal(ReconciliationStatus.DuplicatePossible, item.SystemStatus);
        Assert.Contains("DuplicatePossible", item.ReasonCodes);
        Assert.Contains("MultipleCarsCandidates", item.ReasonCodes);
        Assert.Equal(2, item.MatchCandidates.Count(candidate => candidate.CandidateType == MatchCandidateType.CarsBillingEntry));
    }

    [Fact]
    public void Reconcile_marks_variance_when_litres_difference_exceeds_tolerance()
    {
        var branch = BranchEntry("10000000-0000-0000-0000-000000000005", ra: "RA-VAR", litres: 20m);
        var cars = CarsEntry("20000000-0000-0000-0000-000000000006", ra: "RA-VAR", litres: 18m);

        var result = Reconcile(branches: [branch], cars: [cars]);

        var item = Assert.Single(result.Items);
        Assert.Equal(ReconciliationStatus.Variance, item.SystemStatus);
        Assert.Equal(2m, item.LitresVariance);
        Assert.Contains("LitresVariance", item.ReasonCodes);
    }

    [Fact]
    public void Reconcile_includes_supplier_match_evidence_when_available()
    {
        var branch = BranchEntry("10000000-0000-0000-0000-000000000006", ra: "RA-SUP", litres: 30m);
        var cars = CarsEntry("20000000-0000-0000-0000-000000000007", ra: "RA-SUP", litres: 30m);
        var supplier = SupplierEntry("30000000-0000-0000-0000-000000000001", litres: 30m);

        var result = Reconcile(suppliers: [supplier], branches: [branch], cars: [cars]);

        var item = Assert.Single(result.Items);
        Assert.Equal(supplier.Id, item.SupplierTransactionId);
        Assert.Equal(supplier.SourceReference, item.SupplierSourceReference);
        Assert.Contains(item.MatchCandidates, candidate => candidate.CandidateType == MatchCandidateType.SupplierTransaction);
    }

    [Fact]
    public void Reconcile_returns_branch_summary_totals()
    {
        var branch = BranchEntry("10000000-0000-0000-0000-000000000007", ra: "RA-SUM", litres: 12m);
        var cars = CarsEntry("20000000-0000-0000-0000-000000000008", ra: "RA-SUM", litres: 12m);
        var supplier = SupplierEntry("30000000-0000-0000-0000-000000000002", litres: 12m);

        var result = Reconcile(suppliers: [supplier], branches: [branch], cars: [cars]);

        var summary = Assert.Single(result.BranchSummaries);
        Assert.Equal("TAUPO", summary.BranchId.Value);
        Assert.Equal(12m, summary.SupplierLitres.Value);
        Assert.Equal(12m, summary.BranchLitres.Value);
        Assert.Equal(12m, summary.BilledLitres.Value);
        Assert.Equal(ReconciliationStatus.Matched, summary.Status);
    }

    [Fact]
    public void Reconcile_is_deterministic_for_same_inputs_and_rules()
    {
        var branch = BranchEntry("10000000-0000-0000-0000-000000000008", ra: "RA-DET", litres: 8m);
        var cars = CarsEntry("20000000-0000-0000-0000-000000000009", ra: "RA-DET", litres: 8m);
        var input = new ReconciliationEngineInput(Period, TestImportBatchId, [], [branch], [cars]);
        var engine = new DeterministicReconciliationEngine();

        var first = engine.Reconcile(input);
        var second = engine.Reconcile(input);

        Assert.Equal(first.Run.Id, second.Run.Id);
        Assert.Equal(first.Run.CreatedAtUtc, second.Run.CreatedAtUtc);
        Assert.Equal(first.Items.Select(item => item.Id), second.Items.Select(item => item.Id));
        Assert.Equal(first.Items.Select(item => item.SystemStatus), second.Items.Select(item => item.SystemStatus));
        Assert.Equal(first.BranchSummaries.Select(summary => summary.BranchId.Value), second.BranchSummaries.Select(summary => summary.BranchId.Value));
    }

    private static Guid TestImportBatchId => Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private static FuelPeriod Period => new(2026, 4);

    private static ReconciliationEngineResult Reconcile(
        IReadOnlyList<SupplierTransaction>? suppliers = null,
        IReadOnlyList<BranchLitresEntry>? branches = null,
        IReadOnlyList<CarsBillingEntry>? cars = null,
        ReconciliationRulesOptions? rules = null) =>
        new DeterministicReconciliationEngine().Reconcile(new ReconciliationEngineInput(
            Period,
            TestImportBatchId,
            suppliers ?? [],
            branches ?? [],
            cars ?? [],
            rules));

    private static BranchLitresEntry BranchEntry(
        string id,
        string? ra,
        decimal litres,
        string? rego = null,
        DateOnly? date = null) =>
        new(
            Guid.Parse(id),
            Period,
            new CanonicalBranchId("TAUPO"),
            date ?? new DateOnly(2026, 4, 1),
            new Litres(litres),
            new SourceReference("branch.xlsx", sheetName: "April", rowNumber: 2),
            ra is null ? null : new RentalAgreementNumber(ra),
            rego is null ? null : new Rego(rego));

    private static CarsBillingEntry CarsEntry(
        string id,
        string? ra,
        decimal? litres,
        string? rego = null,
        DateOnly? date = null,
        CanonicalBranchId? branchId = null) =>
        new(
            Guid.Parse(id),
            Period,
            new SourceReference("cars.xlsx", sheetName: "Export", rowNumber: 2),
            branchId ?? new CanonicalBranchId("TAUPO"),
            date ?? new DateOnly(2026, 4, 1),
            ra is null ? null : new RentalAgreementNumber(ra),
            rego is null ? null : new Rego(rego),
            litres is null ? null : new Litres(litres.Value));

    private static SupplierTransaction SupplierEntry(string id, decimal litres) =>
        new(
            Guid.Parse(id),
            "Mobil",
            Period,
            new DateOnly(2026, 4, 1),
            new Litres(litres),
            new SourceReference("supplier.pdf", pageNumber: 1),
            new CanonicalBranchId("TAUPO"));
}
