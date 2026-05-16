using System.Globalization;
using FuelRecon.Application.Persistence;
using FuelRecon.Domain;
using Microsoft.Data.Sqlite;

namespace FuelRecon.Infrastructure.Persistence;

public sealed class SqlitePeriodLifecycleRepository(SqliteConnectionFactory connectionFactory) : IPeriodLifecycleRepository
{
    public PeriodLifecycleRecord? Get(FuelPeriod period)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Year, Month, LifecycleStatus, CreatedAtUtc, UpdatedAtUtc, ClosedAtUtc, ReopenedAtUtc, ReopenReason
            FROM Periods
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", SqliteRepositoryHelpers.ToPeriodId(period));
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public void EnsurePeriod(FuelPeriod period, DateTimeOffset createdAtUtc)
    {
        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO Periods (
                Id,
                Year,
                Month,
                LifecycleStatus,
                CreatedAtUtc
            )
            VALUES (
                $id,
                $year,
                $month,
                $lifecycleStatus,
                $createdAtUtc
            );
            """;
        command.Parameters.AddWithValue("$id", SqliteRepositoryHelpers.ToPeriodId(period));
        command.Parameters.AddWithValue("$year", period.Year);
        command.Parameters.AddWithValue("$month", period.Month);
        command.Parameters.AddWithValue("$lifecycleStatus", PeriodLifecycleStatus.Draft.ToString());
        command.Parameters.AddWithValue("$createdAtUtc", SqliteRepositoryHelpers.ToIsoString(createdAtUtc));
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public void Save(PeriodLifecycleRecord record)
    {
        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE Periods SET
                LifecycleStatus = $lifecycleStatus,
                UpdatedAtUtc = $updatedAtUtc,
                ClosedAtUtc = $closedAtUtc,
                ReopenedAtUtc = $reopenedAtUtc,
                ReopenReason = $reopenReason
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", SqliteRepositoryHelpers.ToPeriodId(record.Period));
        command.Parameters.AddWithValue("$lifecycleStatus", record.LifecycleStatus.ToString());
        command.Parameters.AddWithValue("$updatedAtUtc", SqliteRepositoryHelpers.ToIsoString(record.UpdatedAtUtc!.Value));
        command.Parameters.AddWithNullableValue("$closedAtUtc", record.ClosedAtUtc is null ? null : SqliteRepositoryHelpers.ToIsoString(record.ClosedAtUtc.Value));
        command.Parameters.AddWithNullableValue("$reopenedAtUtc", record.ReopenedAtUtc is null ? null : SqliteRepositoryHelpers.ToIsoString(record.ReopenedAtUtc.Value));
        command.Parameters.AddWithNullableValue("$reopenReason", record.ReopenReason);
        var affected = command.ExecuteNonQuery();
        if (affected != 1)
        {
            throw new InvalidOperationException(
                $"Expected to update period '{record.Period.ToSortableString()}', but no row matched.");
        }

        transaction.Commit();
    }

    private static PeriodLifecycleRecord Read(SqliteDataReader reader)
    {
        var year = reader.GetInt32(0);
        var month = reader.GetInt32(1);
        var period = new FuelPeriod(year, month);
        return new PeriodLifecycleRecord(
            period,
            Enum.Parse<PeriodLifecycleStatus>(reader.GetString(2)),
            DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.IsDBNull(4)
                ? null
                : DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.IsDBNull(5)
                ? null
                : DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.IsDBNull(6)
                ? null
                : DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.GetNullableString(7));
    }
}
