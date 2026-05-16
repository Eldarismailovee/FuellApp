using FuelRecon.Application.BranchReports;
using FuelRecon.Application.Pdf.Export;
using FuelRecon.Application.Pdf.Templates;
using FuelRecon.Application.Persistence;
using FuelRecon.Domain;
using FuelRecon.Infrastructure.Pdf;
using FuelRecon.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using System.Text;
using UglyToad.PdfPig;

namespace FuelRecon.Tests;

public class SqliteReconciliationRepositoriesTests
{
    [Fact]
    public void SettingsSnapshotRepository_saves_and_retrieves_snapshot()
    {
        using var database = RepositoryTestDatabase.Create();
        var repository = new SqliteSettingsSnapshotRepository(database.ConnectionFactory);
        var snapshot = CreateSettingsSnapshot();

        repository.Save(snapshot);

        var loaded = repository.GetById(snapshot.Id);

        Assert.NotNull(loaded);
        Assert.Equal(snapshot.Id, loaded.Id);
        Assert.Equal(snapshot.CreatedAtUtc, loaded.CreatedAtUtc);
        Assert.Equal(snapshot.CreatedBy, loaded.CreatedBy);
        Assert.Equal(snapshot.SnapshotJson, loaded.SnapshotJson);
        Assert.Equal(snapshot.Description, loaded.Description);
    }

    [Fact]
    public void ReconciliationRunRepository_saves_and_retrieves_run_and_latest_for_period()
    {
        using var database = RepositoryTestDatabase.Create();
        var snapshot = CreateSettingsSnapshot();
        new SqliteSettingsSnapshotRepository(database.ConnectionFactory).Save(snapshot);
        var repository = new SqliteReconciliationRunRepository(database.ConnectionFactory);
        var run = CreateRun(snapshot.Id);

        repository.Save(run);

        var loaded = repository.GetById(run.Id);
        var latest = repository.GetLatestForPeriod(run.Period);

        Assert.NotNull(loaded);
        Assert.Equal(run.Id, loaded.Id);
        Assert.Equal(run.Period, loaded.Period);
        Assert.Equal(run.CreatedAtUtc, loaded.CreatedAtUtc);
        Assert.Equal(run.CreatedBy, loaded.CreatedBy);
        Assert.Equal(run.SettingsSnapshotId, loaded.SettingsSnapshotId);
        Assert.Equal(ReconciliationRunStatus.Completed, loaded.Status);
        Assert.Equal(run.CompletedAtUtc, loaded.CompletedAtUtc);
        Assert.Equal(2, loaded.InputFileChecksums.Count);
        Assert.Equal("supplier", loaded.InputFileChecksums[0].Value);
        Assert.Equal(10, loaded.TotalItemCount);
        Assert.Equal(7, loaded.MatchedItemCount);
        Assert.Equal(3, loaded.ReviewRequiredCount);
        Assert.Equal(123.46m, loaded.EstimatedRecoveryTotal?.Value);
        Assert.NotNull(latest);
        Assert.Equal(run.Id, latest.Id);
    }

    [Fact]
    public void ReconciliationItemRepository_saves_and_retrieves_items_by_run()
    {
        using var database = RepositoryTestDatabase.Create();
        var run = SaveRun(database);
        var repository = new SqliteReconciliationItemRepository(database.ConnectionFactory);
        var item = CreateItem(run);

        repository.Save(item);

        var loaded = repository.GetById(item.Id);
        var items = repository.ListByRun(run.Id);

        Assert.NotNull(loaded);
        Assert.Equal(item.Id, loaded.Id);
        Assert.Equal(run.Id, loaded.RunId);
        Assert.Equal("TAUPO", loaded.BranchId?.Value);
        Assert.Equal(ReconciliationStatus.MissingRA, loaded.SystemStatus);
        Assert.Equal(ResolutionStatus.Unresolved, loaded.ResolutionStatus);
        Assert.Equal(ReconciliationStatus.ReviewRequired, loaded.FinalStatus);
        Assert.Equal(["MissingRA", "LowConfidenceMatch"], loaded.ReasonCodes);
        Assert.Equal("Needs review", loaded.HumanReadableReason);
        Assert.Equal(1.25m, loaded.LitresVariance);
        Assert.Equal(10.01m, loaded.AmountVariance?.Value);
        Assert.Equal("supplier.pdf", loaded.SupplierSourceReference?.SourceFile);
        Assert.Equal(3, loaded.SupplierSourceReference?.PageNumber);
        Assert.Single(loaded.MatchCandidates);
        Assert.Equal(MatchCandidateType.CarsBillingEntry, loaded.MatchCandidates[0].CandidateType);
        Assert.Equal(["RA"], loaded.MatchCandidates[0].MissingFields);
        Assert.Single(items);
    }

