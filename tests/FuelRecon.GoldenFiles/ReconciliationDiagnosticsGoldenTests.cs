using FuelRecon.Application.Diagnostics;
using FuelRecon.Application.Processing;
using FuelRecon.Application.Reconciliation;
using FuelRecon.Application.Validation;
using FuelRecon.Domain;
using FuelRecon.Infrastructure.Excel;
using FuelRecon.Infrastructure.Pdf;
using FuelRecon.Infrastructure.Persistence;

namespace FuelRecon.GoldenFiles;

public class ReconciliationDiagnosticsGoldenTests
{
    [Fact]
    public void Write_reconciliation_diagnostics_for_available_real_sample_files()
    {
        var repositoryRoot = FindRepositoryRoot();
        var samples = LocateSamples(repositoryRoot);
        if (samples is null)
        {
            return;
        }

        using var database = TemporarySqliteDatabase.Create();
        var useCase = CreateUseCase(database.ConnectionFactory);
        var period = new FuelPeriod(2026, 4);

        var result = useCase.Execute(new ProcessFuelReconciliationRequest(
            period,
            samples.SupplierPdfPath,
            samples.BranchLitresPath,
            samples.CarsBillingPath,
            CreateBranchAliasResolver(),
            ImportedBy: "golden-files-test",
            RunBy: "golden-files-test",
            FileChecksums: new Dictionary<InputSlot, FileChecksum>
            {
                [InputSlot.SupplierStatement] = new("TEST", "supplier"),
                [InputSlot.BranchLitres] = new("TEST", "branch"),
                [InputSlot.CarsBilling] = new("TEST", "cars"),
            },
            ReconciliationRules: new ReconciliationRulesOptions(RunCreatedAtUtc: new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero)),
            ImportedAtUtc: new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero)));

        Assert.True(result.Success);
        Assert.NotNull(result.ReconciliationEngineResult);

        var outputDirectory = ReconciliationDiagnosticsWriter.Write(
            result,
            repositoryRoot,
            samples.SupplierPdfPath,
            period);

        Assert.True(Directory.Exists(outputDirectory));

        foreach (var fileName in ReconciliationDiagnosticsWriter.ExpectedRelativeFileNames)
        {
            var path = Path.Combine(outputDirectory, fileName);
            Assert.True(File.Exists(path), $"Expected diagnostic file '{fileName}' at '{path}'.");
            Assert.True(new FileInfo(path).Length > 0, $"Diagnostic file '{fileName}' should not be empty.");
        }
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

    private static SamplePaths? LocateSamples(string repositoryRoot)
    {
        var sampleRoot = Path.Combine(repositoryRoot, "samples", "client-raw");
        var supplierPdf = FirstExisting(
            Path.Combine(sampleRoot, "Mobile - Taupo.pdf"),
            Path.Combine(sampleRoot, "Farmlands Statement April.PDF"));
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

    /// <summary>
    /// Matches CLI defaults so diagnostics reflect the same reconciliation behaviour as <c>FuelRecon.Cli</c>.
    /// </summary>
    private static BranchAliasResolver CreateBranchAliasResolver()
    {
        var taupo = new BranchMaster(new CanonicalBranchId("TAUPO"), "Taupo");
        var kerikeri = new BranchMaster(new CanonicalBranchId("KERIKERI"), "Kerikeri");
        var whangarei = new BranchMaster(new CanonicalBranchId("WHANGAREI"), "Whangarei");
        var rotorua = new BranchMaster(new CanonicalBranchId("ROTORUA"), "Rotorua");
        var mtMaunganui = new BranchMaster(new CanonicalBranchId("MT-MAUNGANUI"), "Mt Maunganui");

        return new BranchAliasResolver(
            [taupo, kerikeri, whangarei, rotorua, mtMaunganui],
            [
                new BranchAlias("Taupo", taupo.Id),
                new BranchAlias("Mobil Taupo", taupo.Id),
                new BranchAlias("Mobile - Taupo", taupo.Id),
                new BranchAlias("Hertz Taupo", taupo.Id),
                new BranchAlias("HERTZ TAUPO 1", taupo.Id),
                new BranchAlias("HERTZ TAUPO 2", taupo.Id),
                new BranchAlias("TAUPO 1", taupo.Id),
                new BranchAlias("TAUPO 2", taupo.Id),
                new BranchAlias("HERTZ BOP", taupo.Id),

                new BranchAlias("Kerikeri", kerikeri.Id),
                new BranchAlias("Caltex Kerikeri", kerikeri.Id),

                new BranchAlias("Whangarei", whangarei.Id),
                new BranchAlias("Caltex Whangarei", whangarei.Id),

                new BranchAlias("Caltex Te Ngae", rotorua.Id),
                new BranchAlias("Rotorua", rotorua.Id),
                new BranchAlias("Z Hewletts Rd", mtMaunganui.Id),
                new BranchAlias("Mt Maunganui", mtMaunganui.Id),
            ]);
    }

    private sealed record SamplePaths(string SupplierPdfPath, string BranchLitresPath, string CarsBillingPath);

    private static string FindRepositoryRoot()
    {
        var candidates = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
        };

        foreach (var candidate in candidates)
        {
            var directory = new DirectoryInfo(candidate);

            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "FuelRecon.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing FuelRecon.slnx.");
    }

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
            var databasePath = Path.Combine(Path.GetTempPath(), $"fuelrecon-golden-{Guid.NewGuid():N}.db");
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
