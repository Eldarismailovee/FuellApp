using FuelRecon.Application.PeriodLifecycle;
using FuelRecon.Application.Persistence;
using FuelRecon.Domain;

namespace FuelRecon.Tests;

public class TransitionPeriodLifecycleUseCaseTests
{
    private static readonly FuelPeriod Period = new(2026, 6);

    private static readonly DateTimeOffset T0 = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Execute_advances_Draft_to_FilesImported_and_audits()
    {
        var periodRepo = new FakePeriodLifecycleRepository();
        var audits = new CaptureAuditRepository();
        var useCase = new TransitionPeriodLifecycleUseCase(periodRepo, audits);

        var response = useCase.Execute(
            new TransitionPeriodLifecycleRequest(Period, PeriodLifecycleStatus.FilesImported, "alice", T0, Note: "Imported supplier PDFs."));

        Assert.Equal(PeriodLifecycleStatus.FilesImported, response.Period.LifecycleStatus);
        Assert.Single(periodRepo.Saves);
        Assert.Single(audits.Saved);
        Assert.Equal(AuditActionType.LifecycleTransition, audits.Saved[0].ActionType);
        Assert.Equal(AuditEntityType.Period, audits.Saved[0].EntityType);
        Assert.Equal(Period.ToSortableString(), audits.Saved[0].EntityId);
        Assert.Equal(PeriodLifecycleAuditReasonCodes.TransitionApplied, audits.Saved[0].ReasonCode);
        Assert.Contains("\"lifecycleStatus\":\"Draft\"", audits.Saved[0].OldValuesJson, StringComparison.Ordinal);
        Assert.Contains("\"lifecycleStatus\":\"FilesImported\"", audits.Saved[0].NewValuesJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_requires_note_when_reopening_closed_period()
    {
        var periodRepo = new FakePeriodLifecycleRepository();
        periodRepo.Seed(
            new PeriodLifecycleRecord(Period, PeriodLifecycleStatus.Closed, T0, T0, T0, null, null));
        var audits = new CaptureAuditRepository();
        var useCase = new TransitionPeriodLifecycleUseCase(periodRepo, audits);

        var exception = Assert.Throws<ArgumentException>(() =>
            useCase.Execute(new TransitionPeriodLifecycleRequest(Period, PeriodLifecycleStatus.Reopened, "alice", T0)));

        Assert.Contains(PeriodLifecycleAuditReasonCodes.ReopenReasonRequired, exception.Message, StringComparison.Ordinal);
        Assert.Empty(audits.Saved);
        Assert.Empty(periodRepo.Saves);
    }

    [Fact]
    public void Execute_blocks_invalid_transition_before_persisting()
    {
        var periodRepo = new FakePeriodLifecycleRepository();
        periodRepo.Seed(
            new PeriodLifecycleRecord(Period, PeriodLifecycleStatus.Draft, T0, null, null, null, null));
        var audits = new CaptureAuditRepository();
        var useCase = new TransitionPeriodLifecycleUseCase(periodRepo, audits);

        Assert.Throws<ArgumentException>(() =>
            useCase.Execute(new TransitionPeriodLifecycleRequest(Period, PeriodLifecycleStatus.Closed, "alice", T0)));

        Assert.Empty(audits.Saved);
        Assert.Empty(periodRepo.Saves);
    }

    private sealed class FakePeriodLifecycleRepository : IPeriodLifecycleRepository
    {
        private PeriodLifecycleRecord? _record;

        public List<PeriodLifecycleRecord> Saves { get; } = [];

        public void Seed(PeriodLifecycleRecord record) => _record = record;

        public PeriodLifecycleRecord? Get(FuelPeriod period) =>
            period.Equals(Period) ? _record : throw new InvalidOperationException("Unexpected period.");

        public void EnsurePeriod(FuelPeriod period, DateTimeOffset createdAtUtc)
        {
            if (!period.Equals(Period))
            {
                throw new InvalidOperationException("Unexpected period.");
            }

            _record ??= new PeriodLifecycleRecord(period, PeriodLifecycleStatus.Draft, createdAtUtc, null, null, null, null);
        }

        public void Save(PeriodLifecycleRecord record)
        {
            Saves.Add(record);
            _record = record;
        }
    }

    private sealed class CaptureAuditRepository : IAuditRepository
    {
        public List<AuditRecord> Saved { get; } = [];

        public void Save(AuditRecord auditRecord) => Saved.Add(auditRecord);

        public AuditRecord? GetById(Guid id) => throw new NotSupportedException();

        public IReadOnlyList<AuditRecord> ListByEntity(AuditEntityType entityType, string entityId) =>
            throw new NotSupportedException();
    }
}
