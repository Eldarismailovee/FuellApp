using FuelRecon.Application.PeriodLifecycle;
using FuelRecon.Application.Persistence;
using FuelRecon.Domain;
using FuelRecon.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace FuelRecon.Tests;

public class SqlitePeriodLifecycleRepositoryTests
{
    private static readonly FuelPeriod Period = new(2026, 7);

    [Fact]
    public void Period_lifecycle_transitions_persist_and_append_audit_records()
    {
        using var database = LifecycleTestDatabase.Create();
        var periodRepo = new SqlitePeriodLifecycleRepository(database.ConnectionFactory);
        var auditsRepo = new SqliteAuditRepository(database.ConnectionFactory);
        var useCase = new TransitionPeriodLifecycleUseCase(periodRepo, auditsRepo);

        var t = new DateTimeOffset(2026, 7, 2, 9, 0, 0, TimeSpan.Zero);

        useCase.Execute(step(t, PeriodLifecycleStatus.FilesImported));
        useCase.Execute(step(t.AddMinutes(1), PeriodLifecycleStatus.Validated));
        useCase.Execute(step(t.AddMinutes(2), PeriodLifecycleStatus.Reconciled));
        useCase.Execute(step(t.AddMinutes(3), PeriodLifecycleStatus.Reviewed));
        useCase.Execute(step(t.AddMinutes(4), PeriodLifecycleStatus.Approved));
        useCase.Execute(step(t.AddMinutes(5), PeriodLifecycleStatus.Closed));

        useCase.Execute(
            new TransitionPeriodLifecycleRequest(
                Period,
                PeriodLifecycleStatus.Reopened,
                "bob",
                t.AddMinutes(6),
                Note: "Management authorised rework for missed invoices."));

        useCase.Execute(step(t.AddMinutes(7), PeriodLifecycleStatus.Draft));

        var row = periodRepo.Get(Period);
        Assert.NotNull(row);
        Assert.Equal(PeriodLifecycleStatus.Draft, row!.LifecycleStatus);
        Assert.Equal(t.AddMinutes(5), row.ClosedAtUtc);
        Assert.Null(row.ReopenedAtUtc);
        Assert.Null(row.ReopenReason);

        var audits = auditsRepo.ListByEntity(AuditEntityType.Period, Period.ToSortableString());
        Assert.Equal(8, audits.Count);
        Assert.All(audits, a => Assert.Equal(AuditActionType.LifecycleTransition, a.ActionType));
        Assert.All(audits, a => Assert.Equal(PeriodLifecycleAuditReasonCodes.TransitionApplied, a.ReasonCode));
        Assert.Contains(
            audits,
            a => a.OldValuesJson != null
                && a.NewValuesJson != null
                && a.NewValuesJson.Contains("\"reopenReason\"", StringComparison.Ordinal)
                && a.Note != null
                && a.Note.Contains("Management authorised", StringComparison.Ordinal));
    }

    [Fact]
    public void Invalid_transition_does_not_update_period_or_write_audit()
    {
        using var database = LifecycleTestDatabase.Create();
        var periodRepo = new SqlitePeriodLifecycleRepository(database.ConnectionFactory);
        var auditsRepo = new SqliteAuditRepository(database.ConnectionFactory);
        var useCase = new TransitionPeriodLifecycleUseCase(periodRepo, auditsRepo);

        var t = new DateTimeOffset(2026, 7, 3, 10, 0, 0, TimeSpan.Zero);
        useCase.Execute(new TransitionPeriodLifecycleRequest(Period, PeriodLifecycleStatus.FilesImported, "alice", t));

        Assert.Throws<ArgumentException>(() =>
            useCase.Execute(new TransitionPeriodLifecycleRequest(Period, PeriodLifecycleStatus.Closed, "alice", t.AddMinutes(1))));

        var row = periodRepo.Get(Period);
        Assert.NotNull(row);
        Assert.Equal(PeriodLifecycleStatus.FilesImported, row!.LifecycleStatus);

        var audits = auditsRepo.ListByEntity(AuditEntityType.Period, Period.ToSortableString());
        Assert.Single(audits);
    }

    private static TransitionPeriodLifecycleRequest step(DateTimeOffset at, PeriodLifecycleStatus target) =>
        new(Period, target, "alice", at);

    private sealed class LifecycleTestDatabase : IDisposable
    {
        private readonly SqliteConnection rootConnection;

        private LifecycleTestDatabase(SqliteConnectionFactory connectionFactory, SqliteConnection rootConnection)
        {
            ConnectionFactory = connectionFactory;
            this.rootConnection = rootConnection;
        }

        public SqliteConnectionFactory ConnectionFactory { get; }

        public static LifecycleTestDatabase Create()
        {
            var connectionString = $"Data Source=LifecycleTests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var factory = new SqliteConnectionFactory(connectionString, isConnectionString: true);
            var rootConnection = factory.OpenConnection();
            SqliteSchemaMigrator.ApplyInitialSchema(rootConnection);
            return new LifecycleTestDatabase(factory, rootConnection);
        }

        public void Dispose() => rootConnection.Dispose();
    }
}
