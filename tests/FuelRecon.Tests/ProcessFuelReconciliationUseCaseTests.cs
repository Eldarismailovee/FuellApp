using FuelRecon.Application.Processing;
using FuelRecon.Application.Reconciliation;
using FuelRecon.Application.Validation;
using FuelRecon.Domain;

namespace FuelRecon.Tests;

public class ProcessFuelReconciliationUseCaseTests
{
    [Fact]
    public void Execute_runs_full_valid_pipeline()
    {
        var dependencies = FakeDependencies.Valid();

        var result = dependencies.UseCase.Execute(CreateRequest());

        Assert.True(result.Success);
        Assert.Equal(1, dependencies.Validator.CallCount);
        Assert.Equal(1, dependencies.ImportPersister.CallCount);
        Assert.Equal(1, dependencies.Engine.CallCount);
        Assert.Equal(1, dependencies.ReconciliationPersister.CallCount);
        Assert.NotNull(result.ReconciliationEngineResult);
        Assert.NotNull(result.ReconciliationPersistenceResponse);
    }

    [Fact]
    public void Execute_stops_before_reconciliation_when_validation_has_blocking_errors()
    {
        var dependencies = FakeDependencies.InvalidValidation();

        var result = dependencies.UseCase.Execute(CreateRequest());

        Assert.False(result.Success);
        Assert.Equal(1, dependencies.Validator.CallCount);
        Assert.Equal(1, dependencies.ImportPersister.CallCount);
        Assert.Equal(0, dependencies.Engine.CallCount);
        Assert.Equal(0, dependencies.ReconciliationPersister.CallCount);
        Assert.Null(result.ReconciliationEngineResult);
        Assert.Null(result.ReconciliationPersistenceResponse);
    }

    [Fact]
    public void Execute_output_includes_import_batch_id_and_reconciliation_run_id_when_successful()
    {
        var dependencies = FakeDependencies.Valid();

        var result = dependencies.UseCase.Execute(CreateRequest());

        Assert.Equal(dependencies.ImportPersister.Response.ImportBatchId, result.ImportPersistenceResponse.ImportBatchId);
        Assert.NotNull(result.ReconciliationPersistenceResponse);
        Assert.Equal(dependencies.ReconciliationPersister.Response.RunId, result.ReconciliationPersistenceResponse.RunId);
    }

    [Fact]
    public void Execute_passes_paths_aliases_actor_and_checksums_to_validation_and_import_persistence()
    {
        var dependencies = FakeDependencies.Valid();
        var request = CreateRequest();

        dependencies.UseCase.Execute(request);

        Assert.NotNull(dependencies.Validator.LastRequest);
        Assert.Equal(request.Period, dependencies.Validator.LastRequest.Period);
        Assert.Equal(request.SupplierPdfPath, dependencies.Validator.LastRequest.SupplierPdfPath);
        Assert.Equal(request.BranchLitresExcelPath, dependencies.Validator.LastRequest.BranchLitresExcelPath);
        Assert.Equal(request.CarsBillingExcelPath, dependencies.Validator.LastRequest.CarsBillingExcelPath);
        Assert.Same(request.BranchAliasResolver, dependencies.Validator.LastRequest.BranchAliasResolver);

        Assert.NotNull(dependencies.ImportPersister.LastRequest);
        Assert.Equal(request.ImportedBy, dependencies.ImportPersister.LastRequest.ImportedBy);
        Assert.Equal(request.FileChecksums, dependencies.ImportPersister.LastRequest.FileChecksums);
    }

    [Fact]
    public void Execute_does_not_swallow_import_persistence_exceptions()
    {
        var dependencies = FakeDependencies.Valid();
        dependencies.ImportPersister.ExceptionToThrow = new InvalidOperationException("import persistence failed");

        var exception = Assert.Throws<InvalidOperationException>(() => dependencies.UseCase.Execute(CreateRequest()));

        Assert.Equal("import persistence failed", exception.Message);
    }