    [Fact]
    public void ReconciliationItemRepository_rolls_back_multi_row_save_when_one_insert_fails()
    {
        using var database = RepositoryTestDatabase.Create();
        var run = SaveRun(database);
        var repository = new SqliteReconciliationItemRepository(database.ConnectionFactory);
        var id = Guid.Parse("b7fd074b-90f2-4b68-8000-000000000011");
        var first = CreateItem(run, id);
        var duplicate = CreateItem(run, id);

        Assert.Throws<SqliteException>(() => repository.SaveMany([first, duplicate]));

        Assert.Empty(repository.ListByRun(run.Id));
    }

    [Fact]
    public void BranchReportRepository_saves_and_retrieves_reports_by_run_and_branch()
    {
        using var database = RepositoryTestDatabase.Create();
        var run = SaveRun(database);
        var repository = new SqliteBranchReportRepository(database.ConnectionFactory);
        var report = CreateReport(run);
        var summary = new BranchSummary(
            report.BranchId,
            report.Period,
            report.RunId,
            new Litres(100m),
            new Litres(95m),
            new Litres(90m),
            new Litres(5m),
            new MoneyAmount(12.345m),
            2,
            ReconciliationStatus.ReviewRequired);

        repository.Save(report, summary);

        var loaded = repository.GetById(report.Id);
        var reports = repository.ListByRunAndBranch(run.Id, report.BranchId);

        Assert.NotNull(loaded);
        Assert.Equal(report.Id, loaded.Id);
        Assert.Equal(run.Id, loaded.RunId);
        Assert.Equal("TAUPO", loaded.BranchId.Value);
        Assert.Equal(1, loaded.VersionNumber);
        Assert.Equal(PeriodLifecycleStatus.Reviewed, loaded.Status);
        Assert.Equal("Ready", loaded.Notes);
        Assert.Single(reports);
    }

    [Fact]
    public void BranchReportRepository_sqlite_rejects_duplicate_version_number_for_same_run_and_branch()
    {
        using var database = RepositoryTestDatabase.Create();
        var run = SaveRun(database);
        var repository = new SqliteBranchReportRepository(database.ConnectionFactory);
        var summary = CreateTaupoBranchSummary(run);
        var first = CreateReport(run);
        repository.Save(first, summary);

        var duplicateVersionNumber = new BranchReportVersion(
            Guid.Parse("c7fd074b-90f2-4b68-8000-000000000099"),
            run.Id,
            new CanonicalBranchId("TAUPO"),
            run.Period,
            versionNumber: 1,
            new DateTimeOffset(2026, 5, 15, 9, 0, 0, TimeSpan.Zero),
            "arina",
            PeriodLifecycleStatus.Draft);

        var exception = Assert.Throws<SqliteException>(() => repository.Save(duplicateVersionNumber, summary));
        Assert.Equal(19, exception.SqliteErrorCode);
    }

    [Fact]
    public void CreateBranchReportVersionUseCase_sqlite_appends_monotonic_versions_for_same_run_and_branch()
    {
        using var database = RepositoryTestDatabase.Create();
        var run = SaveRun(database);
        var runsRepo = new SqliteReconciliationRunRepository(database.ConnectionFactory);
        var branchRepo = new SqliteBranchReportRepository(database.ConnectionFactory);
        var createUseCase = new CreateBranchReportVersionUseCase(runsRepo, branchRepo);
        var listUseCase = new ListBranchReportVersionsUseCase(branchRepo);
        var summary = CreateTaupoBranchSummary(run);

        var first = createUseCase.Execute(
            new CreateBranchReportVersionRequest(
                run.Id,
                new CanonicalBranchId("TAUPO"),
                summary,
                CreatedBy: "user-a",
                CreatedAtUtc: new DateTimeOffset(2026, 5, 16, 9, 0, 0, TimeSpan.Zero)));

        var second = createUseCase.Execute(
            new CreateBranchReportVersionRequest(
                run.Id,
                new CanonicalBranchId("TAUPO"),
                summary,
                CreatedBy: "user-b",
                CreatedAtUtc: new DateTimeOffset(2026, 5, 16, 10, 0, 0, TimeSpan.Zero),
                InitialLifecycleStatus: PeriodLifecycleStatus.Reconciled));

        Assert.Equal(1, first.Version.VersionNumber);
        Assert.Equal(PeriodLifecycleStatus.Draft, first.Version.Status);
        Assert.Equal(2, second.Version.VersionNumber);
        Assert.Equal(PeriodLifecycleStatus.Reconciled, second.Version.Status);

        var versions = listUseCase.Execute(new ListBranchReportVersionsRequest(run.Id, new CanonicalBranchId("TAUPO")));
        Assert.Equal(2, versions.Count);
        Assert.Equal(first.Version.Id, versions[0].Id);
        Assert.Equal(second.Version.Id, versions[1].Id);
    }

