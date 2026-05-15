using Microsoft.Data.Sqlite;

namespace FuelRecon.Infrastructure.Persistence;

public static class SqliteSchemaMigrator
{
    public const string InitialSchemaMigrationId = "001_initial_schema";

    public const string Migration002BranchReportNotesAndApprovalsId = "002_branch_report_notes_and_approvals";

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

            InsertMigrationRow(connection, transaction, InitialSchemaMigrationId);
        }

        if (!HasMigration(connection, transaction, Migration002BranchReportNotesAndApprovalsId))
        {
            using var migrationCommand = connection.CreateCommand();
            migrationCommand.Transaction = transaction;
            migrationCommand.CommandText = SqliteSchema.Migration002BranchReportNotesAndApprovals;
            migrationCommand.ExecuteNonQuery();

            InsertMigrationRow(connection, transaction, Migration002BranchReportNotesAndApprovalsId);
        }

        transaction.Commit();
    }

    public static void ApplyInitialSchema(SqliteConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);

        using var connection = connectionFactory.OpenConnection();
        ApplyInitialSchema(connection);
    }

    private static void InsertMigrationRow(SqliteConnection connection, SqliteTransaction transaction, string migrationId)
    {
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
        insertMigrationCommand.Parameters.AddWithValue("$id", migrationId);
        insertMigrationCommand.Parameters.AddWithValue("$appliedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
        insertMigrationCommand.ExecuteNonQuery();
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