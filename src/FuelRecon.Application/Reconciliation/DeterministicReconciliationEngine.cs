using System.Security.Cryptography;
using System.Text;
using FuelRecon.Domain;

namespace FuelRecon.Application.Reconciliation;

public interface IReconciliationEngine
{
    ReconciliationEngineResult Reconcile(ReconciliationEngineInput input);
}

public sealed class DeterministicReconciliationEngine : IReconciliationEngine
{
    public ReconciliationEngineResult Reconcile(ReconciliationEngineInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var rules = input.Rules ?? ReconciliationRulesOptions.Default;
        var runId = DeterministicGuid("run", input.Period.ToSortableString());
        var items = new List<ReconciliationItem>();
        var matchedCarsIds = new HashSet<Guid>();

        var supplierByBranch = input.SupplierTransactions
            .Where(transaction => transaction.Period == input.Period)
            .OrderBy(transaction => transaction.TransactionDate)
            .ThenBy(transaction => transaction.Id)
            .ToArray();

        var branches = input.BranchLitresEntries
            .Where(entry => entry.Period == input.Period)
            .OrderBy(entry => entry.BranchId.Value, StringComparer.Ordinal)
            .ThenBy(entry => entry.Date)
            .ThenBy(entry => entry.Id)
            .ToArray();

        var cars = input.CarsBillingEntries
            .Where(entry => entry.Period == input.Period)
            .OrderBy(entry => entry.BranchId?.Value ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(entry => entry.Date ?? DateOnly.MinValue)
            .ThenBy(entry => entry.Id)
            .ToArray();

        foreach (var branchEntry in branches)
        {
            var supplierCandidates = FindSupplierCandidates(branchEntry, supplierByBranch, rules);
            var match = FindCarsMatch(branchEntry, cars, rules);

            if (match.Status != ReconciliationStatus.Unbilled)
            {
                foreach (var carsId in match.CarsIds)
                {
                    matchedCarsIds.Add(carsId);
                }
            }

            items.Add(CreateItem(input.Period, runId, branchEntry, supplierCandidates, match, items.Count));
        }

        foreach (var carsEntry in cars.Where(carsEntry => !matchedCarsIds.Contains(carsEntry.Id)))
        {
            items.Add(CreateCarsOnlyItem(input.Period, runId, carsEntry, items.Count));
        }

        var summaries = CreateBranchSummaries(input.Period, runId, branches, supplierByBranch, cars, items);
        var matchedCount = items.Count(item => item.SystemStatus == ReconciliationStatus.Matched);
        var reviewCount = items.Count(item => item.SystemStatus is ReconciliationStatus.ReviewRequired or ReconciliationStatus.DuplicatePossible);
        var estimatedRecovery = summaries.Aggregate(0m, (total, summary) => total + summary.EstimatedRecovery.Value);

        var run = new ReconciliationRun(
            runId,
            input.Period,
            rules.RunCreatedAtUtc ?? DateTimeOffset.UnixEpoch,
            rules.CreatedBy,
            [new FileChecksum("DETERMINISTIC", DeterministicHash(input.Period.ToSortableString()))],
            status: ReconciliationRunStatus.Completed,
            completedAtUtc: rules.RunCreatedAtUtc ?? DateTimeOffset.UnixEpoch,
            totalItemCount: items.Count,
            matchedItemCount: matchedCount,
            reviewRequiredCount: reviewCount,
            estimatedRecoveryTotal: new MoneyAmount(estimatedRecovery));

        return new ReconciliationEngineResult(run, items, summaries);
    }

    private static CarsMatchResult FindCarsMatch(BranchLitresEntry branchEntry, IReadOnlyList<CarsBillingEntry> cars, ReconciliationRulesOptions rules)
    {
        if (branchEntry.RentalAgreementNumber is not null)
        {
            var raCandidates = cars
                .Where(carsEntry => carsEntry.RentalAgreementNumber?.NormalisedValue == branchEntry.RentalAgreementNumber.NormalisedValue)
                .OrderBy(carsEntry => carsEntry.Id)
                .ToArray();

            if (raCandidates.Length > 1)
            {
                return new CarsMatchResult(
                    ReconciliationStatus.DuplicatePossible,
                    ConfidenceBucket.Low,
                    ["DuplicatePossible", "MultipleCarsCandidates"],
                    raCandidates,
                    "Multiple Cars+ rows matched the same RA.");
            }

            if (raCandidates.Length == 1)
            {
                return CreateMatchedOrVarianceResult(branchEntry, raCandidates[0], rules, ConfidenceBucket.High, ["MatchedByRA"]);
            }
        }

        var fallbackCandidates = Array.Empty<CarsBillingEntry>();
        if (branchEntry.Rego is not null)
        {
            fallbackCandidates = cars
                .Where(carsEntry => carsEntry.Rego?.NormalisedValue == branchEntry.Rego.NormalisedValue)
                .Where(carsEntry => carsEntry.Date is not null && Math.Abs(carsEntry.Date.Value.DayNumber - branchEntry.Date.DayNumber) <= rules.DateToleranceDays)
                .Where(carsEntry => carsEntry.BilledLitres is not null && Math.Abs(carsEntry.BilledLitres.Value.Value - branchEntry.Litres.Value) <= rules.LitresTolerance)
                .OrderBy(carsEntry => Math.Abs(carsEntry.Date!.Value.DayNumber - branchEntry.Date.DayNumber))
                .ThenBy(carsEntry => Math.Abs(carsEntry.BilledLitres!.Value.Value - branchEntry.Litres.Value))
                .ThenBy(carsEntry => carsEntry.Id)
                .ToArray();
        }

        if (fallbackCandidates.Length > 1)
        {
            return new CarsMatchResult(
                ReconciliationStatus.DuplicatePossible,
                ConfidenceBucket.Low,
                ["DuplicatePossible", "MultipleCarsCandidates", "FallbackRegoDateLitres"],
                fallbackCandidates,
                "Multiple fallback Cars+ candidates matched rego/date/litres.");
        }

        if (fallbackCandidates.Length == 1)
        {
            return CreateMatchedOrVarianceResult(branchEntry, fallbackCandidates[0], rules, ConfidenceBucket.Medium, ["FallbackRegoDateLitres"]);
        }

        return new CarsMatchResult(
            ReconciliationStatus.Unbilled,
            branchEntry.RentalAgreementNumber is null ? ConfidenceBucket.Low : ConfidenceBucket.Medium,
            branchEntry.RentalAgreementNumber is null ? ["MissingRA", "Unbilled"] : ["Unbilled"],
            [],
            "No Cars+ billing row matched branch usage.");
    }

    private static CarsMatchResult CreateMatchedOrVarianceResult(
        BranchLitresEntry branchEntry,
        CarsBillingEntry carsEntry,
        ReconciliationRulesOptions rules,
        ConfidenceBucket confidence,
        IReadOnlyList<string> baseReasonCodes)
    {
        var variance = carsEntry.BilledLitres is null ? 0m : branchEntry.Litres.Value - carsEntry.BilledLitres.Value.Value;
        if (Math.Abs(variance) > rules.LitresTolerance)
        {
            return new CarsMatchResult(
                ReconciliationStatus.Variance,
                confidence,
                [.. baseReasonCodes, "LitresVariance"],
                [carsEntry],
                "Matched row has litres variance outside tolerance.",
                variance);
        }

        return new CarsMatchResult(
            ReconciliationStatus.Matched,
            confidence,
            baseReasonCodes,
            [carsEntry],
            "Branch usage matched Cars+ billing.",
            variance);
    }

    private static IReadOnlyList<SupplierTransaction> FindSupplierCandidates(
        BranchLitresEntry branchEntry,
        IReadOnlyList<SupplierTransaction> suppliers,
        ReconciliationRulesOptions rules) =>
        suppliers
            .Where(transaction => transaction.BranchId?.Value == branchEntry.BranchId.Value)
            .Where(transaction => Math.Abs(transaction.TransactionDate.DayNumber - branchEntry.Date.DayNumber) <= rules.DateToleranceDays)
            .Where(transaction => Math.Abs(transaction.Litres.Value - branchEntry.Litres.Value) <= rules.LitresTolerance)
            .OrderBy(transaction => Math.Abs(transaction.TransactionDate.DayNumber - branchEntry.Date.DayNumber))
            .ThenBy(transaction => Math.Abs(transaction.Litres.Value - branchEntry.Litres.Value))
            .ThenBy(transaction => transaction.Id)
            .ToArray();

    private static ReconciliationItem CreateItem(
        FuelPeriod period,
        Guid runId,
        BranchLitresEntry branchEntry,
        IReadOnlyList<SupplierTransaction> supplierCandidates,
        CarsMatchResult match,
        int index)
    {
        var candidates = new List<MatchCandidate>();
        candidates.AddRange(supplierCandidates.Select(candidate => new MatchCandidate(
            DeterministicGuid("candidate", branchEntry.Id.ToString(), candidate.Id.ToString()),
            MatchCandidateType.SupplierTransaction,
            candidate.Id,
            ConfidenceBucket.Medium,
            ["BranchId", "Date", "Litres"],
            sourceReference: candidate.SourceReference)));
        candidates.AddRange(match.CarsCandidates.Select(candidate => new MatchCandidate(
            DeterministicGuid("candidate", branchEntry.Id.ToString(), candidate.Id.ToString()),
            MatchCandidateType.CarsBillingEntry,
            candidate.Id,
            match.ConfidenceBucket,
            match.Status == ReconciliationStatus.Matched ? ["RA", "Rego", "Date", "Litres"] : [],
            sourceReference: candidate.SourceReference)));

        var reasonCodes = match.ReasonCodes.ToList();
        if (supplierCandidates.Count > 1)
        {
            reasonCodes.Add("MultipleSupplierCandidates");
        }

        return new ReconciliationItem(
            DeterministicGuid("item", runId.ToString(), "branch", branchEntry.Id.ToString(), index.ToString()),
            runId,
            period,
            match.Status,
            match.Status == ReconciliationStatus.Matched ? ResolutionStatus.Resolved : ResolutionStatus.Unresolved,
            supplierCandidates.Count > 1 ? ConfidenceBucket.Low : match.ConfidenceBucket,
            reasonCodes,
            branchEntry.BranchId,
            finalStatus: null,
            humanReadableReason: match.HumanReadableReason,
            supplierTransactionId: supplierCandidates.Count == 1 ? supplierCandidates[0].Id : null,
            branchLitresEntryId: branchEntry.Id,
            carsBillingEntryId: match.CarsCandidates.Count == 1 ? match.CarsCandidates[0].Id : null,
            supplierSourceReference: supplierCandidates.Count == 1 ? supplierCandidates[0].SourceReference : null,
            branchSourceReference: branchEntry.SourceReference,
            carsSourceReference: match.CarsCandidates.Count == 1 ? match.CarsCandidates[0].SourceReference : null,
            matchCandidates: candidates,
            litresVariance: match.LitresVariance ?? (match.Status == ReconciliationStatus.Unbilled ? branchEntry.Litres.Value : null));
    }

    private static ReconciliationItem CreateCarsOnlyItem(FuelPeriod period, Guid runId, CarsBillingEntry carsEntry, int index) =>
        new(
            DeterministicGuid("item", runId.ToString(), "cars", carsEntry.Id.ToString(), index.ToString()),
            runId,
            period,
            ReconciliationStatus.CarsOnly,
            ResolutionStatus.Unresolved,
            ConfidenceBucket.Medium,
            ["CarsOnly"],
            carsEntry.BranchId,
            humanReadableReason: "Cars+ billing row has no branch usage match.",
            carsBillingEntryId: carsEntry.Id,
            carsSourceReference: carsEntry.SourceReference,
            matchCandidates:
            [
                new MatchCandidate(
                    DeterministicGuid("candidate", carsEntry.Id.ToString()),
                    MatchCandidateType.CarsBillingEntry,
                    carsEntry.Id,
                    ConfidenceBucket.Medium,
                    sourceReference: carsEntry.SourceReference)
            ]);

    private static IReadOnlyList<BranchSummary> CreateBranchSummaries(
        FuelPeriod period,
        Guid runId,
        IReadOnlyList<BranchLitresEntry> branches,
        IReadOnlyList<SupplierTransaction> suppliers,
        IReadOnlyList<CarsBillingEntry> cars,
        IReadOnlyList<ReconciliationItem> items)
    {
        var branchIds = branches.Select(entry => entry.BranchId)
            .Concat(suppliers.Where(transaction => transaction.BranchId is not null).Select(transaction => transaction.BranchId!.Value))
            .Concat(cars.Where(entry => entry.BranchId is not null).Select(entry => entry.BranchId!.Value))
            .DistinctBy(branchId => branchId.Value)
            .OrderBy(branchId => branchId.Value, StringComparer.Ordinal)
            .ToArray();

        return branchIds.Select(branchId =>
        {
            var branchItems = items.Where(item => item.BranchId?.Value == branchId.Value).ToArray();
            var supplierLitres = suppliers.Where(transaction => transaction.BranchId?.Value == branchId.Value).Sum(transaction => transaction.Litres.Value);
            var branchLitres = branches.Where(entry => entry.BranchId.Value == branchId.Value).Sum(entry => entry.Litres.Value);
            var billedLitres = cars.Where(entry => entry.BranchId?.Value == branchId.Value && entry.BilledLitres is not null).Sum(entry => entry.BilledLitres!.Value.Value);
            var unbilledLitres = branchItems.Where(item => item.SystemStatus == ReconciliationStatus.Unbilled).Sum(item => item.LitresVariance ?? 0m);
            var status = branchItems.Any(item => item.SystemStatus is ReconciliationStatus.ReviewRequired or ReconciliationStatus.DuplicatePossible)
                ? ReconciliationStatus.ReviewRequired
                : branchItems.Any(item => item.SystemStatus == ReconciliationStatus.Variance)
                    ? ReconciliationStatus.Variance
                    : ReconciliationStatus.Matched;

            return new BranchSummary(
                branchId,
                period,
                runId,
                new Litres(supplierLitres),
                new Litres(branchLitres),
                new Litres(billedLitres),
                new Litres(Math.Max(0, unbilledLitres)),
                new MoneyAmount(0m),
                branchItems.Count(item => item.SystemStatus is not ReconciliationStatus.Matched),
                status);
        }).ToArray();
    }

    private static Guid DeterministicGuid(params string[] parts)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", parts)));
        return new Guid(bytes[..16]);
    }

    private static string DeterministicHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record CarsMatchResult(
        ReconciliationStatus Status,
        ConfidenceBucket ConfidenceBucket,
        IReadOnlyList<string> ReasonCodes,
        IReadOnlyList<CarsBillingEntry> CarsCandidates,
        string HumanReadableReason,
        decimal? LitresVariance = null)
    {
        public IReadOnlyList<Guid> CarsIds => CarsCandidates.Select(candidate => candidate.Id).ToArray();
    }
}