    [Fact]
    public void PdfExportRepository_saves_and_retrieves_exports_by_branch_report()
    {
        using var database = RepositoryTestDatabase.Create();
        var run = SaveRun(database);
        var report = SaveReport(database, run);
        var repository = new SqlitePdfExportRepository(database.ConnectionFactory);
        var export = new PdfExportRecord(
            Guid.Parse("b7fd074b-90f2-4b68-8000-000000000030"),
            report.Id,
            new DateTimeOffset(2026, 5, 15, 5, 0, 0, TimeSpan.Zero),
            "arina",
            PdfExportStatus.Failed,
            templateName: "Branch",
            templateVersion: "v1",
            errorCategory: "TemplateNotFound",
            correlationId: new CorrelationId("corr-1"));

        repository.Save(export);

        var loaded = repository.GetById(export.Id);
        var exports = repository.ListByBranchReport(report.Id);

        Assert.NotNull(loaded);
        Assert.Equal(export.Id, loaded.Id);
        Assert.Equal(report.Id, loaded.BranchReportVersionId);
        Assert.Equal(PdfExportStatus.Failed, loaded.Status);
        Assert.Equal("Branch", loaded.TemplateName);
        Assert.Equal("v1", loaded.TemplateVersion);
        Assert.Equal("TemplateNotFound", loaded.ErrorCategory);
        Assert.Equal("corr-1", loaded.CorrelationId?.Value);
        Assert.Single(exports);
    }

    [Fact]
    public void PdfExportRepository_round_trips_error_message_and_export_settings_snapshot()
    {
        using var database = RepositoryTestDatabase.Create();
        var run = SaveRun(database);
        var report = SaveReport(database, run);
        var repository = new SqlitePdfExportRepository(database.ConnectionFactory);
        var export = new PdfExportRecord(
            Guid.Parse("c7fd074b-90f2-4b68-8000-000000000031"),
            report.Id,
            new DateTimeOffset(2026, 5, 15, 6, 0, 0, TimeSpan.Zero),
            "arina",
            PdfExportStatus.Failed,
            errorCategory: "PdfWriteFailed",
            errorMessage: " Disk full ",
            exportSettingsSnapshot: " {\"schemaVersion\":\"1\",\"failureStage\":\"PdfWriteFailed\"} ");

        repository.Save(export);

        var loaded = repository.GetById(export.Id);

        Assert.NotNull(loaded);
        Assert.Equal("PdfWriteFailed", loaded.ErrorCategory);
        Assert.Equal("Disk full", loaded.ErrorMessage);
        Assert.Equal("{\"schemaVersion\":\"1\",\"failureStage\":\"PdfWriteFailed\"}", loaded.ExportSettingsSnapshot);
    }

    [Fact]
    public void PdfExportRepository_lists_exports_newest_first_for_branch_report()
    {
        using var database = RepositoryTestDatabase.Create();
        var run = SaveRun(database);
        var report = SaveReport(database, run);
        var repository = new SqlitePdfExportRepository(database.ConnectionFactory);
        var older = new PdfExportRecord(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            report.Id,
            new DateTimeOffset(2026, 5, 10, 10, 0, 0, TimeSpan.Zero),
            "u1",
            PdfExportStatus.Failed,
            errorCategory: "Old");

        var newer = new PdfExportRecord(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            report.Id,
            new DateTimeOffset(2026, 5, 11, 10, 0, 0, TimeSpan.Zero),
            "u2",
            PdfExportStatus.Succeeded,
            filePath: "/tmp/report.pdf");

        repository.Save(older);
        repository.Save(newer);

        var exports = repository.ListByBranchReport(report.Id);

        Assert.Equal(2, exports.Count);
        Assert.Equal(newer.Id, exports[0].Id);
        Assert.Equal(older.Id, exports[1].Id);
    }

    [Fact]
    public void AuditRepository_saves_and_retrieves_audit_records_by_entity()
    {
        using var database = RepositoryTestDatabase.Create();
        var repository = new SqliteAuditRepository(database.ConnectionFactory);
        var audit = new AuditRecord(
            Guid.Parse("b7fd074b-90f2-4b68-8000-000000000040"),
            new DateTimeOffset(2026, 5, 15, 6, 0, 0, TimeSpan.Zero),
            "arina",
            AuditActionType.Reconcile,
            AuditEntityType.ReconciliationRun,
            "run-1",
            "UnitTest",
            OldValuesJson: "{}",
            NewValuesJson: "{\"status\":\"Completed\"}",
            Note: "Created run",
            ReasonCode: "ReconciliationCompleted",
            CorrelationId: new CorrelationId("audit-corr"),
            ContextJson: "{\"branch\":\"TAUPO\"}");

        repository.Save(audit);

        var loaded = repository.GetById(audit.Id);
        var audits = repository.ListByEntity(AuditEntityType.ReconciliationRun, "run-1");

        Assert.NotNull(loaded);
        Assert.Equal(audit.Id, loaded.Id);
        Assert.Equal(AuditActionType.Reconcile, loaded.ActionType);
        Assert.Equal(AuditEntityType.ReconciliationRun, loaded.EntityType);
        Assert.Equal("run-1", loaded.EntityId);
        Assert.Equal("UnitTest", loaded.Origin);
        Assert.Equal("{}", loaded.OldValuesJson);
        Assert.Equal("{\"status\":\"Completed\"}", loaded.NewValuesJson);
        Assert.Equal("Created run", loaded.Note);
        Assert.Equal("ReconciliationCompleted", loaded.ReasonCode);
        Assert.Equal("audit-corr", loaded.CorrelationId?.Value);
        Assert.Equal("{\"branch\":\"TAUPO\"}", loaded.ContextJson);
        Assert.Single(audits);
    }

