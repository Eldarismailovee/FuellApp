using FuelRecon.Domain;

namespace FuelRecon.Tests;

public class ReconciliationResultModelsTests
{
    [Fact]
    public void ReconciliationRun_constructs_with_checksums_and_settings_snapshot()
    {
        var checksums = new[]
        {
            new FileChecksum("sha256", "supplier-checksum"),
            new FileChecksum("sha256", "branch-checksum"),
            new FileChecksum("sha256", "cars-checksum"),
        };

        var run = new ReconciliationRun(
            Guid.Parse("74a98d17-1352-4a69-b6d8-000000000001"),
            new FuelPeriod(2026, 4),
            new DateTimeOffset(2026, 5, 1, 8, 30, 0, TimeSpan.Zero),
            " arina ",
            checksums,
            settingsSnapshotId: " settings-v1 ",
            status: " Completed ",
            completedAtUtc: new DateTimeOffset(2026, 5, 1, 8, 35, 0, TimeSpan.Zero),
            totalItemCount: 10,
            matchedItemCount: 7,
            reviewRequiredCount: 3,
            estimatedRecoveryTotal: new MoneyAmount(123.456m));

        Assert.Equal(new FuelPeriod(2026, 4), run.Period);
        Assert.Equal("arina", run.CreatedBy);
        Assert.Equal(3, run.InputFileChecksums.Count);
        Assert.Equal("SHA256", run.InputFileChecksums[0].Algorithm);
        Assert.Equal("supplier-checksum", run.InputFileChecksums[0].Value);
        Assert.Equal("settings-v1", run.SettingsSnapshotId);
        Assert.Equal("Completed", run.Status);
        Assert.True(run.IsCompleted);
        Assert.False(run.IsFailed);
        Assert.Equal(10, run.TotalItemCount);
        Assert.Equal(7, run.MatchedItemCount);
        Assert.Equal(3, run.ReviewRequiredCount);
        Assert.Equal(123.46m, run.EstimatedRecoveryTotal?.Value);
    }

    [Fact]
    public void ReconciliationRun_copies_checksum_collection()
    {
        var checksums = new[] { new FileChecksum("sha256", "original") };

        var run = new ReconciliationRun(
            Guid.Parse("74a98d17-1352-4a69-b6d8-000000000002"),
            new FuelPeriod(2026, 5),
            DateTimeOffset.UnixEpoch,
            "arina",
            checksums);

        checksums[0] = new FileChecksum("sha256", "changed");

        Assert.Equal("original", run.InputFileChecksums[0].Value);
        Assert.Equal("Created", run.Status);
        Assert.Null(run.SettingsSnapshotId);
    }

    [Fact]
    public void MatchCandidate_constructs_with_field_lists_and_source_reference()
    {
        var sourceReference = new SourceReference("branch.xlsx", sheetName: "April", rowNumber: 10);

        var candidate = new MatchCandidate(
            Guid.Parse("74a98d17-1352-4a69-b6d8-000000000003"),
            " BranchLitresEntry ",
            Guid.Parse("74a98d17-1352-4a69-b6d8-000000000004"),
            ConfidenceBucket.High,
            matchedFields: [" BranchId ", "Date", ""],
            missingFields: ["RA"],
            conflictingFields: [" Litres "],
            sourceReference: sourceReference);

        Assert.Equal("BranchLitresEntry", candidate.CandidateType);
        Assert.Equal(ConfidenceBucket.High, candidate.ConfidenceBucket);
        Assert.Equal(["BranchId", "Date"], candidate.MatchedFields);
        Assert.Equal(["RA"], candidate.MissingFields);
        Assert.Equal(["Litres"], candidate.ConflictingFields);
        Assert.Same(sourceReference, candidate.SourceReference);
    }

