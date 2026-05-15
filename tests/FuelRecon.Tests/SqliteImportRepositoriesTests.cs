using FuelRecon.Application.Persistence;
using FuelRecon.Domain;
using FuelRecon.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace FuelRecon.Tests;

public class SqliteImportRepositoriesTests
{
    [Fact]
    public void ImportBatchRepository_saves_and_retrieves_import_batch()
    {
        using var database = RepositoryTestDatabase.Create();
        var repository = new SqliteImportBatchRepository(database.ConnectionFactory);
        var batch = CreateImportBatch();

        repository.Save(batch);

        var loaded = repository.GetById(batch.Id);

        Assert.NotNull(loaded);
        Assert.Equal(batch.Id, loaded.Id);
        Assert.Equal(batch.Period, loaded.Period);
        Assert.Equal(batch.CreatedAtUtc, loaded.CreatedAtUtc);
        Assert.Equal(batch.CreatedBy, loaded.CreatedBy);
        Assert.Equal(batch.Status, loaded.Status);
        Assert.Equal(batch.SourceDescription, loaded.SourceDescription);
    }

    [Fact]
    public void ImportedFileRepository_saves_and_retrieves_imported_file()
    {
        using var database = RepositoryTestDatabase.Create();
        var batch = CreateImportBatch();
        new SqliteImportBatchRepository(database.ConnectionFactory).Save(batch);
        var repository = new SqliteImportedFileRepository(database.ConnectionFactory);
        var file = CreateImportedFile(batch, InputSlot.SupplierStatement, "supplier.pdf");

        repository.Save(file);

        var loaded = repository.GetById(file.Id);
        var batchFiles = repository.ListByImportBatch(batch.Id);

        Assert.NotNull(loaded);
        Assert.Equal(file.Id, loaded.Id);
        Assert.Equal(batch.Id, loaded.ImportBatchId);
        Assert.Equal(file.Period, loaded.Period);
        Assert.Equal(InputSlot.SupplierStatement, loaded.InputSlot);
        Assert.Equal("supplier.pdf", loaded.OriginalFileName);
        Assert.Equal(FileStatus.Parsed, loaded.FileStatus);
        Assert.Equal(file.Checksum.Algorithm, loaded.Checksum.Algorithm);
        Assert.Equal(file.Checksum.Value, loaded.Checksum.Value);
        Assert.Single(batchFiles);
    }

    [Fact]
    public void SupplierTransactionRepository_saves_and_retrieves_supplier_transactions()
    {
        using var database = RepositoryTestDatabase.Create();
        var (batch, file) = SaveBatchAndFile(database, InputSlot.SupplierStatement);
        var repository = new SqliteSupplierTransactionRepository(database.ConnectionFactory);
        var transaction = new SupplierTransaction(
            Guid.Parse("69f76680-fc0d-4795-9000-000000000001"),
            "Mobil",
            batch.Period,
            new DateOnly(2026, 4, 3),
            new Litres(42.125m),
            new SourceReference("supplier.pdf", pageNumber: 2, referenceText: "voucher row"),
            new CanonicalBranchId("TAUPO"),
            rawBranchText: "Mobil Taupo",
            rawSiteText: "Taupo Site",
            cardholder: "Driver One",
            voucherOrInvoiceReference: "INV-1",
            product: "Diesel",
            amount: new MoneyAmount(123.455m),
            validationIssueCodes: ["LowConfidenceMatch"]);

        repository.Save(batch.Id, file.Id, transaction);

        var loaded = repository.GetById(transaction.Id);
        var rows = repository.ListByImportBatch(batch.Id);

        Assert.NotNull(loaded);
        Assert.Equal(transaction.Id, loaded.Id);
        Assert.Equal("Mobil", loaded.SupplierName);
        Assert.Equal(batch.Period, loaded.Period);
        Assert.Equal("TAUPO", loaded.BranchId?.Value);
        Assert.Equal(new DateOnly(2026, 4, 3), loaded.TransactionDate);
        Assert.Equal("Mobil Taupo", loaded.RawBranchText);
        Assert.Equal("Taupo Site", loaded.RawSiteText);
        Assert.Equal("Driver One", loaded.Cardholder);
        Assert.Equal("INV-1", loaded.VoucherOrInvoiceReference);
        Assert.Equal("Diesel", loaded.Product);
        Assert.Equal(42.13m, loaded.Litres.Value);
        Assert.Equal(123.46m, loaded.Amount?.Value);
        Assert.Equal("supplier.pdf", loaded.SourceReference.SourceFile);
        Assert.Equal(2, loaded.SourceReference.PageNumber);
        Assert.Equal("voucher row", loaded.SourceReference.ReferenceText);
        Assert.Equal(["LowConfidenceMatch"], loaded.ValidationIssueCodes);
        Assert.Single(rows);
    }