    [Fact]
    public void BranchReportRepository_GetPersistedMetrics_returns_inserted_totals()
    {
        using var database = RepositoryTestDatabase.Create();
        var run = SaveRun(database);
        var summary = CreateTaupoBranchSummary(run);
        var repository = new SqliteBranchReportRepository(database.ConnectionFactory);
        var report = CreateReport(run);
        repository.Save(report, summary);

        var metrics = repository.GetPersistedMetrics(report.Id);

        Assert.NotNull(metrics);
        Assert.Equal(summary.ReviewCount, metrics.ReviewCount);
        Assert.Equal(PeriodLifecycleStatus.Reviewed, metrics.LifecycleStatus);
        Assert.Equal(summary.SupplierLitres.Value, metrics.SupplierLitres.Value);
        Assert.Equal(summary.BranchLitres.Value, metrics.BranchLitres.Value);
        Assert.Equal(summary.EstimatedRecovery.Value, metrics.EstimatedRecovery.Value);
    }

    [Fact]
    public void BranchReportNoteRepository_saves_and_orders_by_created_time()
    {
        using var database = RepositoryTestDatabase.Create();
        var run = SaveRun(database);
        var report = SaveReport(database, run);
        var repository = new SqliteBranchReportNoteRepository(database.ConnectionFactory);
        var later = new BranchReportNote(
            Guid.Parse("c7fd074b-90f2-4b68-8000-000000000081"),
            report.Id,
            new DateTimeOffset(2026, 5, 15, 18, 0, 0, TimeSpan.Zero),
            "b",
            "second");
        var earlier = new BranchReportNote(
            Guid.Parse("c7fd074b-90f2-4b68-8000-000000000080"),
            report.Id,
            new DateTimeOffset(2026, 5, 15, 17, 0, 0, TimeSpan.Zero),
            "a",
            "first");

        repository.Save(later);
        repository.Save(earlier);

        var notes = repository.ListByBranchReport(report.Id);

        Assert.Equal(2, notes.Count);
        Assert.Equal("first", notes[0].NoteText);
        Assert.Equal("second", notes[1].NoteText);
    }

    [Fact]
    public void BranchReportApprovalRepository_round_trips_snapshot_and_optional_note()
    {
        using var database = RepositoryTestDatabase.Create();
        var run = SaveRun(database);
        var report = SaveReport(database, run);
        var repository = new SqliteBranchReportApprovalRepository(database.ConnectionFactory);
        var approval = new BranchReportApprovalRecord(
            Guid.Parse("c7fd074b-90f2-4b68-8000-000000000082"),
            report.Id,
            run.Id,
            new DateTimeOffset(2026, 5, 15, 19, 0, 0, TimeSpan.Zero),
            "mgr",
            "{\"branch\":\"TAUPO\"}",
            "Looks good");

        repository.Save(approval);

        var loaded = repository.FindByBranchReport(report.Id);

        Assert.NotNull(loaded);
        Assert.Equal(approval.Id, loaded.Id);
        Assert.Equal("{\"branch\":\"TAUPO\"}", loaded.SnapshotJson);
        Assert.Equal("Looks good", loaded.ApprovalNote);
    }

    [Fact]
    public void BranchReportApprovalRepository_rejects_second_row_for_same_branch_report()
    {
        using var database = RepositoryTestDatabase.Create();
        var run = SaveRun(database);
        var report = SaveReport(database, run);
        var repository = new SqliteBranchReportApprovalRepository(database.ConnectionFactory);
        var first = new BranchReportApprovalRecord(
            Guid.Parse("f7fd074b-90f2-4b68-8000-000000000070"),
            report.Id,
            run.Id,
            new DateTimeOffset(2026, 5, 15, 16, 0, 0, TimeSpan.Zero),
            "arina",
            "{}",
            null);
        repository.Save(first);

        var duplicate = new BranchReportApprovalRecord(
            Guid.Parse("f7fd074b-90f2-4b68-8000-000000000071"),
            report.Id,
            run.Id,
            new DateTimeOffset(2026, 5, 15, 16, 1, 0, TimeSpan.Zero),
            "arina",
            "{\"dup\":true}",
            "again");

        var exception = Assert.Throws<SqliteException>(() => repository.Save(duplicate));
        Assert.Equal(19, exception.SqliteErrorCode);
    }

