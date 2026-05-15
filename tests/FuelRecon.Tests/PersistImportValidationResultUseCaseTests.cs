using FuelRecon.Application.Persistence;
using FuelRecon.Application.Validation;
using FuelRecon.Domain;

namespace FuelRecon.Tests;

public class PersistImportValidationResultUseCaseTests
{
    [Fact]
    public void Execute_persists_one_import_batch_and_three_imported_files()
    {
        var repositories = new FakeRepositories();
        var useCase = repositories.CreateUseCase();
        var request = CreateRequest(CreateValidationResult(success: true));

        var response = useCase.Execute(request);

        var batch = Assert.Single(repositories.ImportBatches.Saved);
        Assert.Equal(response.ImportBatchId, batch.Id);
        Assert.Equal(request.Period, batch.Period);
        Assert.Equal("arina", batch.CreatedBy);
        Assert.Equal("Validated", batch.Status);

        Assert.Equal(3, repositories.ImportedFiles.Saved.Count);
        Assert.Contains(InputSlot.SupplierStatement, response.ImportedFileIds.Keys);
        Assert.Contains(InputSlot.BranchLitres, response.ImportedFileIds.Keys);
        Assert.Contains(InputSlot.CarsBilling, response.ImportedFileIds.Keys);
        Assert.All(repositories.ImportedFiles.Saved, file => Assert.Equal(response.ImportBatchId, file.ImportBatchId));
    }

    [Fact]
    public void Execute_persists_imported_file_records_with_status_and_checksums()
    {
        var repositories = new FakeRepositories();
        var useCase = repositories.CreateUseCase();
        var result = CreateValidationResult(success: false, supplierStatus: FileStatus.Invalid);
        var request = CreateRequest(result);

        var response = useCase.Execute(request);

        var supplierFile = repositories.ImportedFiles.Saved.Single(file => file.InputSlot == InputSlot.SupplierStatement);
        var branchFile = repositories.ImportedFiles.Saved.Single(file => file.InputSlot == InputSlot.BranchLitres);

        Assert.Equal(response.ImportedFileIds[InputSlot.SupplierStatement], supplierFile.Id);
        Assert.Equal(FileStatus.Invalid, supplierFile.FileStatus);
        Assert.Equal("supplier.pdf", supplierFile.OriginalFileName);
        Assert.Equal("/imports/supplier.pdf", supplierFile.StoredFilePath);
        Assert.Equal("SHA256", supplierFile.Checksum.Algorithm);
        Assert.Equal("supplier-checksum", supplierFile.Checksum.Value);
        Assert.Equal("ParserFailed", supplierFile.FailureReasonCode);

        Assert.Equal("UNAVAILABLE", branchFile.Checksum.Algorithm);
        Assert.Equal("UNAVAILABLE", branchFile.Checksum.Value);
    }

    [Fact]
    public void Execute_persists_parsed_rows_from_all_sources()
    {
        var repositories = new FakeRepositories();
        var supplier = CreateSupplierTransaction();
        var branch = CreateBranchLitresEntry();
        var cars = CreateCarsBillingEntry();
        var useCase = repositories.CreateUseCase();

        var response = useCase.Execute(CreateRequest(new ImportValidationResult(
            Success: true,
            Files: CreateValidFiles(),
            SupplierTransactions: [supplier],
            BranchLitresEntries: [branch],
            CarsBillingEntries: [cars])));

        var supplierSave = Assert.Single(repositories.SupplierTransactions.SavedManyCalls);
        var branchSave = Assert.Single(repositories.BranchLitres.SavedManyCalls);
        var carsSave = Assert.Single(repositories.CarsBilling.SavedManyCalls);

        Assert.Equal(response.ImportBatchId, supplierSave.ImportBatchId);
        Assert.Equal(response.ImportedFileIds[InputSlot.SupplierStatement], supplierSave.ImportedFileId);
        Assert.Same(supplier, Assert.Single(supplierSave.Rows));

        Assert.Equal(response.ImportBatchId, branchSave.ImportBatchId);
        Assert.Equal(response.ImportedFileIds[InputSlot.BranchLitres], branchSave.ImportedFileId);
        Assert.Same(branch, Assert.Single(branchSave.Rows));

        Assert.Equal(response.ImportBatchId, carsSave.ImportBatchId);
        Assert.Equal(response.ImportedFileIds[InputSlot.CarsBilling], carsSave.ImportedFileId);
        Assert.Same(cars, Assert.Single(carsSave.Rows));
    }

