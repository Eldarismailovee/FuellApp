using System.Collections.ObjectModel;

namespace FuelRecon.Domain;

/// <summary>
/// Immutable record of one reconciliation run and the source/settings snapshots used to create it.
/// </summary>
public sealed record ReconciliationRun
{
    public ReconciliationRun(
        Guid id,
        FuelPeriod period,
        DateTimeOffset createdAtUtc,
        string createdBy,
        IEnumerable<FileChecksum> inputFileChecksums,
        string? settingsSnapshotId = null,
        ReconciliationRunStatus status = ReconciliationRunStatus.Created,
        DateTimeOffset? completedAtUtc = null,
        DateTimeOffset? failedAtUtc = null,
        string? failureReasonCode = null,
        int totalItemCount = 0,
        int matchedItemCount = 0,
        int reviewRequiredCount = 0,
        MoneyAmount? estimatedRecoveryTotal = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Reconciliation run ID cannot be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(createdBy))
        {
            throw new ArgumentException("Created by cannot be empty.", nameof(createdBy));
        }

        if (totalItemCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalItemCount), totalItemCount, "Total item count cannot be negative.");
        }

        if (matchedItemCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(matchedItemCount), matchedItemCount, "Matched item count cannot be negative.");
        }

        if (reviewRequiredCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reviewRequiredCount), reviewRequiredCount, "Review required count cannot be negative.");
        }

        Id = id;
        Period = period;
        CreatedAtUtc = createdAtUtc;
        CreatedBy = createdBy.Trim();
        InputFileChecksums = ReconciliationModelHelpers.ToReadOnlyList(inputFileChecksums, nameof(inputFileChecksums));
        SettingsSnapshotId = ReconciliationModelHelpers.TrimToNull(settingsSnapshotId);
        Status = status;
        CompletedAtUtc = completedAtUtc;
        FailedAtUtc = failedAtUtc;
        FailureReasonCode = ReconciliationModelHelpers.TrimToNull(failureReasonCode);
        TotalItemCount = totalItemCount;
        MatchedItemCount = matchedItemCount;
        ReviewRequiredCount = reviewRequiredCount;
        EstimatedRecoveryTotal = estimatedRecoveryTotal;
    }

    

    public Guid Id { get; }

    public FuelPeriod Period { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public string CreatedBy { get; }

    public IReadOnlyList<FileChecksum> InputFileChecksums { get; }

    public string? SettingsSnapshotId { get; }

    public ReconciliationRunStatus Status { get; }

    public DateTimeOffset? CompletedAtUtc { get; }

    public DateTimeOffset? FailedAtUtc { get; }

    public string? FailureReasonCode { get; }

    public int TotalItemCount { get; }

    public int MatchedItemCount { get; }

    public int ReviewRequiredCount { get; }

    public MoneyAmount? EstimatedRecoveryTotal { get; }

    public bool IsCompleted => CompletedAtUtc is not null;

    public bool IsFailed => FailedAtUtc is not null;
}

/// <summary>
/// Candidate source record considered for a reconciliation item.
/// </summary>
public sealed record MatchCandidate
{
    public MatchCandidate(
        Guid id,
        MatchCandidateType candidateType,
        Guid sourceId,
        ConfidenceBucket confidenceBucket,
        IEnumerable<string>? matchedFields = null,
        IEnumerable<string>? missingFields = null,
        IEnumerable<string>? conflictingFields = null,
        SourceReference? sourceReference = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Match candidate ID cannot be empty.", nameof(id));
        }

        if (sourceId == Guid.Empty)
        {
            throw new ArgumentException("Match candidate source ID cannot be empty.", nameof(sourceId));
        }

