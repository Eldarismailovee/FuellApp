using FuelRecon.Application.Excel;
using FuelRecon.Application.Pdf;
using FuelRecon.Application.Validation;
using FuelRecon.Domain;

namespace FuelRecon.Tests;

public class ValidateImportBatchUseCaseTests
{
    [Fact]
    public void Execute_returns_blocking_error_when_mandatory_file_is_missing()
    {
        var useCase = CreateUseCase();

        var result = useCase.Execute(new ValidateImportBatchRequest(
            new FuelPeriod(2026, 4),
            SupplierPdfPath: null,
            BranchLitresExcelPath: "branch.xlsx",
            CarsBillingExcelPath: "cars.xlsx",
            CreateBranchAliasResolver()));

        Assert.False(result.Success);
        var supplierFile = Assert.Single(result.Files, file => file.InputSlot == InputSlot.SupplierStatement);
        Assert.Equal(FileStatus.Invalid, supplierFile.Status);
        Assert.True(supplierFile.HasErrors);
        Assert.Equal(ValidateImportBatchUseCase.MissingMandatoryFileReasonCode, Assert.Single(supplierFile.Issues).ReasonCode);
    }

    [Fact]
    public void Execute_marks_parser_failure_as_invalid()
    {
        var supplierParser = new FakeSupplierPdfParser
        {
            Result = SupplierPdfParseResult.From(
                [],
                [new SupplierPdfParseIssue(ValidationSeverity.Error, "UnsupportedPdfLayout", "Unsupported")],
                pageCount: 1,
                candidateRowCount: 1,
                success: false)
        };
        var useCase = CreateUseCase(supplierParser: supplierParser);

        var result = ExecuteAllFiles(useCase);

        Assert.False(result.Success);
        var supplierFile = result.Files.Single(file => file.InputSlot == InputSlot.SupplierStatement);
        Assert.Equal(FileStatus.Invalid, supplierFile.Status);
        Assert.Equal(1, supplierFile.RowCount);
        Assert.Equal(0, supplierFile.ValidRowCount);
        Assert.Equal(1, supplierFile.SkippedRowCount);
        Assert.Equal("UnsupportedPdfLayout", Assert.Single(supplierFile.Issues).ReasonCode);
    }

    [Fact]
    public void Execute_maps_parser_warnings_to_parsed_status_without_blocking_success()
    {
        var branchParser = new FakeBranchLitresExcelParser
        {
            Result = BranchLitresParseResult.From(
                [CreateBranchLitresEntry()],
                [new BranchLitresParseIssue(ValidationSeverity.Warning, "MissingDateDefaultedToPeriodStart", "Defaulted")],
                rowCount: 1,
                success: true)
        };
        var useCase = CreateUseCase(branchParser: branchParser);

        var result = ExecuteAllFiles(useCase);

        Assert.True(result.Success);
        var branchFile = result.Files.Single(file => file.InputSlot == InputSlot.BranchLitres);
        Assert.Equal(FileStatus.Parsed, branchFile.Status);
        Assert.True(branchFile.HasWarnings);
        Assert.False(branchFile.HasErrors);
    }

    [Fact]
    public void Execute_returns_success_when_all_three_parser_results_are_valid()
    {
        var useCase = CreateUseCase();

        var result = ExecuteAllFiles(useCase);

        Assert.True(result.Success);
        Assert.False(result.HasBlockingErrors);
        Assert.Equal(3, result.Files.Count);
        Assert.All(result.Files, file => Assert.Equal(FileStatus.Valid, file.Status));
    }

    [Fact]
    public void Execute_preserves_parsed_rows_from_all_three_sources()
    {
        var supplierTransaction = CreateSupplierTransaction();
        var branchEntry = CreateBranchLitresEntry();
        var carsEntry = CreateCarsBillingEntry();
        var useCase = CreateUseCase(
            new FakeSupplierPdfParser
            {
                Result = SupplierPdfParseResult.From([supplierTransaction], [], pageCount: 1, candidateRowCount: 1, success: true)
            },
            new FakeBranchLitresExcelParser
            {
                Result = BranchLitresParseResult.From([branchEntry], [], rowCount: 1, success: true)
            },
            new FakeCarsBillingExcelParser
            {
                Result = CarsBillingParseResult.From([carsEntry], [], rowCount: 1, success: true)
            });

        var result = ExecuteAllFiles(useCase);

        Assert.True(result.Success);
        Assert.Same(supplierTransaction, Assert.Single(result.SupplierTransactions));
        Assert.Same(branchEntry, Assert.Single(result.BranchLitresEntries));
        Assert.Same(carsEntry, Assert.Single(result.CarsBillingEntries));
    }