    [Fact]
    public void Execute_does_not_persist_rows_for_missing_mandatory_file_result()
    {
        var repositories = new FakeRepositories();
        var useCase = repositories.CreateUseCase();
        var validationResult = new ImportValidationResult(
            Success: false,
            Files:
            [
                new ImportFileValidationResult(
                    InputSlot.SupplierStatement,
                    FilePath: null,
                    FileStatus.Invalid,
                    RowCount: 0,
                    ValidRowCount: 0,
                    SkippedRowCount: 0,
                    [
                        new ImportValidationIssue(
                            ValidationSeverity.Error,
                            ValidateImportBatchUseCase.MissingMandatoryFileReasonCode,
                            "Missing")
                    ]),
                CreateValidFile(InputSlot.BranchLitres, "/imports/branch.xlsx"),
                CreateValidFile(InputSlot.CarsBilling, "/imports/cars.xlsx"),
            ],
            SupplierTransactions: [],
            BranchLitresEntries: [CreateBranchLitresEntry()],
            CarsBillingEntries: [CreateCarsBillingEntry()]);

        var response = useCase.Execute(CreateRequest(validationResult));

        Assert.Equal(3, repositories.ImportedFiles.Saved.Count);
        Assert.Contains(InputSlot.SupplierStatement, response.ImportedFileIds.Keys);
        Assert.Empty(repositories.SupplierTransactions.SavedManyCalls);
        Assert.Single(repositories.BranchLitres.SavedManyCalls);
        Assert.Single(repositories.CarsBilling.SavedManyCalls);
    }

    [Fact]
    public void Execute_creates_missing_file_records_when_file_result_is_absent()
    {
        var repositories = new FakeRepositories();
        var useCase = repositories.CreateUseCase();
        var validationResult = new ImportValidationResult(
            Success: false,
            Files: [CreateValidFile(InputSlot.SupplierStatement, "/imports/supplier.pdf")],
            SupplierTransactions: [CreateSupplierTransaction()],
            BranchLitresEntries: [],
            CarsBillingEntries: []);

        var response = useCase.Execute(CreateRequest(validationResult));

        Assert.Equal(3, repositories.ImportedFiles.Saved.Count);
        Assert.Contains(InputSlot.BranchLitres, response.ImportedFileIds.Keys);
        Assert.Contains(InputSlot.CarsBilling, response.ImportedFileIds.Keys);
        Assert.Contains(repositories.ImportedFiles.Saved, file =>
            file.InputSlot == InputSlot.BranchLitres
            && file.FileStatus == FileStatus.Invalid
            && file.OriginalFileName == "missing-BranchLitres");
    }

    [Fact]
    public void Execute_returns_created_batch_and_file_ids()
    {
        var repositories = new FakeRepositories();
        var response = repositories.CreateUseCase().Execute(CreateRequest(CreateValidationResult(success: true)));

        Assert.NotEqual(Guid.Empty, response.ImportBatchId);
        Assert.Equal(3, response.ImportedFileIds.Count);
        Assert.All(response.ImportedFileIds.Values, id => Assert.NotEqual(Guid.Empty, id));
    }