        Id = id;
        CandidateType = candidateType;
        SourceId = sourceId;
        ConfidenceBucket = confidenceBucket;
        MatchedFields = ReconciliationModelHelpers.NormaliseStrings(matchedFields);
        MissingFields = ReconciliationModelHelpers.NormaliseStrings(missingFields);
        ConflictingFields = ReconciliationModelHelpers.NormaliseStrings(conflictingFields);
        SourceReference = sourceReference;
    }

    public Guid Id { get; }

    public MatchCandidateType CandidateType { get; }

    public Guid SourceId { get; }

    public ConfidenceBucket ConfidenceBucket { get; }

    public IReadOnlyList<string> MatchedFields { get; }

    public IReadOnlyList<string> MissingFields { get; }

    public IReadOnlyList<string> ConflictingFields { get; }

    public SourceReference? SourceReference { get; }
}

/// <summary>
/// Group of candidates retained for review or deterministic evidence.
/// </summary>
public sealed record MatchGroup
{
    public MatchGroup(
        Guid id,
        IEnumerable<MatchCandidate> candidates,
        ConfidenceBucket confidenceBucket,
        IEnumerable<string>? reasonCodes = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Match group ID cannot be empty.", nameof(id));
        }

        Id = id;
        Candidates = ReconciliationModelHelpers.ToReadOnlyList(candidates, nameof(candidates));
        ConfidenceBucket = confidenceBucket;
        ReasonCodes = ReconciliationModelHelpers.NormaliseStrings(reasonCodes);
    }

    public Guid Id { get; }

    public IReadOnlyList<MatchCandidate> Candidates { get; }

    public ConfidenceBucket ConfidenceBucket { get; }

    public IReadOnlyList<string> ReasonCodes { get; }
}

/// <summary>
/// Reconciliation output item preserving system status, resolution state, source references and candidates.
/// </summary>
public sealed record ReconciliationItem
{
    public ReconciliationItem(
        Guid id,
        Guid runId,
        FuelPeriod period,
        ReconciliationStatus systemStatus,
        ResolutionStatus resolutionStatus,
        ConfidenceBucket confidenceBucket,
        IEnumerable<string> reasonCodes,
        CanonicalBranchId? branchId = null,
        ReconciliationStatus? finalStatus = null,
        string? humanReadableReason = null,
        Guid? supplierTransactionId = null,
        Guid? branchLitresEntryId = null,
        Guid? carsBillingEntryId = null,
        SourceReference? supplierSourceReference = null,
        SourceReference? branchSourceReference = null,
        SourceReference? carsSourceReference = null,
        IEnumerable<MatchCandidate>? matchCandidates = null,
        decimal? litresVariance = null,
        MoneyAmount? amountVariance = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Reconciliation item ID cannot be empty.", nameof(id));
        }

        if (runId == Guid.Empty)
        {
            throw new ArgumentException("Reconciliation item run ID cannot be empty.", nameof(runId));
        }

        Id = id;
        RunId = runId;
        Period = period;
        BranchId = branchId;
        SystemStatus = systemStatus;
        ResolutionStatus = resolutionStatus;
        FinalStatus = finalStatus;
        ConfidenceBucket = confidenceBucket;
        ReasonCodes = ReconciliationModelHelpers.NormaliseStrings(reasonCodes, nameof(reasonCodes));
        HumanReadableReason = ReconciliationModelHelpers.TrimToNull(humanReadableReason);
        SupplierTransactionId = EmptyGuidToNull(supplierTransactionId);
        BranchLitresEntryId = EmptyGuidToNull(branchLitresEntryId);
        CarsBillingEntryId = EmptyGuidToNull(carsBillingEntryId);
        SupplierSourceReference = supplierSourceReference;
        BranchSourceReference = branchSourceReference;
        CarsSourceReference = carsSourceReference;
        MatchCandidates = ReconciliationModelHelpers.ToReadOnlyList(matchCandidates ?? [], nameof(matchCandidates));
        LitresVariance = litresVariance;
        AmountVariance = amountVariance;
    }

    public Guid Id { get; }

    public Guid RunId { get; }

    public FuelPeriod Period { get; }

    public CanonicalBranchId? BranchId { get; }

    public ReconciliationStatus SystemStatus { get; }

    public ResolutionStatus ResolutionStatus { get; }

    public ReconciliationStatus? FinalStatus { get; }

    public ConfidenceBucket ConfidenceBucket { get; }

    public IReadOnlyList<string> ReasonCodes { get; }

    public string? HumanReadableReason { get; }

    public Guid? SupplierTransactionId { get; }

    public Guid? BranchLitresEntryId { get; }

    public Guid? CarsBillingEntryId { get; }

    public SourceReference? SupplierSourceReference { get; }

    public SourceReference? BranchSourceReference { get; }

    public SourceReference? CarsSourceReference { get; }

    public IReadOnlyList<MatchCandidate> MatchCandidates { get; }

    public decimal? LitresVariance { get; }

    public MoneyAmount? AmountVariance { get; }

    private static Guid? EmptyGuidToNull(Guid? value) => value == Guid.Empty ? null : value;
}