    [Fact]
    public void Execute_does_not_swallow_reconciliation_persistence_exceptions()
    {
        var dependencies = FakeDependencies.Valid();
        dependencies.ReconciliationPersister.ExceptionToThrow = new InvalidOperationException("reconciliation persistence failed");

        var exception = Assert.Throws<InvalidOperationException>(() => dependencies.UseCase.Execute(CreateRequest()));

        Assert.Equal("reconciliation persistence failed", exception.Message);
    }

    private static ProcessFuelReconciliationRequest CreateRequest()
    {
        var taupo = new BranchMaster(new CanonicalBranchId("TAUPO"), "Taupo");
        return new ProcessFuelReconciliationRequest(
            new FuelPeriod(2026, 4),
            "supplier.pdf",
            "branch.xlsx",
            "cars.xlsx",
            new BranchAliasResolver([taupo], [new BranchAlias("Taupo", taupo.Id)]),
            ImportedBy: "importer",
            RunBy: "runner",
            new Dictionary<InputSlot, FileChecksum>
            {
                [InputSlot.SupplierStatement] = new("SHA256", "supplier"),
            },
            ReconciliationRules: new ReconciliationRulesOptions(RunCreatedAtUtc: new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero)),
            ImportedAtUtc: new DateTimeOffset(2026, 5, 15, 11, 0, 0, TimeSpan.Zero));
    }

    private static ImportValidationResult CreateValidValidationResult()
    {
        var supplier = new SupplierTransaction(
            Guid.Parse("173d4e65-277a-4987-9000-000000000001"),
            "Mobil",
            new FuelPeriod(2026, 4),
            new DateOnly(2026, 4, 1),
            new Litres(10m),
            new SourceReference("supplier.pdf", pageNumber: 1),
            new CanonicalBranchId("TAUPO"));
        var branch = new BranchLitresEntry(
            Guid.Parse("173d4e65-277a-4987-9000-000000000002"),
            new FuelPeriod(2026, 4),
            new CanonicalBranchId("TAUPO"),
            new DateOnly(2026, 4, 1),
            new Litres(10m),
            new SourceReference("branch.xlsx", sheetName: "April", rowNumber: 2),
            new RentalAgreementNumber("RA-1"));
        var cars = new CarsBillingEntry(
            Guid.Parse("173d4e65-277a-4987-9000-000000000003"),
            new FuelPeriod(2026, 4),
            new SourceReference("cars.xlsx", sheetName: "Export", rowNumber: 2),
            new CanonicalBranchId("TAUPO"),
            new DateOnly(2026, 4, 1),
            new RentalAgreementNumber("RA-1"),
            billedLitres: new Litres(10m));

        return new ImportValidationResult(
            Success: true,
            Files:
            [
                ValidFile(InputSlot.SupplierStatement, "supplier.pdf"),
                ValidFile(InputSlot.BranchLitres, "branch.xlsx"),
                ValidFile(InputSlot.CarsBilling, "cars.xlsx"),
            ],
            SupplierTransactions: [supplier],
            BranchLitresEntries: [branch],
            CarsBillingEntries: [cars]);
    }

    private static ImportValidationResult CreateInvalidValidationResult() =>
        new(
            Success: false,
            Files:
            [
                new ImportFileValidationResult(
                    InputSlot.SupplierStatement,
                    null,
                    FileStatus.Invalid,
                    RowCount: 0,
                    ValidRowCount: 0,
                    SkippedRowCount: 0,
                    [new ImportValidationIssue(ValidationSeverity.Error, "MissingMandatoryFile", "Missing supplier")]),
                ValidFile(InputSlot.BranchLitres, "branch.xlsx"),
                ValidFile(InputSlot.CarsBilling, "cars.xlsx"),
            ],
            SupplierTransactions: [],
            BranchLitresEntries: [],
            CarsBillingEntries: []);

    private static ImportFileValidationResult ValidFile(InputSlot inputSlot, string path) =>
        new(inputSlot, path, FileStatus.Valid, RowCount: 1, ValidRowCount: 1, SkippedRowCount: 0, []);

    private sealed class FakeDependencies
    {
        private FakeDependencies(
            FakeValidateImportBatchUseCase validator,
            FakePersistImportValidationResultUseCase importPersister,
            FakeReconciliationEngine engine,
            FakePersistReconciliationResultUseCase reconciliationPersister)
        {
            Validator = validator;
            ImportPersister = importPersister;
            Engine = engine;
            ReconciliationPersister = reconciliationPersister;
            UseCase = new ProcessFuelReconciliationUseCase(validator, importPersister, engine, reconciliationPersister);
        }

        public FakeValidateImportBatchUseCase Validator { get; }
        public FakePersistImportValidationResultUseCase ImportPersister { get; }
        public FakeReconciliationEngine Engine { get; }
        public FakePersistReconciliationResultUseCase ReconciliationPersister { get; }
        public ProcessFuelReconciliationUseCase UseCase { get; }

        public static FakeDependencies Valid()
        {
            var validationResult = CreateValidValidationResult();
            var engineResult = new DeterministicReconciliationEngine().Reconcile(new ReconciliationEngineInput(
                new FuelPeriod(2026, 4),
                validationResult.SupplierTransactions,
                validationResult.BranchLitresEntries,
                validationResult.CarsBillingEntries));

            return new FakeDependencies(
                new FakeValidateImportBatchUseCase(validationResult),
                new FakePersistImportValidationResultUseCase(),
                new FakeReconciliationEngine(engineResult),
                new FakePersistReconciliationResultUseCase(engineResult.Run.Id));
        }

        public static FakeDependencies InvalidValidation()
        {
            var validationResult = CreateInvalidValidationResult();
            return new FakeDependencies(
                new FakeValidateImportBatchUseCase(validationResult),
                new FakePersistImportValidationResultUseCase(),
                new FakeReconciliationEngine(null),
                new FakePersistReconciliationResultUseCase(Guid.Parse("173d4e65-277a-4987-9000-000000000010")));
        }
    }

    private sealed class FakeValidateImportBatchUseCase(ImportValidationResult result) : IValidateImportBatchUseCase
    {
        public int CallCount { get; private set; }
        public ValidateImportBatchRequest? LastRequest { get; private set; }

        public ImportValidationResult Execute(ValidateImportBatchRequest request)
        {
            CallCount++;
            LastRequest = request;
            return result;
        }
    }

    private sealed class FakePersistImportValidationResultUseCase : IPersistImportValidationResultUseCase
    {
        public int CallCount { get; private set; }
        public PersistImportValidationResultRequest? LastRequest { get; private set; }
        public Exception? ExceptionToThrow { get; set; }
        public PersistImportValidationResultResponse Response { get; } = new(
            Guid.Parse("173d4e65-277a-4987-9000-000000000020"),
            new Dictionary<InputSlot, Guid>
            {
                [InputSlot.SupplierStatement] = Guid.Parse("173d4e65-277a-4987-9000-000000000021"),
                [InputSlot.BranchLitres] = Guid.Parse("173d4e65-277a-4987-9000-000000000022"),
                [InputSlot.CarsBilling] = Guid.Parse("173d4e65-277a-4987-9000-000000000023"),
            });

        public PersistImportValidationResultResponse Execute(PersistImportValidationResultRequest request)
        {
            CallCount++;
            LastRequest = request;
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Response;
        }
    }

    private sealed class FakeReconciliationEngine(ReconciliationEngineResult? result) : IReconciliationEngine
    {
        public int CallCount { get; private set; }
        public ReconciliationEngineInput? LastInput { get; private set; }

        public ReconciliationEngineResult Reconcile(ReconciliationEngineInput input)
        {
            CallCount++;
            LastInput = input;
            return result ?? throw new InvalidOperationException("Reconciliation should not have been called.");
        }
    }

    private sealed class FakePersistReconciliationResultUseCase(Guid runId) : IPersistReconciliationResultUseCase
    {
        public int CallCount { get; private set; }
        public PersistReconciliationResultRequest? LastRequest { get; private set; }
        public Exception? ExceptionToThrow { get; set; }
        public PersistReconciliationResultResponse Response { get; } = new(runId, SavedItemCount: 1);

        public PersistReconciliationResultResponse Execute(PersistReconciliationResultRequest request)
        {
            CallCount++;
            LastRequest = request;
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Response;
        }
    }
}