    [Fact]
    public void ApproveBranchReportVersionUseCase_sqlite_requires_note_when_unresolved_items_exist()
    {
        using var database = RepositoryTestDatabase.Create();
        var run = SaveRun(database);
        var summary = CreateTaupoBranchSummary(run);
        var report = SaveReport(database, run, summary);
        var itemsRepo = new SqliteReconciliationItemRepository(database.ConnectionFactory);
        itemsRepo.Save(CreateItem(run));

        var branchRepo = new SqliteBranchReportRepository(database.ConnectionFactory);
        var approvalsRepo = new SqliteBranchReportApprovalRepository(database.ConnectionFactory);
        var auditsRepo = new SqliteAuditRepository(database.ConnectionFactory);
        var useCase = new ApproveBranchReportVersionUseCase(branchRepo, approvalsRepo, itemsRepo, auditsRepo);

        var missingNote = Assert.Throws<ArgumentException>(() =>
            useCase.Execute(
                new ApproveBranchReportVersionRequest(
                    report.Id,
                    ApprovedBy: "mgr",
                    ApprovedAtUtc: new DateTimeOffset(2026, 5, 15, 17, 0, 0, TimeSpan.Zero),
                    ApprovalNote: null)));
        Assert.Contains(
            BranchReportAuditReasonCodes.ApprovalNoteRequiredForUnresolvedItems,
            missingNote.Message,
            StringComparison.Ordinal);

        var response = useCase.Execute(
            new ApproveBranchReportVersionRequest(
                report.Id,
                ApprovedBy: "mgr",
                ApprovedAtUtc: new DateTimeOffset(2026, 5, 15, 17, 1, 0, TimeSpan.Zero),
                ApprovalNote: "Proceeding with exceptions."));

        Assert.NotNull(approvalsRepo.FindByBranchReport(report.Id));
        Assert.Contains("\"ReviewCount\":2", response.Approval.SnapshotJson, StringComparison.Ordinal);

        var audits = auditsRepo
            .ListByEntity(AuditEntityType.BranchReport, report.Id.ToString("D"))
            .Where(auditRecord => auditRecord.ActionType == AuditActionType.Approve)
            .ToArray();
        Assert.Single(audits);
        Assert.Equal(BranchReportAuditReasonCodes.Approved, audits[0].ReasonCode);
    }

    [Fact]
    public void ExportBranchReportPdfUseCase_sqlite_writes_pdf_and_export_history_row()
    {
        using var database = RepositoryTestDatabase.Create();
        var run = SaveRun(database);
        var summary = CreateTaupoBranchSummary(run);
        var report = SaveReport(database, run, summary);
        var itemsRepo = new SqliteReconciliationItemRepository(database.ConnectionFactory);
        itemsRepo.Save(CreateItem(run));

        var approvalsRepo = new SqliteBranchReportApprovalRepository(database.ConnectionFactory);
        approvalsRepo.Save(
            new BranchReportApprovalRecord(
                Guid.Parse("a7fd074b-90f2-4b68-8000-000000000091"),
                report.Id,
                run.Id,
                new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero),
                "approver",
                "{}",
                "Looks consistent."));

        var branchRepo = new SqliteBranchReportRepository(database.ConnectionFactory);
        var runsRepo = new SqliteReconciliationRunRepository(database.ConnectionFactory);
        var exportRepo = new SqlitePdfExportRepository(database.ConnectionFactory);

        var tempDir = Path.Combine(AppContext.BaseDirectory, $"FuelRecon-pdf-smoke-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var useCase = new ExportBranchReportPdfUseCase(
                InMemoryPdfTemplateService.CreateDefault(),
                branchRepo,
                runsRepo,
                itemsRepo,
                approvalsRepo,
                exportRepo,
                new BranchReportService(),
                new PdfSharpBranchReportPdfRenderer());

