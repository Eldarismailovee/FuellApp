using FuelRecon.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace FuelRecon.Tests;

public class SqliteSchemaMigratorTests
{
    [Fact]
    public void Connection_factory_opens_connection_and_enables_foreign_keys()
    {
        var factory = new SqliteConnectionFactory("Data Source=:memory:", isConnectionString: true);

        using var connection = factory.OpenConnection();

        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
        Assert.True(ForeignKeysAreEnabled(connection));
    }

    [Fact]
    public void Migrator_applies_initial_schema_to_empty_in_memory_database()
    {
        var factory = new SqliteConnectionFactory("Data Source=:memory:", isConnectionString: true);

        using var connection = factory.OpenConnection();
        SqliteSchemaMigrator.ApplyInitialSchema(connection);

        Assert.True(TableExists(connection, "Periods"));
        Assert.True(TableExists(connection, "ReconciliationRuns"));
        Assert.True(TableExists(connection, "SchemaMigrations"));
        Assert.Equal(1, GetMigrationCount(connection));
    }

    [Fact]
    public void Migrator_can_be_applied_twice_without_failing_or_duplicating_migration_record()
    {
        var factory = new SqliteConnectionFactory("Data Source=:memory:", isConnectionString: true);

        using var connection = factory.OpenConnection();
        SqliteSchemaMigrator.ApplyInitialSchema(connection);
        SqliteSchemaMigrator.ApplyInitialSchema(connection);

        Assert.True(TableExists(connection, "Periods"));
        Assert.Equal(1, GetMigrationCount(connection));
    }

    [Fact]
    public void Migrated_database_enforces_foreign_keys()
    {
        var factory = new SqliteConnectionFactory("Data Source=:memory:", isConnectionString: true);

        using var connection = factory.OpenConnection();
        SqliteSchemaMigrator.ApplyInitialSchema(connection);

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ImportedFiles (
                Id,
                ImportBatchId,
                PeriodId,
                InputSlot,
                OriginalFileName,
                FileStatus,
                ChecksumAlgorithm,
                ChecksumValue,
                ImportedAtUtc
            )
            VALUES (
                'file-1',
                'missing-batch',
                'missing-period',
                'SupplierStatement',
                'supplier.pdf',
                'Imported',
                'SHA256',
                'abc123',
                '2026-05-15T00:00:00Z'
            );
            """;

        var exception = Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());

        Assert.Equal(19, exception.SqliteErrorCode);
    }

    [Fact]
    public void Migrator_initialises_temporary_file_database_from_database_path()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"fuelrecon-{Guid.NewGuid():N}.db");

        try
        {
            var factory = new SqliteConnectionFactory(databasePath);
            SqliteSchemaMigrator.ApplyInitialSchema(factory);

            using var connection = factory.OpenConnection();

            Assert.True(TableExists(connection, "Periods"));
            Assert.True(TableExists(connection, "PdfExports"));
            Assert.True(ForeignKeysAreEnabled(connection));
            Assert.Equal(1, GetMigrationCount(connection));
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Connection_factory_rejects_empty_database_path_or_connection_string(string value)
    {
        Assert.Throws<ArgumentException>(() => new SqliteConnectionFactory(value));
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name = $name;
            """;
        command.Parameters.AddWithValue("$name", tableName);

        return (long)command.ExecuteScalar()! == 1;
    }

    private static int GetMigrationCount(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM SchemaMigrations
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", SqliteSchemaMigrator.InitialSchemaMigrationId);

        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static bool ForeignKeysAreEnabled(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys;";

        return (long)command.ExecuteScalar()! == 1;
    }
}