    [Fact]
    public void BranchLitresRepository_saves_and_retrieves_branch_litres_entries()
    {
        using var database = RepositoryTestDatabase.Create();
        var (batch, file) = SaveBatchAndFile(database, InputSlot.BranchLitres);
        var repository = new SqliteBranchLitresRepository(database.ConnectionFactory);
        var entry = new BranchLitresEntry(
            Guid.Parse("69f76680-fc0d-4795-9000-000000000002"),
            batch.Period,
            new CanonicalBranchId("TAUPO"),
            new DateOnly(2026, 4, 4),
            new Litres(20.225m),
            new SourceReference("branch.xlsx", sheetName: "April", rowNumber: 12),
            new RentalAgreementNumber(" RA-123 "),
            new Rego("abc-123"),
            "manual note",
            ["InvalidRA"]);

        repository.Save(batch.Id, file.Id, entry);

        var loaded = repository.GetById(entry.Id);
        var rows = repository.ListByImportBatch(batch.Id);

        Assert.NotNull(loaded);
        Assert.Equal(entry.Id, loaded.Id);
        Assert.Equal(batch.Period, loaded.Period);
        Assert.Equal("TAUPO", loaded.BranchId.Value);
        Assert.Equal(new DateOnly(2026, 4, 4), loaded.Date);
        Assert.Equal(" RA-123 ", loaded.RentalAgreementNumber?.RawValue);
        Assert.Equal("RA123", loaded.RentalAgreementNumber?.NormalisedValue);
        Assert.Equal("abc-123", loaded.Rego?.RawValue);
        Assert.Equal("ABC123", loaded.Rego?.NormalisedValue);
        Assert.Equal("manual note", loaded.NoteOrReference);
        Assert.Equal(20.23m, loaded.Litres.Value);
        Assert.Equal("branch.xlsx", loaded.SourceReference.SourceFile);
        Assert.Equal("April", loaded.SourceReference.SheetName);
        Assert.Equal(12, loaded.SourceReference.RowNumber);
        Assert.Equal(["InvalidRA"], loaded.ValidationIssueCodes);
        Assert.Single(rows);
    }

    [Fact]
    public void CarsBillingRepository_saves_and_retrieves_cars_billing_entries()
    {
        using var database = RepositoryTestDatabase.Create();
        var (batch, file) = SaveBatchAndFile(database, InputSlot.CarsBilling);
        var repository = new SqliteCarsBillingRepository(database.ConnectionFactory);
        var entry = new CarsBillingEntry(
            Guid.Parse("69f76680-fc0d-4795-9000-000000000003"),
            batch.Period,
            new SourceReference("cars.xlsx", sheetName: "Export", rowNumber: 50),
            new CanonicalBranchId("TAUPO"),
            new DateOnly(2026, 4, 5),
            new RentalAgreementNumber("RA-456"),
            new Rego("xy-999"),
            new Litres(30.125m),
            new MoneyAmount(90.555m),
            "Closed",
            ["MissingCharge"]);

        repository.Save(batch.Id, file.Id, entry);

        var loaded = repository.GetById(entry.Id);
        var rows = repository.ListByImportBatch(batch.Id);

        Assert.NotNull(loaded);
        Assert.Equal(entry.Id, loaded.Id);
        Assert.Equal(batch.Period, loaded.Period);
        Assert.Equal("TAUPO", loaded.BranchId?.Value);
        Assert.Equal(new DateOnly(2026, 4, 5), loaded.Date);
        Assert.Equal("RA-456", loaded.RentalAgreementNumber?.RawValue);
        Assert.Equal("RA456", loaded.RentalAgreementNumber?.NormalisedValue);
        Assert.Equal("xy-999", loaded.Rego?.RawValue);
        Assert.Equal("XY999", loaded.Rego?.NormalisedValue);
        Assert.Equal(30.13m, loaded.BilledLitres?.Value);
        Assert.Equal(90.56m, loaded.BilledAmount?.Value);
        Assert.Equal("Closed", loaded.BillingStatus);
        Assert.Equal("cars.xlsx", loaded.SourceReference.SourceFile);
        Assert.Equal("Export", loaded.SourceReference.SheetName);
        Assert.Equal(50, loaded.SourceReference.RowNumber);
        Assert.Equal(["MissingCharge"], loaded.ValidationIssueCodes);
        Assert.Single(rows);
    }

