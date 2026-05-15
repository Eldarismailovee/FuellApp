using FuelRecon.Application.Persistence;
using FuelRecon.Domain;
using FuelRecon.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

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
        Assert.Equal("Completed", loaded.Status);
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
        Assert.Equal(ResolutionStatus.Open, loaded.ResolutionStatus);
        Assert.Equal(ReconciliationStatus.ReviewRequired, loaded.FinalStatus);
        Assert.Equal(["MissingRA", "LowConfidenceMatch"], loaded.ReasonCodes);
        Assert.Equal("Needs review", loaded.HumanReadableReason);
        Assert.Equal(1.25m, loaded.LitresVariance);
        Assert.Equal(10.01m, loaded.AmountVariance?.Value);
        Assert.Equal("supplier.pdf", loaded.SupplierSourceReference?.SourceFile);
        Assert.Equal(3, loaded.SupplierSourceReference?.PageNumber);
        Assert.Single(loaded.MatchCandidates);
        Assert.Equal("CarsBillingEntry", loaded.MatchCandidates[0].CandidateType);
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

    [Theory]
    [InlineData("ReconciliationRuns")]
    [InlineData("BranchReports")]
    [InlineData("PdfExports")]
    [InlineData("AuditRecords")]
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

    private static BranchReportVersion SaveReport(RepositoryTestDatabase database, ReconciliationRun run)
    {
        var report = CreateReport(run);
        new SqliteBranchReportRepository(database.ConnectionFactory).Save(report);
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
            "Completed",
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
            "CarsBillingEntry",
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
            ResolutionStatus.Open,
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
}