/// <summary>
/// Branch-level totals for a specific run and period.
/// </summary>
public sealed record BranchSummary
{
    public BranchSummary(
        CanonicalBranchId branchId,
        FuelPeriod period,
        Guid runId,
        Litres supplierLitres,
        Litres branchLitres,
        Litres billedLitres,
        Litres unbilledLitres,
        MoneyAmount estimatedRecovery,
        int reviewCount,
        ReconciliationStatus status)
    {
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("Branch summary run ID cannot be empty.", nameof(runId));
        }

        if (reviewCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reviewCount), reviewCount, "Review count cannot be negative.");
        }

        BranchId = branchId;
        Period = period;
        RunId = runId;
        SupplierLitres = supplierLitres;
        BranchLitres = branchLitres;
        BilledLitres = billedLitres;
        UnbilledLitres = unbilledLitres;
        EstimatedRecovery = estimatedRecovery;
        ReviewCount = reviewCount;
        Status = status;
    }

    public CanonicalBranchId BranchId { get; }

    public FuelPeriod Period { get; }

    public Guid RunId { get; }

    public Litres SupplierLitres { get; }

    public Litres BranchLitres { get; }

    public Litres BilledLitres { get; }

    public Litres UnbilledLitres { get; }

    public MoneyAmount EstimatedRecovery { get; }

    public int ReviewCount { get; }

    public ReconciliationStatus Status { get; }
}

/// <summary>
/// Manual user action against a reconciliation item, preserving the status change and required note.
/// </summary>
public sealed record ManualAction
{
    public ManualAction(
        Guid id,
        Guid runId,
        Guid reconciliationItemId,
        AuditActionType actionType,
        DateTimeOffset createdAtUtc,
        string createdBy,
        string note,
        ResolutionStatus? oldResolutionStatus = null,
        ResolutionStatus? newResolutionStatus = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Manual action ID cannot be empty.", nameof(id));
        }

        if (runId == Guid.Empty)
        {
            throw new ArgumentException("Manual action run ID cannot be empty.", nameof(runId));
        }

        if (reconciliationItemId == Guid.Empty)
        {
            throw new ArgumentException("Manual action item ID cannot be empty.", nameof(reconciliationItemId));
        }

        if (string.IsNullOrWhiteSpace(createdBy))
        {
            throw new ArgumentException("Created by cannot be empty.", nameof(createdBy));
        }

        if (string.IsNullOrWhiteSpace(note))
        {
            throw new ArgumentException("Manual action note cannot be empty.", nameof(note));
        }

        Id = id;
        RunId = runId;
        ReconciliationItemId = reconciliationItemId;
        ActionType = actionType;
        CreatedAtUtc = createdAtUtc;
        CreatedBy = createdBy.Trim();
        Note = note.Trim();
        OldResolutionStatus = oldResolutionStatus;
        NewResolutionStatus = newResolutionStatus;
    }

    public Guid Id { get; }

    public Guid RunId { get; }

    public Guid ReconciliationItemId { get; }

    public AuditActionType ActionType { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public string CreatedBy { get; }

    public string Note { get; }

    public ResolutionStatus? OldResolutionStatus { get; }

    public ResolutionStatus? NewResolutionStatus { get; }
}