    [Fact]
    public void MatchGroup_constructs_with_candidates_and_reason_codes()
    {
        var firstCandidate = new MatchCandidate(
            Guid.Parse("74a98d17-1352-4a69-b6d8-000000000005"),
            "SupplierTransaction",
            Guid.Parse("74a98d17-1352-4a69-b6d8-000000000006"),
            ConfidenceBucket.Medium);

        var secondCandidate = new MatchCandidate(
            Guid.Parse("74a98d17-1352-4a69-b6d8-000000000007"),
            "CarsBillingEntry",
            Guid.Parse("74a98d17-1352-4a69-b6d8-000000000008"),
            ConfidenceBucket.Low);

        var group = new MatchGroup(
            Guid.Parse("74a98d17-1352-4a69-b6d8-000000000009"),
            [firstCandidate, secondCandidate],
            ConfidenceBucket.Medium,
            [" MultipleCarsCandidates ", ""]);

        Assert.Equal(2, group.Candidates.Count);
        Assert.Same(firstCandidate, group.Candidates[0]);
        Assert.Same(secondCandidate, group.Candidates[1]);
        Assert.Equal(ConfidenceBucket.Medium, group.ConfidenceBucket);
        Assert.Equal(["MultipleCarsCandidates"], group.ReasonCodes);
    }

    [Fact]
    public void ReconciliationItem_preserves_system_status_separately_from_resolution_status()
    {
        var runId = Guid.Parse("74a98d17-1352-4a69-b6d8-000000000010");
        var supplierReference = new SourceReference("supplier.pdf", pageNumber: 4);
        var branchReference = new SourceReference("branch.xlsx", sheetName: "April", rowNumber: 20);
        var carsReference = new SourceReference("cars.xlsx", sheetName: "Export", rowNumber: 100);
        var candidate = new MatchCandidate(
            Guid.Parse("74a98d17-1352-4a69-b6d8-000000000011"),
            "CarsBillingEntry",
            Guid.Parse("74a98d17-1352-4a69-b6d8-000000000012"),
            ConfidenceBucket.Low);

        var item = new ReconciliationItem(
            Guid.Parse("74a98d17-1352-4a69-b6d8-000000000013"),
            runId,
            new FuelPeriod(2026, 4),
            ReconciliationStatus.MissingRA,
            ResolutionStatus.Resolved,
            ConfidenceBucket.Low,
            [" MissingRA ", "LowConfidenceMatch"],
            branchId: new CanonicalBranchId("TAUPO"),
            finalStatus: ReconciliationStatus.Matched,
            humanReadableReason: " Manually linked to Cars+ entry ",
            supplierTransactionId: Guid.Parse("74a98d17-1352-4a69-b6d8-000000000014"),
            branchLitresEntryId: Guid.Parse("74a98d17-1352-4a69-b6d8-000000000015"),
            carsBillingEntryId: Guid.Parse("74a98d17-1352-4a69-b6d8-000000000016"),
            supplierSourceReference: supplierReference,
            branchSourceReference: branchReference,
            carsSourceReference: carsReference,
            matchCandidates: [candidate],
            litresVariance: -1.25m,
            amountVariance: new MoneyAmount(10.005m));

        Assert.Equal(runId, item.RunId);
        Assert.Equal(ReconciliationStatus.MissingRA, item.SystemStatus);
        Assert.Equal(ResolutionStatus.Resolved, item.ResolutionStatus);
        Assert.Equal(ReconciliationStatus.Matched, item.FinalStatus);
        Assert.Equal(ConfidenceBucket.Low, item.ConfidenceBucket);
        Assert.Equal(["MissingRA", "LowConfidenceMatch"], item.ReasonCodes);
        Assert.Equal("Manually linked to Cars+ entry", item.HumanReadableReason);
        Assert.Equal("TAUPO", item.BranchId?.Value);
        Assert.Same(supplierReference, item.SupplierSourceReference);
        Assert.Same(branchReference, item.BranchSourceReference);
        Assert.Same(carsReference, item.CarsSourceReference);
        Assert.Single(item.MatchCandidates);
        Assert.Same(candidate, item.MatchCandidates[0]);
        Assert.Equal(-1.25m, item.LitresVariance);
        Assert.Equal(10.01m, item.AmountVariance?.Value);
    }

