using Microsoft.Data.Sqlite;

namespace FuelRecon.Infrastructure.Persistence;

public static class SqliteSchemaMigrator
{
    public const string InitialSchemaMigrationId = "001_initial_schema";

    public static void ApplyInitialSchema(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        SqlitePragmas.EnableForeignKeys(connection);

        using var transaction = connection.BeginTransaction();

        using var migrationTableCommand = connection.CreateCommand();
        migrationTableCommand.Transaction = transaction;
        migrationTableCommand.CommandText = """
            CREATE TABLE IF NOT EXISTS SchemaMigrations (
                Id TEXT PRIMARY KEY,
                AppliedAtUtc TEXT NOT NULL
            );
            """;
        migrationTableCommand.ExecuteNonQuery();

        if (!HasMigration(connection, transaction, InitialSchemaMigrationId))
        {
            using var schemaCommand = connection.CreateCommand();
            schemaCommand.Transaction = transaction;
            schemaCommand.CommandText = SqliteSchema.InitialSchema;
            schemaCommand.ExecuteNonQuery();

            using var insertMigrationCommand = connection.CreateCommand();
            insertMigrationCommand.Transaction = transaction;
            insertMigrationCommand.CommandText = """
                INSERT INTO SchemaMigrations (
                    Id,
                    AppliedAtUtc
                )
                VALUES (
                    $id,
                    $appliedAtUtc
                );
                """;
            insertMigrationCommand.Parameters.AddWithValue("$id", InitialSchemaMigrationId);
            insertMigrationCommand.Parameters.AddWithValue("$appliedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
            insertMigrationCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public static void ApplyInitialSchema(SqliteConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);

        using var connection = connectionFactory.OpenConnection();
        ApplyInitialSchema(connection);
    }

    private static bool HasMigration(SqliteConnection connection, SqliteTransaction transaction, string migrationId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM SchemaMigrations
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", migrationId);

        var count = (long)command.ExecuteScalar()!;
        return count > 0;
    }
}