    [Fact]
    public void Multi_row_save_rolls_back_when_one_insert_fails()
    {
        using var database = RepositoryTestDatabase.Create();
        var (batch, file) = SaveBatchAndFile(database, InputSlot.BranchLitres);
        var repository = new SqliteBranchLitresRepository(database.ConnectionFactory);
        var duplicateId = Guid.Parse("69f76680-fc0d-4795-9000-000000000004");
        var first = CreateBranchEntry(duplicateId, batch.Period, "first.xlsx");
        var second = CreateBranchEntry(duplicateId, batch.Period, "second.xlsx");

        Assert.Throws<SqliteException>(() => repository.SaveMany(batch.Id, file.Id, [first, second]));

        var rows = repository.ListByImportBatch(batch.Id);
        Assert.Empty(rows);
    }

    private static (ImportBatchRecord Batch, ImportedFileRecord File) SaveBatchAndFile(
        RepositoryTestDatabase database,
        InputSlot inputSlot)
    {
        var batch = CreateImportBatch();
        new SqliteImportBatchRepository(database.ConnectionFactory).Save(batch);
        var file = CreateImportedFile(batch, inputSlot, $"{inputSlot}.dat");
        new SqliteImportedFileRepository(database.ConnectionFactory).Save(file);
        return (batch, file);
    }

    private static ImportBatchRecord CreateImportBatch() =>
        new(
            Guid.Parse("69f76680-fc0d-4795-9000-000000000100"),
            new FuelPeriod(2026, 4),
            new DateTimeOffset(2026, 5, 15, 1, 2, 3, TimeSpan.Zero),
            "arina",
            "Created",
            "April import");

    private static ImportedFileRecord CreateImportedFile(ImportBatchRecord batch, InputSlot inputSlot, string fileName) =>
        new(
            Guid.NewGuid(),
            batch.Id,
            batch.Period,
            inputSlot,
            fileName,
            FileStatus.Parsed,
            new FileChecksum("SHA256", $"checksum-{inputSlot}"),
            new DateTimeOffset(2026, 5, 15, 2, 3, 4, TimeSpan.Zero),
            StoredFilePath: $"/imports/{fileName}",
            ParserName: "test-parser",
            ParserVersion: "1.0",
            CompletedAtUtc: new DateTimeOffset(2026, 5, 15, 2, 4, 4, TimeSpan.Zero));

    private static BranchLitresEntry CreateBranchEntry(Guid id, FuelPeriod period, string sourceFile) =>
        new(
            id,
            period,
            new CanonicalBranchId("TAUPO"),
            new DateOnly(2026, 4, 6),
            new Litres(10m),
            new SourceReference(sourceFile, sheetName: "April", rowNumber: 1));

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
            var connectionString = $"Data Source=RepoTests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var factory = new SqliteConnectionFactory(connectionString, isConnectionString: true);
            var rootConnection = factory.OpenConnection();
            SqliteSchemaMigrator.ApplyInitialSchema(rootConnection);

            return new RepositoryTestDatabase(factory, rootConnection);
        }

        public void Dispose() => rootConnection.Dispose();
    }
}