    [Fact]
    public void Execute_preserves_counts_for_each_file()
    {
        var useCase = CreateUseCase(
            new FakeSupplierPdfParser
            {
                Result = SupplierPdfParseResult.From([CreateSupplierTransaction()], [], pageCount: 2, candidateRowCount: 3, success: true)
            },
            new FakeBranchLitresExcelParser
            {
                Result = BranchLitresParseResult.From([CreateBranchLitresEntry()], [], rowCount: 4, success: true)
            },
            new FakeCarsBillingExcelParser
            {
                Result = CarsBillingParseResult.From([CreateCarsBillingEntry()], [], rowCount: 5, success: true)
            });

        var result = ExecuteAllFiles(useCase);

        var supplier = result.Files.Single(file => file.InputSlot == InputSlot.SupplierStatement);
        var branch = result.Files.Single(file => file.InputSlot == InputSlot.BranchLitres);
        var cars = result.Files.Single(file => file.InputSlot == InputSlot.CarsBilling);

        Assert.Equal(3, supplier.RowCount);
        Assert.Equal(1, supplier.ValidRowCount);
        Assert.Equal(2, supplier.SkippedRowCount);
        Assert.Equal(4, branch.RowCount);
        Assert.Equal(1, branch.ValidRowCount);
        Assert.Equal(3, branch.SkippedRowCount);
        Assert.Equal(5, cars.RowCount);
        Assert.Equal(1, cars.ValidRowCount);
        Assert.Equal(4, cars.SkippedRowCount);
    }

    private static ImportValidationResult ExecuteAllFiles(ValidateImportBatchUseCase useCase) =>
        useCase.Execute(new ValidateImportBatchRequest(
            new FuelPeriod(2026, 4),
            "supplier.pdf",
            "branch.xlsx",
            "cars.xlsx",
            CreateBranchAliasResolver()));

    private static ValidateImportBatchUseCase CreateUseCase(
        FakeSupplierPdfParser? supplierParser = null,
        FakeBranchLitresExcelParser? branchParser = null,
        FakeCarsBillingExcelParser? carsParser = null) =>
        new(
            supplierParser ?? new FakeSupplierPdfParser(),
            branchParser ?? new FakeBranchLitresExcelParser(),
            carsParser ?? new FakeCarsBillingExcelParser());

    private static BranchAliasResolver CreateBranchAliasResolver()
    {
        var taupo = new BranchMaster(new CanonicalBranchId("TAUPO"), "Taupo");
        return new BranchAliasResolver([taupo], [new BranchAlias("Taupo", taupo.Id)]);
    }

    private static SupplierTransaction CreateSupplierTransaction() =>
        new(
            Guid.Parse("2af57cb1-2f6f-4ecb-9000-000000000001"),
            "Mobil",
            new FuelPeriod(2026, 4),
            new DateOnly(2026, 4, 1),
            new Litres(10m),
            new SourceReference("supplier.pdf", pageNumber: 1));

    private static BranchLitresEntry CreateBranchLitresEntry() =>
        new(
            Guid.Parse("2af57cb1-2f6f-4ecb-9000-000000000002"),
            new FuelPeriod(2026, 4),
            new CanonicalBranchId("TAUPO"),
            new DateOnly(2026, 4, 1),
            new Litres(10m),
            new SourceReference("branch.xlsx", sheetName: "Sheet1", rowNumber: 2));

    private static CarsBillingEntry CreateCarsBillingEntry() =>
        new(
            Guid.Parse("2af57cb1-2f6f-4ecb-9000-000000000003"),
            new FuelPeriod(2026, 4),
            new SourceReference("cars.xlsx", sheetName: "Export", rowNumber: 2),
            rentalAgreementNumber: new RentalAgreementNumber("RA-1"),
            billedAmount: new MoneyAmount(10m));

    private sealed class FakeSupplierPdfParser : ISupplierPdfParser
    {
        public SupplierPdfParseResult Result { get; init; } =
            SupplierPdfParseResult.From([CreateSupplierTransaction()], [], pageCount: 1, candidateRowCount: 1, success: true);

        public SupplierPdfParseResult Parse(string filePath, FuelPeriod period, BranchAliasResolver branchAliasResolver) => Result;
    }

    private sealed class FakeBranchLitresExcelParser : IBranchLitresExcelParser
    {
        public BranchLitresParseResult Result { get; init; } =
            BranchLitresParseResult.From([CreateBranchLitresEntry()], [], rowCount: 1, success: true);

        public BranchLitresParseResult Parse(string filePath, FuelPeriod period, BranchAliasResolver branchAliasResolver) => Result;
    }

    private sealed class FakeCarsBillingExcelParser : ICarsBillingExcelParser
    {
        public CarsBillingParseResult Result { get; init; } =
            CarsBillingParseResult.From([CreateCarsBillingEntry()], [], rowCount: 1, success: true);

        public CarsBillingParseResult Parse(string filePath, FuelPeriod period, BranchAliasResolver branchAliasResolver) => Result;
    }
}
