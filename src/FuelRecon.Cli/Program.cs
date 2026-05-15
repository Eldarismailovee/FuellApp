using FuelRecon.Application.Excel;
using FuelRecon.Application.Processing;
using FuelRecon.Application.Reconciliation;
using FuelRecon.Application.Validation;
using FuelRecon.Domain;
using FuelRecon.Infrastructure.Excel;
using FuelRecon.Infrastructure.Pdf;
using FuelRecon.Infrastructure.Persistence;

var parseResult = CliArguments.Parse(args);
if (!parseResult.Success || parseResult.Options is null)
{
    Console.Error.WriteLine(parseResult.Message);
    Console.Error.WriteLine("Usage: FuelRecon.Cli --period 2026-04 --supplier path.pdf --branch-litres path.xlsx --cars path.xlsx --db fuelrecon.db");
    return 2;
}

try
{
    var options = parseResult.Options;
    var connectionFactory = new SqliteConnectionFactory(options.DatabasePath);
    SqliteSchemaMigrator.ApplyInitialSchema(connectionFactory);

    var useCase = CreateUseCase(connectionFactory);
    var result = useCase.Execute(new ProcessFuelReconciliationRequest(
        options.Period,
        options.SupplierPdfPath,
        options.BranchLitresExcelPath,
        options.CarsBillingExcelPath,
        CreateDefaultBranchAliasResolver(),
        ImportedBy: Environment.UserName,
        RunBy: Environment.UserName,
        ImportedAtUtc: DateTimeOffset.UtcNow,
        ReconciliationRules: new ReconciliationRulesOptions(RunCreatedAtUtc: DateTimeOffset.UtcNow)));

    PrintSummary(result, options.DatabasePath);

    return result.Success ? 0 : 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Fuel reconciliation failed: {exception.Message}");
    return 1;
}

static ProcessFuelReconciliationUseCase CreateUseCase(SqliteConnectionFactory connectionFactory)
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

static void PrintSummary(ProcessFuelReconciliationResult result, string databasePath)
{
    Console.WriteLine($"Import success: {result.ImportValidationResult.Success}");
    Console.WriteLine("Files:");
    foreach (var file in result.ImportValidationResult.Files.OrderBy(file => file.InputSlot))
    {
        Console.WriteLine($"  {file.InputSlot}: status={file.Status}, rows={file.RowCount}, valid={file.ValidRowCount}, skipped={file.SkippedRowCount}");
        if (file.Issues.Count == 0)
        {
            Console.WriteLine("    Issues: none");
            continue;
        }

        Console.WriteLine("    Issues:");
        foreach (var issue in file.Issues)
        {
            var sourceReference = issue.SourceReference is null
                ? string.Empty
                : $" | source={issue.SourceReference}";

            Console.WriteLine($"      [{issue.Severity}] {issue.ReasonCode}: {issue.Message}{sourceReference}");
        }
    }

    Console.WriteLine($"Supplier rows: {result.ImportValidationResult.SupplierTransactions.Count}");
    Console.WriteLine($"Branch litres rows: {result.ImportValidationResult.BranchLitresEntries.Count}");
    Console.WriteLine($"Cars+ rows: {result.ImportValidationResult.CarsBillingEntries.Count}");
    Console.WriteLine($"Import batch id: {result.ImportPersistenceResponse.ImportBatchId}");
    Console.WriteLine($"Reconciliation ran: {result.ReconciliationEngineResult is not null}");

    if (result.ReconciliationEngineResult is not null)
    {
        Console.WriteLine($"Reconciliation run id: {result.ReconciliationEngineResult.Run.Id}");
        Console.WriteLine($"Reconciliation item count: {result.ReconciliationEngineResult.Items.Count}");
    }

    Console.WriteLine($"Database path: {databasePath}");
}

static BranchAliasResolver CreateDefaultBranchAliasResolver()
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
            new BranchAlias("Caltex Kerikeri", kerikeri.Id),
            new BranchAlias("Caltex Whangarei", whangarei.Id),
            new BranchAlias("Caltex Te Ngae", rotorua.Id),
            new BranchAlias("Rotorua", rotorua.Id),
            new BranchAlias("Z Hewletts Rd", mtMaunganui.Id),
            new BranchAlias("Mt Maunganui", mtMaunganui.Id),
        ]);
}

internal sealed record CliOptions(
    FuelPeriod Period,
    string SupplierPdfPath,
    string BranchLitresExcelPath,
    string CarsBillingExcelPath,
    string DatabasePath);

internal sealed record CliParseResult(bool Success, CliOptions? Options, string Message);

internal static class CliArguments
{
    private static readonly string[] RequiredKeys = ["--period", "--supplier", "--branch-litres", "--cars", "--db"];

    public static CliParseResult Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < args.Length; index++)
        {
            var key = args[index];
            if (!key.StartsWith("--", StringComparison.Ordinal))
            {
                return new CliParseResult(false, null, $"Unexpected argument '{key}'.");
            }

            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return new CliParseResult(false, null, $"Missing value for argument '{key}'.");
            }

            values[key] = args[++index];
        }

        foreach (var requiredKey in RequiredKeys)
        {
            if (!values.ContainsKey(requiredKey))
            {
                return new CliParseResult(false, null, $"Missing required argument '{requiredKey}'.");
            }
        }

        if (!FuelPeriod.TryParse(values["--period"], out var period))
        {
            return new CliParseResult(false, null, "Period must be in a supported format such as 2026-04.");
        }

        return new CliParseResult(
            true,
            new CliOptions(
                period,
                values["--supplier"],
                values["--branch-litres"],
                values["--cars"],
                values["--db"]),
            "OK");
    }
}
