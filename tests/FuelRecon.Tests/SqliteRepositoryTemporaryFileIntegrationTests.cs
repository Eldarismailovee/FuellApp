using FuelRecon.Application.Persistence;
using FuelRecon.Domain;
using FuelRecon.Infrastructure.Persistence;

namespace FuelRecon.Tests;

public class SqliteRepositoryTemporaryFileIntegrationTests
{
    [Fact]
    public void Import_and_source_row_repositories_roundtrip_using_temporary_sqlite_file()
    {
        using var database = TemporarySqliteDatabase.Create();
        var batchRepository = new SqliteImportBatchRepository(database.ConnectionFactory);
        var fileRepository = new SqliteImportedFileRepository(database.ConnectionFactory);
        var supplierRepository = new SqliteSupplierTransactionRepository(database.ConnectionFactory);
        var branchRepository = new SqliteBranchLitresRepository(database.ConnectionFactory);
        var carsRepository = new SqliteCarsBillingRepository(database.ConnectionFactory);

        var batch = CreateImportBatch();
        batchRepository.Save(batch);

        var supplierFile = CreateImportedFile(batch, InputSlot.SupplierStatement, "supplier.pdf");
        var branchFile = CreateImportedFile(batch, InputSlot.BranchLitres, "branch.xlsx");
        var carsFile = CreateImportedFile(batch, InputSlot.CarsBilling, "cars.xlsx");
        fileRepository.Save(supplierFile);
        fileRepository.Save(branchFile);
        fileRepository.Save(carsFile);

        var supplier = CreateSupplierTransaction();
        var branch = CreateBranchLitresEntry();
        var cars = CreateCarsBillingEntry();
        supplierRepository.Save(batch.Id, supplierFile.Id, supplier);
        branchRepository.Save(batch.Id, branchFile.Id, branch);
        carsRepository.Save(batch.Id, carsFile.Id, cars);

        var loadedBatch = batchRepository.GetById(batch.Id);
        var loadedFiles = fileRepository.ListByImportBatch(batch.Id);
        var loadedSupplier = supplierRepository.GetById(supplier.Id);
        var loadedBranch = branchRepository.GetById(branch.Id);
        var loadedCars = carsRepository.GetById(cars.Id);

        Assert.NotNull(loadedBatch);
        Assert.Equal(batch.Id, loadedBatch.Id);
        Assert.Equal(batch.Period, loadedBatch.Period);

        Assert.Equal(3, loadedFiles.Count);
        Assert.Contains(loadedFiles, file => file.Id == supplierFile.Id && file.InputSlot == InputSlot.SupplierStatement);
        Assert.Contains(loadedFiles, file => file.Id == branchFile.Id && file.InputSlot == InputSlot.BranchLitres);
        Assert.Contains(loadedFiles, file => file.Id == carsFile.Id && file.InputSlot == InputSlot.CarsBilling);

        Assert.NotNull(loadedSupplier);
        Assert.Equal(supplier.Id, loadedSupplier.Id);
        Assert.Equal("Mobil", loadedSupplier.SupplierName);
        Assert.Equal("TAUPO", loadedSupplier.BranchId?.Value);
        Assert.Equal(42.13m, loadedSupplier.Litres.Value);
        Assert.Equal("supplier.pdf", loadedSupplier.SourceReference.SourceFile);
        Assert.Equal(2, loadedSupplier.SourceReference.PageNumber);

        Assert.NotNull(loadedBranch);
        Assert.Equal(branch.Id, loadedBranch.Id);
        Assert.Equal("TAUPO", loadedBranch.BranchId.Value);
        Assert.Equal("RA123", loadedBranch.RentalAgreementNumber?.NormalisedValue);
        Assert.Equal("ABC123", loadedBranch.Rego?.NormalisedValue);
        Assert.Equal("branch.xlsx", loadedBranch.SourceReference.SourceFile);

        Assert.NotNull(loadedCars);
        Assert.Equal(cars.Id, loadedCars.Id);
        Assert.Equal("TAUPO", loadedCars.BranchId?.Value);
        Assert.Equal("RA123", loadedCars.RentalAgreementNumber?.NormalisedValue);
        Assert.Equal(42.13m, loadedCars.BilledLitres?.Value);
        Assert.Equal(123.46m, loadedCars.BilledAmount?.Value);
        Assert.Equal("cars.xlsx", loadedCars.SourceReference.SourceFile);
    }