    private static PersistImportValidationResultRequest CreateRequest(ImportValidationResult validationResult) =>
        new(
            validationResult,
            new FuelPeriod(2026, 4),
            "arina",
            new Dictionary<InputSlot, FileChecksum>
            {
                [InputSlot.SupplierStatement] = new("SHA256", "supplier-checksum"),
            },
            new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero));

    private static ImportValidationResult CreateValidationResult(bool success, FileStatus supplierStatus = FileStatus.Valid) =>
        new(
            success,
            supplierStatus == FileStatus.Valid
                ? CreateValidFiles()
                :
                [
                    new ImportFileValidationResult(
                        InputSlot.SupplierStatement,
                        "/imports/supplier.pdf",
                        supplierStatus,
                        RowCount: 1,
                        ValidRowCount: 0,
                        SkippedRowCount: 1,
                        [new ImportValidationIssue(ValidationSeverity.Error, "ParserFailed", "Parser failed")]),
                    CreateValidFile(InputSlot.BranchLitres, "/imports/branch.xlsx"),
                    CreateValidFile(InputSlot.CarsBilling, "/imports/cars.xlsx"),
                ],
            [CreateSupplierTransaction()],
            [CreateBranchLitresEntry()],
            [CreateCarsBillingEntry()]);

    private static IReadOnlyList<ImportFileValidationResult> CreateValidFiles() =>
        [
            CreateValidFile(InputSlot.SupplierStatement, "/imports/supplier.pdf"),
            CreateValidFile(InputSlot.BranchLitres, "/imports/branch.xlsx"),
            CreateValidFile(InputSlot.CarsBilling, "/imports/cars.xlsx"),
        ];

    private static ImportFileValidationResult CreateValidFile(InputSlot inputSlot, string path) =>
        new(
            inputSlot,
            path,
            FileStatus.Valid,
            RowCount: 1,
            ValidRowCount: 1,
            SkippedRowCount: 0,
            []);

    private static SupplierTransaction CreateSupplierTransaction() =>
        new(
            Guid.Parse("fd25992e-62f9-45da-9000-000000000001"),
            "Mobil",
            new FuelPeriod(2026, 4),
            new DateOnly(2026, 4, 1),
            new Litres(10m),
            new SourceReference("supplier.pdf", pageNumber: 1));

    private static BranchLitresEntry CreateBranchLitresEntry() =>
        new(
            Guid.Parse("fd25992e-62f9-45da-9000-000000000002"),
            new FuelPeriod(2026, 4),
            new CanonicalBranchId("TAUPO"),
            new DateOnly(2026, 4, 1),
            new Litres(10m),
            new SourceReference("branch.xlsx", sheetName: "Sheet1", rowNumber: 2));

    private static CarsBillingEntry CreateCarsBillingEntry() =>
        new(
            Guid.Parse("fd25992e-62f9-45da-9000-000000000003"),
            new FuelPeriod(2026, 4),
            new SourceReference("cars.xlsx", sheetName: "Export", rowNumber: 2),
            rentalAgreementNumber: new RentalAgreementNumber("RA-1"),
            billedAmount: new MoneyAmount(10m));

    private sealed class FakeRepositories
    {
        public FakeImportBatchRepository ImportBatches { get; } = new();
        public FakeImportedFileRepository ImportedFiles { get; } = new();
        public FakeSupplierTransactionRepository SupplierTransactions { get; } = new();
        public FakeBranchLitresRepository BranchLitres { get; } = new();
        public FakeCarsBillingRepository CarsBilling { get; } = new();

        public PersistImportValidationResultUseCase CreateUseCase() =>
            new(ImportBatches, ImportedFiles, SupplierTransactions, BranchLitres, CarsBilling);
    }

    private sealed class FakeImportBatchRepository : IImportBatchRepository
    {
        public List<ImportBatchRecord> Saved { get; } = [];
        public void Save(ImportBatchRecord batch) => Saved.Add(batch);
        public ImportBatchRecord? GetById(Guid id) => Saved.FirstOrDefault(batch => batch.Id == id);
    }

    private sealed class FakeImportedFileRepository : IImportedFileRepository
    {
        public List<ImportedFileRecord> Saved { get; } = [];
        public void Save(ImportedFileRecord file) => Saved.Add(file);
        public ImportedFileRecord? GetById(Guid id) => Saved.FirstOrDefault(file => file.Id == id);
        public IReadOnlyList<ImportedFileRecord> ListByImportBatch(Guid importBatchId) =>
            Saved.Where(file => file.ImportBatchId == importBatchId).ToArray();
    }

    private sealed class FakeSupplierTransactionRepository : ISupplierTransactionRepository
    {
        public List<SaveManyCall<SupplierTransaction>> SavedManyCalls { get; } = [];
        public void Save(Guid importBatchId, Guid importedFileId, SupplierTransaction transaction) =>
            SaveMany(importBatchId, importedFileId, [transaction]);
        public void SaveMany(Guid importBatchId, Guid importedFileId, IEnumerable<SupplierTransaction> transactions) =>
            SavedManyCalls.Add(new SaveManyCall<SupplierTransaction>(importBatchId, importedFileId, transactions.ToArray()));
        public SupplierTransaction? GetById(Guid id) => null;
        public IReadOnlyList<SupplierTransaction> ListByImportBatch(Guid importBatchId) => [];
    }

    private sealed class FakeBranchLitresRepository : IBranchLitresRepository
    {
        public List<SaveManyCall<BranchLitresEntry>> SavedManyCalls { get; } = [];
        public void Save(Guid importBatchId, Guid importedFileId, BranchLitresEntry entry) =>
            SaveMany(importBatchId, importedFileId, [entry]);
        public void SaveMany(Guid importBatchId, Guid importedFileId, IEnumerable<BranchLitresEntry> entries) =>
            SavedManyCalls.Add(new SaveManyCall<BranchLitresEntry>(importBatchId, importedFileId, entries.ToArray()));
        public BranchLitresEntry? GetById(Guid id) => null;
        public IReadOnlyList<BranchLitresEntry> ListByImportBatch(Guid importBatchId) => [];
    }

    private sealed class FakeCarsBillingRepository : ICarsBillingRepository
    {
        public List<SaveManyCall<CarsBillingEntry>> SavedManyCalls { get; } = [];
        public void Save(Guid importBatchId, Guid importedFileId, CarsBillingEntry entry) =>
            SaveMany(importBatchId, importedFileId, [entry]);
        public void SaveMany(Guid importBatchId, Guid importedFileId, IEnumerable<CarsBillingEntry> entries) =>
            SavedManyCalls.Add(new SaveManyCall<CarsBillingEntry>(importBatchId, importedFileId, entries.ToArray()));
        public CarsBillingEntry? GetById(Guid id) => null;
        public IReadOnlyList<CarsBillingEntry> ListByImportBatch(Guid importBatchId) => [];
    }

    private sealed record SaveManyCall<T>(Guid ImportBatchId, Guid ImportedFileId, IReadOnlyList<T> Rows);
}
