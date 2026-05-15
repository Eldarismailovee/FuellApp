using FuelRecon.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace FuelRecon.Tests;

public class SqliteSchemaTests
{
    [Fact]
    public void Initial_schema_applies_to_empty_in_memory_database()
    {
        using var connection = CreateDatabase();

        var tables = GetUserTableNames(connection);

        Assert.Contains("Periods", tables);
        Assert.Contains("ImportBatches", tables);
        Assert.Contains("ImportedFiles", tables);
        Assert.Contains("ValidationResults", tables);
        Assert.Contains("SupplierTransactions", tables);
        Assert.Contains("BranchLitresEntries", tables);
        Assert.Contains("CarsBillingEntries", tables);
        Assert.Contains("ReconciliationRuns", tables);
        Assert.Contains("ReconciliationItems", tables);
        Assert.Contains("ManualActions", tables);
        Assert.Contains("BranchReports", tables);
        Assert.Contains("PdfExports", tables);
        Assert.Contains("AuditRecords", tables);
        Assert.Contains("SettingsSnapshots", tables);
    }

    [Fact]
    public void Initial_schema_contains_expected_source_value_and_status_columns()
    {
        using var connection = CreateDatabase();

        AssertTableHasColumns(
            connection,
            "BranchLitresEntries",
            "RawRentalAgreementNumber",
            "NormalisedRentalAgreementNumber",
            "RawRego",
            "NormalisedRego",
            "SourceFile",
            "SourceRowNumber",
            "ValidationIssueCodes");

        AssertTableHasColumns(
            connection,
            "ReconciliationItems",
            "SystemStatus",
            "ResolutionStatus",
            "FinalStatus",
            "ReasonCodes",
            "MatchCandidatesJson",
            "SupplierSourceFile",
            "BranchSourceFile",
            "CarsSourceFile");

        AssertTableHasColumns(
            connection,
            "PdfExports",
            "Status",
            "TemplateName",
            "TemplateVersion",
            "ErrorCategory",
            "CorrelationId",
            "ExportSettingsSnapshot");
    }

    [Theory]
    [InlineData("ImportBatches", "PeriodId", "Periods")]
    [InlineData("ImportedFiles", "ImportBatchId", "ImportBatches")]
    [InlineData("ImportedFiles", "PeriodId", "Periods")]
    [InlineData("ValidationResults", "ImportedFileId", "ImportedFiles")]
    [InlineData("SupplierTransactions", "ImportedFileId", "ImportedFiles")]
    [InlineData("SupplierTransactions", "PeriodId", "Periods")]
    [InlineData("BranchLitresEntries", "ImportedFileId", "ImportedFiles")]
    [InlineData("CarsBillingEntries", "ImportedFileId", "ImportedFiles")]
    [InlineData("ReconciliationRuns", "PeriodId", "Periods")]
    [InlineData("ReconciliationRuns", "SettingsSnapshotId", "SettingsSnapshots")]
    [InlineData("ReconciliationItems", "RunId", "ReconciliationRuns")]
    [InlineData("ReconciliationItems", "SupplierTransactionId", "SupplierTransactions")]
    [InlineData("ReconciliationItems", "BranchLitresEntryId", "BranchLitresEntries")]
    [InlineData("ReconciliationItems", "CarsBillingEntryId", "CarsBillingEntries")]
    [InlineData("ManualActions", "ReconciliationItemId", "ReconciliationItems")]
    [InlineData("BranchReports", "RunId", "ReconciliationRuns")]
    [InlineData("PdfExports", "BranchReportId", "BranchReports")]
    public void Initial_schema_contains_practical_foreign_keys(string tableName, string fromColumn, string referencedTable)
    {
        using var connection = CreateDatabase();

        var foreignKeys = GetForeignKeys(connection, tableName);

        Assert.Contains(foreignKeys, foreignKey =>
            foreignKey.FromColumn == fromColumn && foreignKey.ReferencedTable == referencedTable);
    }

    [Fact]
    public void Initial_schema_enforces_foreign_keys()
    {
        using var connection = CreateDatabase();

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ImportBatches (
                Id,
                PeriodId,
                CreatedAtUtc,
                CreatedBy,
                Status
            )
            VALUES (
                'batch-1',
                'missing-period',
                '2026-05-15T00:00:00Z',
                'arina',
                'Created'
            );
            """;

        var exception = Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());

        Assert.Equal(19, exception.SqliteErrorCode);
    }

    [Theory]
    [InlineData("TR_ReconciliationRuns_PreventUpdate")]
    [InlineData("TR_ReconciliationRuns_PreventDelete")]
    [InlineData("TR_BranchReports_PreventUpdate")]
    [InlineData("TR_BranchReports_PreventDelete")]
    [InlineData("TR_PdfExports_PreventUpdate")]
    [InlineData("TR_PdfExports_PreventDelete")]
    [InlineData("TR_AuditRecords_PreventUpdate")]
    [InlineData("TR_AuditRecords_PreventDelete")]
    public void Initial_schema_contains_append_only_triggers(string triggerName)
    {
        using var connection = CreateDatabase();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'trigger'
              AND name = $name;
            """;
        command.Parameters.AddWithValue("$name", triggerName);

        var count = (long)command.ExecuteScalar()!;

        Assert.Equal(1, count);
    }

    private static SqliteConnection CreateDatabase()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using var pragmaCommand = connection.CreateCommand();
        pragmaCommand.CommandText = "PRAGMA foreign_keys = ON;";
        pragmaCommand.ExecuteNonQuery();

        using var schemaCommand = connection.CreateCommand();
        schemaCommand.CommandText = SqliteSchema.InitialSchema;
        schemaCommand.ExecuteNonQuery();

        return connection;
    }

    private static HashSet<string> GetUserTableNames(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table'
              AND name NOT LIKE 'sqlite_%';
            """;

        using var reader = command.ExecuteReader();
        var tableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (reader.Read())
        {
            tableNames.Add(reader.GetString(0));
        }

        return tableNames;
    }

    private static void AssertTableHasColumns(SqliteConnection connection, string tableName, params string[] expectedColumnNames)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";

        using var reader = command.ExecuteReader();
        var columnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (reader.Read())
        {
            columnNames.Add(reader.GetString(1));
        }

        foreach (var expectedColumnName in expectedColumnNames)
        {
            Assert.Contains(expectedColumnName, columnNames);
        }
    }

    private static IReadOnlyList<ForeignKeyInfo> GetForeignKeys(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA foreign_key_list({tableName});";

        using var reader = command.ExecuteReader();
        var foreignKeys = new List<ForeignKeyInfo>();

        while (reader.Read())
        {
            foreignKeys.Add(new ForeignKeyInfo(
                ReferencedTable: reader.GetString(2),
                FromColumn: reader.GetString(3)));
        }

        return foreignKeys;
    }

    private sealed record ForeignKeyInfo(string ReferencedTable, string FromColumn);
}
