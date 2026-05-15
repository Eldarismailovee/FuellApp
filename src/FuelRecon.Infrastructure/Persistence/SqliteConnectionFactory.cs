using Microsoft.Data.Sqlite;

namespace FuelRecon.Infrastructure.Persistence;

/// <summary>
/// Opens SQLite connections for local app data or tests and enables foreign-key enforcement.
/// </summary>
public sealed class SqliteConnectionFactory
{
    private readonly string connectionString;

    public SqliteConnectionFactory(string databasePathOrConnectionString, bool isConnectionString = false)
    {
        if (string.IsNullOrWhiteSpace(databasePathOrConnectionString))
        {
            throw new ArgumentException("Database path or connection string cannot be empty.", nameof(databasePathOrConnectionString));
        }

        connectionString = isConnectionString
            ? databasePathOrConnectionString
            : new SqliteConnectionStringBuilder { DataSource = databasePathOrConnectionString }.ToString();
    }

    public string ConnectionString => connectionString;

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        SqlitePragmas.EnableForeignKeys(connection);
        return connection;
    }
}

internal static class SqlitePragmas
{
    internal static void EnableForeignKeys(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();
    }
}