            var exportedAt = new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.Zero);
            var response = useCase.Execute(new ExportBranchReportPdfRequest(report.Id, "export-user", exportedAt, tempDir));

            Assert.Equal(PdfExportStatus.Succeeded, response.Status);
            Assert.NotNull(response.FilePath);
            Assert.True(File.Exists(response.FilePath));

            var pdfBytes = File.ReadAllBytes(response.FilePath);
            Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdfBytes.AsSpan(0, 5)));

            Assert.Contains(report.Id.ToString("D"), response.FilePath, StringComparison.OrdinalIgnoreCase);

            using var pdf = PdfDocument.Open(new MemoryStream(pdfBytes));
            Assert.Equal("Fuel reconciliation branch report", pdf.Information.Title);

            var text = string.Join(' ', pdf.GetPages().SelectMany(page => page.GetWords()).Select(word => word.Text));
            var compactPdfText = CompactWhitespace(text);

            Assert.Contains("TAUPO", text, StringComparison.Ordinal);
            Assert.Contains(run.Id.ToString("D"), text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("100.00", text, StringComparison.Ordinal);

            Assert.Contains("Branch:TAUPO", compactPdfText, StringComparison.Ordinal);
            Assert.Contains("Period:2026-04", compactPdfText, StringComparison.Ordinal);
            Assert.Contains("Reportversion:1", compactPdfText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                $"PDFtemplate:{PdfTemplateDefaults.ActiveBranchReportTemplateKey}(v{PdfTemplateDefaults.BranchReportTemplateVersion})",
                compactPdfText,
                StringComparison.OrdinalIgnoreCase);

            Assert.Contains("2026-05-15T03:00:00", compactPdfText, StringComparison.Ordinal);
            Assert.Contains("Preparedby:arina", compactPdfText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("2026-05-20T08:00:00", compactPdfText, StringComparison.Ordinal);
            Assert.Contains("Approvedby:approver", compactPdfText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Approvalnote:Looksconsistent.", compactPdfText, StringComparison.OrdinalIgnoreCase);

            var exports = exportRepo.ListByBranchReport(report.Id);
            Assert.Single(exports);
            Assert.Equal(PdfExportStatus.Succeeded, exports[0].Status);
            Assert.Equal(PdfTemplateDefaults.ActiveBranchReportTemplateKey, exports[0].TemplateName);
            Assert.Equal(PdfTemplateDefaults.BranchReportTemplateVersion, exports[0].TemplateVersion);
            Assert.Equal(response.ExportId, exports[0].Id);
            Assert.NotNull(exports[0].ExportSettingsSnapshot);
            Assert.Contains("\"exportOutcome\":\"Succeeded\"", exports[0].ExportSettingsSnapshot, StringComparison.Ordinal);
            Assert.Contains(
                PdfTemplateDefaults.ActiveBranchReportTemplateKey,
                exports[0].ExportSettingsSnapshot,
                StringComparison.Ordinal);

            var history = new ListBranchReportPdfExportsUseCase(exportRepo).Execute(new ListBranchReportPdfExportsRequest(report.Id));
            Assert.Single(history.Exports);
            Assert.Equal(exports[0].Id, history.Exports[0].ExportId);
            Assert.Equal(exports[0].ExportSettingsSnapshot, history.Exports[0].ExportSettingsSnapshotJson);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ExportBranchReportPdfUseCase_sqlite_records_TemplateNotFound_without_pdf_file()
    {
        using var database = RepositoryTestDatabase.Create();
        var run = SaveRun(database);
        var summary = CreateTaupoBranchSummary(run);
        var report = SaveReport(database, run, summary);

        var branchRepo = new SqliteBranchReportRepository(database.ConnectionFactory);
        var runsRepo = new SqliteReconciliationRunRepository(database.ConnectionFactory);
        var itemsRepo = new SqliteReconciliationItemRepository(database.ConnectionFactory);
        var approvalsRepo = new SqliteBranchReportApprovalRepository(database.ConnectionFactory);
        var exportRepo = new SqlitePdfExportRepository(database.ConnectionFactory);

        var missingCatalogTemplateService = new InMemoryPdfTemplateService(
            PdfTemplateDefaults.ActiveBranchReportTemplateKey,
            new Dictionary<string, PdfTemplateConfiguration>(StringComparer.Ordinal));

        var tempDir = Path.Combine(AppContext.BaseDirectory, $"FuelRecon-pdf-missing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var useCase = new ExportBranchReportPdfUseCase(
                missingCatalogTemplateService,
                branchRepo,
                runsRepo,
                itemsRepo,
                approvalsRepo,
                exportRepo,
                new BranchReportService(),
                new PdfSharpBranchReportPdfRenderer());

            var response = useCase.Execute(
                new ExportBranchReportPdfRequest(
                    report.Id,
                    "export-user",
                    new DateTimeOffset(2026, 5, 20, 11, 0, 0, TimeSpan.Zero),
                    tempDir));

            Assert.Equal(PdfExportStatus.Failed, response.Status);
            Assert.Equal(PdfTemplateErrorCategories.TemplateNotFound, response.ErrorCategory);
            Assert.Null(response.FilePath);
            Assert.Empty(Directory.GetFiles(tempDir));

            var exports = exportRepo.ListByBranchReport(report.Id);
            Assert.Single(exports);
            Assert.Equal(PdfExportStatus.Failed, exports[0].Status);
            Assert.Equal(PdfTemplateErrorCategories.TemplateNotFound, exports[0].ErrorCategory);
            Assert.Null(exports[0].TemplateName);
            Assert.Equal("Active PDF template configuration was not found.", exports[0].ErrorMessage);
            Assert.NotNull(exports[0].ExportSettingsSnapshot);
            Assert.Contains("\"failureStage\":\"TemplateNotFound\"", exports[0].ExportSettingsSnapshot, StringComparison.Ordinal);
            Assert.Contains($"\"branchReportVersionId\":\"{report.Id:D}\"", exports[0].ExportSettingsSnapshot, StringComparison.Ordinal);
            Assert.Contains("\"schemaVersion\":\"1\"", exports[0].ExportSettingsSnapshot, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Theory]
    [InlineData("ReconciliationRuns")]
    [InlineData("BranchReports")]
    [InlineData("PdfExports")]
    [InlineData("AuditRecords")]
    [InlineData("BranchReportNotes")]
    [InlineData("BranchReportApprovals")]
    public void Append_only_triggers_prevent_update_and_delete_for_protected_tables(string tableName)
    {
        using var database = RepositoryTestDatabase.Create();
        var run = SaveRun(database);
        var report = SaveReport(database, run);
        var export = SaveExport(database, report);
        var audit = SaveAudit(database, run.Id.ToString());

        var id = tableName switch
        {
            "ReconciliationRuns" => run.Id.ToString(),
            "BranchReports" => report.Id.ToString(),
            "PdfExports" => export.Id.ToString(),
            "AuditRecords" => audit.Id.ToString(),
            "BranchReportNotes" => SaveBranchReportNote(database, report).Id.ToString(),
            "BranchReportApprovals" => SaveBranchReportApproval(database, run, report).Id.ToString(),
            _ => throw new InvalidOperationException("Unexpected table name."),
        };

        using var connection = database.ConnectionFactory.OpenConnection();

        using var updateCommand = connection.CreateCommand();
        updateCommand.CommandText = $"UPDATE {tableName} SET Id = Id WHERE Id = $id;";
        updateCommand.Parameters.AddWithValue("$id", id);
        var updateException = Assert.Throws<SqliteException>(() => updateCommand.ExecuteNonQuery());
        Assert.Equal(19, updateException.SqliteErrorCode);

        using var deleteCommand = connection.CreateCommand();
        deleteCommand.CommandText = $"DELETE FROM {tableName} WHERE Id = $id;";
        deleteCommand.Parameters.AddWithValue("$id", id);
        var deleteException = Assert.Throws<SqliteException>(() => deleteCommand.ExecuteNonQuery());
        Assert.Equal(19, deleteException.SqliteErrorCode);
    }

    private static ReconciliationRun SaveRun(RepositoryTestDatabase database)
    {
        var snapshot = CreateSettingsSnapshot();
        new SqliteSettingsSnapshotRepository(database.ConnectionFactory).Save(snapshot);
        var run = CreateRun(snapshot.Id);
        new SqliteReconciliationRunRepository(database.ConnectionFactory).Save(run);
        return run;
    }

    private static BranchReportVersion SaveReport(RepositoryTestDatabase database, ReconciliationRun run, BranchSummary? summary = null)
    {
        var report = CreateReport(run);
        new SqliteBranchReportRepository(database.ConnectionFactory).Save(report, summary);
        return report;
    }

    private static PdfExportRecord SaveExport(RepositoryTestDatabase database, BranchReportVersion report)
    {
        var export = new PdfExportRecord(
            Guid.Parse("b7fd074b-90f2-4b68-8000-000000000050"),
            report.Id,
            new DateTimeOffset(2026, 5, 15, 7, 0, 0, TimeSpan.Zero),
            "arina",
            PdfExportStatus.Succeeded,
            filePath: "/tmp/report.pdf");
        new SqlitePdfExportRepository(database.ConnectionFactory).Save(export);
        return export;
    }

    private static AuditRecord SaveAudit(RepositoryTestDatabase database, string entityId)
    {
        var audit = new AuditRecord(
            Guid.Parse("b7fd074b-90f2-4b68-8000-000000000051"),
            new DateTimeOffset(2026, 5, 15, 8, 0, 0, TimeSpan.Zero),
            "arina",
            AuditActionType.Create,
            AuditEntityType.ReconciliationRun,
            entityId,
            "UnitTest");
        new SqliteAuditRepository(database.ConnectionFactory).Save(audit);
        return audit;
    }

    private static BranchReportNote SaveBranchReportNote(RepositoryTestDatabase database, BranchReportVersion report)
    {
        var note = new BranchReportNote(
            Guid.Parse("d7fd074b-90f2-4b68-8000-000000000060"),
            report.Id,
            new DateTimeOffset(2026, 5, 15, 11, 0, 0, TimeSpan.Zero),
            "arina",
            "append-only probe");
        new SqliteBranchReportNoteRepository(database.ConnectionFactory).Save(note);
        return note;
    }

    private static BranchReportApprovalRecord SaveBranchReportApproval(
        RepositoryTestDatabase database,
        ReconciliationRun run,
        BranchReportVersion report)
    {
        var approval = new BranchReportApprovalRecord(
            Guid.Parse("e7fd074b-90f2-4b68-8000-000000000061"),
            report.Id,
            run.Id,
            new DateTimeOffset(2026, 5, 15, 11, 1, 0, TimeSpan.Zero),
            "arina",
            "{}",
            null);
        new SqliteBranchReportApprovalRepository(database.ConnectionFactory).Save(approval);
        return approval;
    }

    private static SettingsSnapshotRecord CreateSettingsSnapshot() =>
        new(
            "settings-v1",
            new DateTimeOffset(2026, 5, 15, 1, 0, 0, TimeSpan.Zero),
            "arina",
            "{\"litreTolerance\":0.5}",
            "Initial settings");

    private static ReconciliationRun CreateRun(string settingsSnapshotId) =>
        new(
            Guid.Parse("b7fd074b-90f2-4b68-8000-000000000001"),
            new FuelPeriod(2026, 4),
            new DateTimeOffset(2026, 5, 15, 2, 0, 0, TimeSpan.Zero),
            "arina",
            [new FileChecksum("SHA256", "supplier"), new FileChecksum("SHA256", "branch")],
            settingsSnapshotId,
            ReconciliationRunStatus.Completed,
            completedAtUtc: new DateTimeOffset(2026, 5, 15, 2, 5, 0, TimeSpan.Zero),
            totalItemCount: 10,
            matchedItemCount: 7,
            reviewRequiredCount: 3,
            estimatedRecoveryTotal: new MoneyAmount(123.455m));

    private static ReconciliationItem CreateItem(ReconciliationRun run) =>
        CreateItem(run, Guid.Parse("b7fd074b-90f2-4b68-8000-000000000010"));

    private static ReconciliationItem CreateItem(ReconciliationRun run, Guid itemId)
    {
        var candidate = new MatchCandidate(
            Guid.Parse("b7fd074b-90f2-4b68-8000-000000000012"),
            MatchCandidateType.CarsBillingEntry,
            Guid.Parse("b7fd074b-90f2-4b68-8000-000000000013"),
            ConfidenceBucket.Low,
            matchedFields: ["Rego"],
            missingFields: ["RA"],
            conflictingFields: ["Litres"],
            sourceReference: new SourceReference("cars.xlsx", sheetName: "Export", rowNumber: 20));

        return new ReconciliationItem(
            itemId,
            run.Id,
            run.Period,
            ReconciliationStatus.MissingRA,
            ResolutionStatus.Unresolved,
            ConfidenceBucket.Low,
            ["MissingRA", "LowConfidenceMatch"],
            new CanonicalBranchId("TAUPO"),
            ReconciliationStatus.ReviewRequired,
            "Needs review",
            supplierSourceReference: new SourceReference("supplier.pdf", pageNumber: 3),
            branchSourceReference: new SourceReference("branch.xlsx", sheetName: "April", rowNumber: 4),
            carsSourceReference: new SourceReference("cars.xlsx", sheetName: "Export", rowNumber: 5),
            matchCandidates: [candidate],
            litresVariance: 1.25m,
            amountVariance: new MoneyAmount(10.005m));
    }

    private static BranchSummary CreateTaupoBranchSummary(ReconciliationRun run) =>
        new(
            new CanonicalBranchId("TAUPO"),
            run.Period,
            run.Id,
            new Litres(100m),
            new Litres(95m),
            new Litres(90m),
            new Litres(5m),
            new MoneyAmount(12.345m),
            reviewCount: 2,
            status: ReconciliationStatus.ReviewRequired);

    private static BranchReportVersion CreateReport(ReconciliationRun run) =>
        new(
            Guid.Parse("b7fd074b-90f2-4b68-8000-000000000020"),
            run.Id,
            new CanonicalBranchId("TAUPO"),
            run.Period,
            1,
            new DateTimeOffset(2026, 5, 15, 3, 0, 0, TimeSpan.Zero),
            "arina",
            PeriodLifecycleStatus.Reviewed,
            "Ready");

    private sealed class RepositoryTestDatabase : IDisposable
    {
        private readonly SqliteConnection rootConnection;

        private RepositoryTestDatabase(SqliteConnectionFactory connectionFactory, SqliteConnection rootConnection)
        {
            ConnectionFactory = connectionFactory;
            this.rootConnection = rootConnection;
        }

        public SqliteConnectionFactory ConnectionFactory { get; }

        public static RepositoryTestDatabase Create()
        {
            var connectionString = $"Data Source=ReconRepoTests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var factory = new SqliteConnectionFactory(connectionString, isConnectionString: true);
            var rootConnection = factory.OpenConnection();
            SqliteSchemaMigrator.ApplyInitialSchema(rootConnection);
            return new RepositoryTestDatabase(factory, rootConnection);
        }

        public void Dispose() => rootConnection.Dispose();
    }

    private static string CompactWhitespace(string value) =>
        string.Concat(value.Where(c => !char.IsWhiteSpace(c)));

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures on shared runners.
        }
    }
}