/// <summary>
/// Immutable version of a branch report generated from a reconciliation run.
/// </summary>
public sealed record BranchReportVersion
{
    public BranchReportVersion(
        Guid id,
        Guid runId,
        CanonicalBranchId branchId,
        FuelPeriod period,
        int versionNumber,
        DateTimeOffset createdAtUtc,
        string createdBy,
        PeriodLifecycleStatus status,
        string? notes = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Branch report version ID cannot be empty.", nameof(id));
        }

        if (runId == Guid.Empty)
        {
            throw new ArgumentException("Branch report version run ID cannot be empty.", nameof(runId));
        }

        if (versionNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(versionNumber), versionNumber, "Version number must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(createdBy))
        {
            throw new ArgumentException("Created by cannot be empty.", nameof(createdBy));
        }

        Id = id;
        RunId = runId;
        BranchId = branchId;
        Period = period;
        VersionNumber = versionNumber;
        CreatedAtUtc = createdAtUtc;
        CreatedBy = createdBy.Trim();
        Status = status;
        Notes = ReconciliationModelHelpers.TrimToNull(notes);
    }

    public Guid Id { get; }

    public Guid RunId { get; }

    public CanonicalBranchId BranchId { get; }

    public FuelPeriod Period { get; }

    public int VersionNumber { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public string CreatedBy { get; }

    public PeriodLifecycleStatus Status { get; }

    public string? Notes { get; }
}

/// <summary>
/// Immutable record of a single branch report PDF export attempt.
/// </summary>
public sealed record PdfExportRecord
{
    public PdfExportRecord(
        Guid id,
        Guid branchReportVersionId,
        DateTimeOffset exportedAtUtc,
        string exportedBy,
        PdfExportStatus status,
        string? filePath = null,
        string? templateName = null,
        string? templateVersion = null,
        string? errorCategory = null,
        CorrelationId? correlationId = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("PDF export record ID cannot be empty.", nameof(id));
        }

        if (branchReportVersionId == Guid.Empty)
        {
            throw new ArgumentException("PDF export branch report version ID cannot be empty.", nameof(branchReportVersionId));
        }

        if (string.IsNullOrWhiteSpace(exportedBy))
        {
            throw new ArgumentException("Exported by cannot be empty.", nameof(exportedBy));
        }

        Id = id;
        BranchReportVersionId = branchReportVersionId;
        ExportedAtUtc = exportedAtUtc;
        ExportedBy = exportedBy.Trim();
        Status = status;
        FilePath = ReconciliationModelHelpers.TrimToNull(filePath);
        TemplateName = ReconciliationModelHelpers.TrimToNull(templateName);
        TemplateVersion = ReconciliationModelHelpers.TrimToNull(templateVersion);
        ErrorCategory = ReconciliationModelHelpers.TrimToNull(errorCategory);
        CorrelationId = correlationId;
    }

    public Guid Id { get; }

    public Guid BranchReportVersionId { get; }

    public DateTimeOffset ExportedAtUtc { get; }

    public string ExportedBy { get; }

    public PdfExportStatus Status { get; }

    public string? FilePath { get; }

    public string? TemplateName { get; }

    public string? TemplateVersion { get; }

    public string? ErrorCategory { get; }

    public CorrelationId? CorrelationId { get; }
}

internal static class ReconciliationModelHelpers
{
    internal static string? TrimToNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    internal static IReadOnlyList<string> NormaliseStrings(IEnumerable<string>? values, string? parameterName = null)
    {
        if (values is null)
        {
            if (parameterName is null)
            {
                return Array.Empty<string>();
            }

            throw new ArgumentNullException(parameterName);
        }

        var normalisedValues = values
            .Select(TrimToNull)
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();

        return Array.AsReadOnly(normalisedValues);
    }

    internal static IReadOnlyList<T> ToReadOnlyList<T>(IEnumerable<T> values, string parameterName)
    {
        if (values is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        return Array.AsReadOnly(values.ToArray());
    }
}