    [Fact]
    public void Reconciliation_repositories_roundtrip_using_temporary_sqlite_file()
    {
        using var database = TemporarySqliteDatabase.Create();
        var runRepository = new SqliteReconciliationRunRepository(database.ConnectionFactory);
        var itemRepository = new SqliteReconciliationItemRepository(database.ConnectionFactory);

        var run = new ReconciliationRun(
            Guid.Parse("7f20e446-8001-4000-9000-000000000001"),
            Period,
            new DateTimeOffset(2026, 5, 15, 9, 0, 0, TimeSpan.Zero),
            "arina",
            [new FileChecksum("SHA256", "run-checksum")],
            status: ReconciliationRunStatus.Completed,
            completedAtUtc: new DateTimeOffset(2026, 5, 15, 9, 1, 0, TimeSpan.Zero),
            totalItemCount: 2,
            matchedItemCount: 1,
            reviewRequiredCount: 1);
        var firstItem = CreateReconciliationItem(run.Id, "7f20e446-8001-4000-9000-000000000101", ReconciliationStatus.Matched);
        var secondItem = CreateReconciliationItem(run.Id, "7f20e446-8001-4000-9000-000000000102", ReconciliationStatus.Unbilled);

        runRepository.Save(run);
        itemRepository.SaveMany([firstItem, secondItem]);

        var loadedRun = runRepository.GetById(run.Id);
        var latestRun = runRepository.GetLatestForPeriod(Period);
        var loadedItems = itemRepository.ListByRun(run.Id);
        var loadedFirstItem = itemRepository.GetById(firstItem.Id);

        Assert.NotNull(loadedRun);
        Assert.Equal(run.Id, loadedRun.Id);
        Assert.Equal(ReconciliationRunStatus.Completed, loadedRun.Status);
        Assert.Equal(2, loadedRun.TotalItemCount);
        Assert.Single(loadedRun.InputFileChecksums);
        Assert.Equal("run-checksum", loadedRun.InputFileChecksums[0].Value);

        Assert.NotNull(latestRun);
        Assert.Equal(run.Id, latestRun.Id);

        Assert.Equal(2, loadedItems.Count);
        Assert.Contains(loadedItems, item => item.Id == firstItem.Id && item.SystemStatus == ReconciliationStatus.Matched);
        Assert.Contains(loadedItems, item => item.Id == secondItem.Id && item.SystemStatus == ReconciliationStatus.Unbilled);

        Assert.NotNull(loadedFirstItem);
        Assert.Equal(firstItem.Id, loadedFirstItem.Id);
        Assert.Equal("TAUPO", loadedFirstItem.BranchId?.Value);
        Assert.Equal("branch.xlsx", loadedFirstItem.BranchSourceReference?.SourceFile);
        Assert.Equal(["MatchedByRA"], loadedFirstItem.ReasonCodes);
    }

    private static FuelPeriod Period => new(2026, 4);

    private static ImportBatchRecord CreateImportBatch() =>
        new(
            Guid.Parse("7f20e446-8001-4000-9000-000000000201"),
            Period,
            new DateTimeOffset(2026, 5, 15, 8, 0, 0, TimeSpan.Zero),
            "arina",
            "Created",
            "temporary file integration");

    private static ImportedFileRecord CreateImportedFile(ImportBatchRecord batch, InputSlot inputSlot, string fileName) =>
        new(
            Guid.NewGuid(),
            batch.Id,
            batch.Period,
            inputSlot,
            fileName,
            FileStatus.Parsed,
            new FileChecksum("SHA256", $"checksum-{inputSlot}"),
            new DateTimeOffset(2026, 5, 15, 8, 1, 0, TimeSpan.Zero),
            StoredFilePath: $"/imports/{fileName}",
            CompletedAtUtc: new DateTimeOffset(2026, 5, 15, 8, 2, 0, TimeSpan.Zero));

    private static SupplierTransaction CreateSupplierTransaction() =>
        new(
            Guid.Parse("7f20e446-8001-4000-9000-000000000301"),
            "Mobil",
            Period,
            new DateOnly(2026, 4, 1),
            new Litres(42.125m),
            new SourceReference("supplier.pdf", pageNumber: 2, referenceText: "line"),
            new CanonicalBranchId("TAUPO"),
            rawSiteText: "Mobil Taupo",
            voucherOrInvoiceReference: "INV-1",
            amount: new MoneyAmount(123.455m));

    private static BranchLitresEntry CreateBranchLitresEntry() =>
        new(
            Guid.Parse("7f20e446-8001-4000-9000-000000000302"),
            Period,
            new CanonicalBranchId("TAUPO"),
            new DateOnly(2026, 4, 1),
            new Litres(42.125m),
            new SourceReference("branch.xlsx", sheetName: "April", rowNumber: 2),
            new RentalAgreementNumber("RA-123"),
            new Rego("abc-123"));

    private static CarsBillingEntry CreateCarsBillingEntry() =>
        new(
            Guid.Parse("7f20e446-8001-4000-9000-000000000303"),
            Period,
            new SourceReference("cars.xlsx", sheetName: "Export", rowNumber: 2),
            new CanonicalBranchId("TAUPO"),
            new DateOnly(2026, 4, 1),
            new RentalAgreementNumber("RA-123"),
            new Rego("abc-123"),
            new Litres(42.125m),
            new MoneyAmount(123.455m),
            "Closed");

    private static ReconciliationItem CreateReconciliationItem(Guid runId, string id, ReconciliationStatus status) =>
        new(
            Guid.Parse(id),
            runId,
            Period,
            status,
            status == ReconciliationStatus.Matched ? ResolutionStatus.Resolved : ResolutionStatus.Unresolved,
            ConfidenceBucket.High,
            status == ReconciliationStatus.Matched ? ["MatchedByRA"] : ["Unbilled"],
            new CanonicalBranchId("TAUPO"),
            branchSourceReference: new SourceReference("branch.xlsx", sheetName: "April", rowNumber: 2));

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
            var databasePath = Path.Combine(Path.GetTempPath(), $"fuelrecon-repo-{Guid.NewGuid():N}.db");
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
