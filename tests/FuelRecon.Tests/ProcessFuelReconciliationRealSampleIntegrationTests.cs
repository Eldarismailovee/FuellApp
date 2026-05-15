using FuelRecon.Application.Processing;
using FuelRecon.Application.Reconciliation;
using FuelRecon.Application.Validation;
using FuelRecon.Domain;
using FuelRecon.Infrastructure.Excel;
using FuelRecon.Infrastructure.Pdf;
using FuelRecon.Infrastructure.Persistence;

namespace FuelRecon.Tests;

public class ProcessFuelReconciliationRealSampleIntegrationTests
{
    [Fact]
    public void ProcessFuelReconciliation_runs_real_pipeline_with_samples_and_temporary_sqlite_when_available()
    {
        var samples = LocateSamples();
        if (samples is null)
        {
            return;
        }

        using var database = TemporarySqliteDatabase.Create();
        var useCase = CreateUseCase(database.ConnectionFactory);
        var aliasResolver = CreateBranchAliasResolver();

        var result = useCase.Execute(new ProcessFuelReconciliationRequest(
            new FuelPeriod(2026, 4),
            samples.SupplierPdfPath,
            samples.BranchLitresPath,
            samples.CarsBillingPath,
            aliasResolver,
            ImportedBy: "integration-test",
            RunBy: "integration-test",
            FileChecksums: new Dictionary<InputSlot, FileChecksum>
            {
                [InputSlot.SupplierStatement] = new("TEST", "supplier"),
                [InputSlot.BranchLitres] = new("TEST", "branch"),
                [InputSlot.CarsBilling] = new("TEST", "cars"),
            },
            ReconciliationRules: new ReconciliationRulesOptions(RunCreatedAtUtc: new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero)),
            ImportedAtUtc: new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero)));

        Assert.NotNull(result.ImportValidationResult);
        Assert.NotEqual(Guid.Empty, result.ImportPersistenceResponse.ImportBatchId);

        var importBatchRepository = new SqliteImportBatchRepository(database.ConnectionFactory);
        var importedFileRepository = new SqliteImportedFileRepository(database.ConnectionFactory);
        var runRepository = new SqliteReconciliationRunRepository(database.ConnectionFactory);

        var importedBatch = importBatchRepository.GetById(result.ImportPersistenceResponse.ImportBatchId);
        var importedFiles = importedFileRepository.ListByImportBatch(result.ImportPersistenceResponse.ImportBatchId);

        Assert.NotNull(importedBatch);
        Assert.Equal(3, importedFiles.Count);

        Assert.True(result.ImportValidationResult.Success);
        Assert.NotEmpty(result.ImportValidationResult.SupplierTransactions);
        Assert.NotEmpty(result.ImportValidationResult.BranchLitresEntries);
        Assert.NotEmpty(result.ImportValidationResult.CarsBillingEntries);

        Assert.NotNull(result.ReconciliationEngineResult);
        Assert.NotNull(result.ReconciliationPersistenceResponse);
        Assert.NotEqual(Guid.Empty, result.ReconciliationPersistenceResponse.RunId);

        var persistedRun = runRepository.GetById(result.ReconciliationPersistenceResponse.RunId);
        Assert.NotNull(persistedRun);
        Assert.Equal(result.ReconciliationEngineResult.Run.Id, persistedRun.Id);
    }

    private static ProcessFuelReconciliationUseCase CreateUseCase(SqliteConnectionFactory connectionFactory)
    {
        var workbookReader = new ClosedXmlExcelWorkbookReader();
        var supplierParser = new SupplierPdfParser(new PdfPigDocumentReader());
        var branchParser = new BranchLitresExcelParser(workbookReader);
        var carsParser = new CarsBillingExcelParser(workbookReader);
        var validate = new ValidateImportBatchUseCase(supplierParser, branchParser, carsParser);
        var persistImport = new PersistImportValidationResultUseCase(
            new SqliteImportBatchRepository(connectionFactory),
            new SqliteImportedFileRepository(connectionFactory),
            new SqliteSupplierTransactionRepository(connectionFactory),
            new SqliteBranchLitresRepository(connectionFactory),
            new SqliteCarsBillingRepository(connectionFactory));
        var engine = new DeterministicReconciliationEngine();
        var persistReconciliation = new PersistReconciliationResultUseCase(
            new SqliteReconciliationRunRepository(connectionFactory),
            new SqliteReconciliationItemRepository(connectionFactory));

        return new ProcessFuelReconciliationUseCase(validate, persistImport, engine, persistReconciliation);
    }

    private static SamplePaths? LocateSamples()
    {
        var sampleRoot = Path.Combine("samples", "client-raw");
        var supplierPdf = FirstExisting(
            Path.Combine(sampleRoot, "Farmlands Statement April.PDF"),
            Path.Combine(sampleRoot, "Mobile - Taupo.pdf"));
        var branchLitres = Path.Combine(sampleRoot, "branch litres.xlsx");
        var carsBilling = Path.Combine(sampleRoot, "cars+ statement.xlsx");

        if (supplierPdf is null || !File.Exists(branchLitres) || !File.Exists(carsBilling))
        {
            return null;
        }

        return new SamplePaths(supplierPdf, branchLitres, carsBilling);
    }

    private static string? FirstExisting(params string[] paths) =>
        paths.FirstOrDefault(File.Exists);

    private static BranchAliasResolver CreateBranchAliasResolver()
    {
        var taupo = new BranchMaster(new CanonicalBranchId("TAUPO"), "Taupo");
        var kerikeri = new BranchMaster(new CanonicalBranchId("KERIKERI"), "Kerikeri");
        var whangarei = new BranchMaster(new CanonicalBranchId("WHANGAREI"), "Whangarei");

        return new BranchAliasResolver(
            [taupo, kerikeri, whangarei],
            [
                new BranchAlias("Taupo", taupo.Id),
                new BranchAlias("Mobil Taupo", taupo.Id),
                new BranchAlias("Mobile - Taupo", taupo.Id),
                new BranchAlias("Hertz Taupo", taupo.Id),
                new BranchAlias("Caltex Kerikeri", kerikeri.Id),
                new BranchAlias("Caltex Whangarei", whangarei.Id),
            ]);
    }

    private sealed record SamplePaths(string SupplierPdfPath, string BranchLitresPath, string CarsBillingPath);

    private sealed class TemporarySqliteDatabase : IDisposable
    {
        private readonly string databasePath;

        private TemporarySqliteDatabase(string databasePath)
        {
            this.databasePath = databasePath;
            ConnectionFactory = new SqliteConnectionFactory(databasePath);
            SqliteSchemaMigrator.ApplyInitialSchema(ConnectionFactory);
        }

        public SqliteConnectionFactory ConnectionFactory { get; }

        public static TemporarySqliteDatabase Create()
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"fuelrecon-e2e-{Guid.NewGuid():N}.db");
            return new TemporarySqliteDatabase(databasePath);
        }

        public void Dispose()
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }
}