    [Fact]
    public void ReconciliationItem_allows_optional_source_ids_and_references_to_be_absent()
    {
        var item = new ReconciliationItem(
            Guid.Parse("74a98d17-1352-4a69-b6d8-000000000017"),
            Guid.Parse("74a98d17-1352-4a69-b6d8-000000000018"),
            new FuelPeriod(2026, 5),
            ReconciliationStatus.SupplierOnly,
            ResolutionStatus.Open,
            ConfidenceBucket.Unknown,
            ["SupplierOnly"]);

        Assert.Null(item.BranchId);
        Assert.Null(item.FinalStatus);
        Assert.Null(item.HumanReadableReason);
        Assert.Null(item.SupplierTransactionId);
        Assert.Null(item.BranchLitresEntryId);
        Assert.Null(item.CarsBillingEntryId);
        Assert.Null(item.SupplierSourceReference);
        Assert.Null(item.BranchSourceReference);
        Assert.Null(item.CarsSourceReference);
        Assert.Empty(item.MatchCandidates);
    }

    [Fact]
    public void BranchSummary_constructs_with_branch_totals()
    {
        var summary = new BranchSummary(
            new CanonicalBranchId("ROTORUA"),
            new FuelPeriod(2026, 4),
            Guid.Parse("74a98d17-1352-4a69-b6d8-000000000019"),
            new Litres(100.111m),
            new Litres(95.222m),
            new Litres(90.333m),
            new Litres(4.444m),
            new MoneyAmount(12.345m),
            reviewCount: 2,
            ReconciliationStatus.ReviewRequired);

        Assert.Equal("ROTORUA", summary.BranchId.Value);
        Assert.Equal(new FuelPeriod(2026, 4), summary.Period);
        Assert.Equal(100.11m, summary.SupplierLitres.Value);
        Assert.Equal(95.22m, summary.BranchLitres.Value);
        Assert.Equal(90.33m, summary.BilledLitres.Value);
        Assert.Equal(4.44m, summary.UnbilledLitres.Value);
        Assert.Equal(12.35m, summary.EstimatedRecovery.Value);
        Assert.Equal(2, summary.ReviewCount);
        Assert.Equal(ReconciliationStatus.ReviewRequired, summary.Status);
    }

    [Fact]
    public void ManualAction_constructs_with_resolution_status_change_and_note()
    {
        var action = new ManualAction(
            Guid.Parse("74a98d17-1352-4a69-b6d8-000000000020"),
            Guid.Parse("74a98d17-1352-4a69-b6d8-000000000021"),
            Guid.Parse("74a98d17-1352-4a69-b6d8-000000000022"),
            AuditActionType.Resolve,
            new DateTimeOffset(2026, 5, 2, 9, 0, 0, TimeSpan.Zero),
            " arina ",
            " matched after review ",
            oldResolutionStatus: ResolutionStatus.Open,
            newResolutionStatus: ResolutionStatus.Resolved);

        Assert.Equal(AuditActionType.Resolve, action.ActionType);
        Assert.Equal("arina", action.CreatedBy);
        Assert.Equal("matched after review", action.Note);
        Assert.Equal(ResolutionStatus.Open, action.OldResolutionStatus);
        Assert.Equal(ResolutionStatus.Resolved, action.NewResolutionStatus);
    }

    [Fact]
    public void BranchReportVersion_constructs_with_version_and_status()
    {
        var report = new BranchReportVersion(
            Guid.Parse("74a98d17-1352-4a69-b6d8-000000000023"),
            Guid.Parse("74a98d17-1352-4a69-b6d8-000000000024"),
            new CanonicalBranchId("WELLINGTON"),
            new FuelPeriod(2026, 5),
            versionNumber: 2,
            new DateTimeOffset(2026, 5, 31, 10, 0, 0, TimeSpan.Zero),
            " reviewer ",
            PeriodLifecycleStatus.Reviewed,
            notes: " Ready for approval ");

        Assert.Equal("WELLINGTON", report.BranchId.Value);
        Assert.Equal(new FuelPeriod(2026, 5), report.Period);
        Assert.Equal(2, report.VersionNumber);
        Assert.Equal("reviewer", report.CreatedBy);
        Assert.Equal(PeriodLifecycleStatus.Reviewed, report.Status);
        Assert.Equal("Ready for approval", report.Notes);
    }

    [Fact]
    public void PdfExportRecord_can_represent_success()
    {
        var correlationId = new CorrelationId("export-123");

        var exportRecord = new PdfExportRecord(
            Guid.Parse("74a98d17-1352-4a69-b6d8-000000000025"),
            Guid.Parse("74a98d17-1352-4a69-b6d8-000000000026"),
            new DateTimeOffset(2026, 6, 1, 11, 0, 0, TimeSpan.Zero),
            " approver ",
            PdfExportStatus.Succeeded,
            filePath: " C:\\Reports\\branch.pdf ",
            templateName: " Branch Report ",
            templateVersion: " v1 ",
            correlationId: correlationId);

        Assert.Equal(PdfExportStatus.Succeeded, exportRecord.Status);
        Assert.Equal("approver", exportRecord.ExportedBy);
        Assert.Equal("C:\\Reports\\branch.pdf", exportRecord.FilePath);
        Assert.Equal("Branch Report", exportRecord.TemplateName);
        Assert.Equal("v1", exportRecord.TemplateVersion);
        Assert.Null(exportRecord.ErrorCategory);
        Assert.Equal(correlationId, exportRecord.CorrelationId);
    }

    [Fact]
    public void PdfExportRecord_can_represent_failure()
    {
        var exportRecord = new PdfExportRecord(
            Guid.Parse("74a98d17-1352-4a69-b6d8-000000000027"),
            Guid.Parse("74a98d17-1352-4a69-b6d8-000000000028"),
            new DateTimeOffset(2026, 6, 1, 11, 5, 0, TimeSpan.Zero),
            "approver",
            PdfExportStatus.Failed,
            errorCategory: " TemplateNotFound ",
            correlationId: new CorrelationId("export-failure-1"));

        Assert.Equal(PdfExportStatus.Failed, exportRecord.Status);
        Assert.Null(exportRecord.FilePath);
        Assert.Null(exportRecord.TemplateName);
        Assert.Null(exportRecord.TemplateVersion);
        Assert.Equal("TemplateNotFound", exportRecord.ErrorCategory);
        Assert.Equal("export-failure-1", exportRecord.CorrelationId?.Value);
    }

    [Fact]
    public void Constructors_reject_empty_required_ids_or_required_text()
    {
        Assert.Throws<ArgumentException>(() => new ReconciliationRun(
            Guid.Empty,
            new FuelPeriod(2026, 4),
            DateTimeOffset.UnixEpoch,
            "arina",
            []));

        Assert.Throws<ArgumentException>(() => new MatchCandidate(
            Guid.NewGuid(),
            " ",
            Guid.NewGuid(),
            ConfidenceBucket.Low));

        Assert.Throws<ArgumentException>(() => new ManualAction(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            AuditActionType.Resolve,
            DateTimeOffset.UnixEpoch,
            "arina",
            " "));

        Assert.Throws<ArgumentOutOfRangeException>(() => new BranchReportVersion(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new CanonicalBranchId("TAUPO"),
            new FuelPeriod(2026, 4),
            0,
            DateTimeOffset.UnixEpoch,
            "arina",
            PeriodLifecycleStatus.Reviewed));
    }
}
