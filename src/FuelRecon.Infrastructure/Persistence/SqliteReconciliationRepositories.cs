using System.Globalization;
using System.Text.Json;
using FuelRecon.Application.Persistence;
using FuelRecon.Domain;
using Microsoft.Data.Sqlite;

namespace FuelRecon.Infrastructure.Persistence;

public sealed class SqliteSettingsSnapshotRepository(SqliteConnectionFactory connectionFactory) : ISettingsSnapshotRepository
{
    public void Save(SettingsSnapshotRecord snapshot)
    {
        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO SettingsSnapshots (Id, CreatedAtUtc, CreatedBy, SnapshotJson, Description)
            VALUES ($id, $createdAtUtc, $createdBy, $snapshotJson, $description);
            """;
        command.Parameters.AddWithValue("$id", snapshot.Id);
        command.Parameters.AddWithValue("$createdAtUtc", SqliteRepositoryHelpers.ToIsoString(snapshot.CreatedAtUtc));
        command.Parameters.AddWithValue("$createdBy", snapshot.CreatedBy);
        command.Parameters.AddWithValue("$snapshotJson", snapshot.SnapshotJson);
        command.Parameters.AddWithNullableValue("$description", snapshot.Description);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public SettingsSnapshotRecord? GetById(string id)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, CreatedAtUtc, CreatedBy, SnapshotJson, Description
            FROM SettingsSnapshots
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new SettingsSnapshotRecord(
                reader.GetString(0),
                DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetNullableString(4))
            : null;
    }
}

public sealed class SqliteReconciliationRunRepository(SqliteConnectionFactory connectionFactory) : IReconciliationRunRepository
{
    public void Save(ReconciliationRun run)
    {
        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        SqliteRepositoryHelpers.EnsurePeriod(connection, transaction, run.Period);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ReconciliationRuns (
                Id, PeriodId, CreatedAtUtc, CreatedBy, Status, SettingsSnapshotId, InputFileChecksums,
                CompletedAtUtc, FailedAtUtc, FailureReasonCode, TotalItemCount, MatchedItemCount,
                ReviewRequiredCount, EstimatedRecoveryTotal
            )
            VALUES (
                $id, $periodId, $createdAtUtc, $createdBy, $status, $settingsSnapshotId, $inputFileChecksums,
                $completedAtUtc, $failedAtUtc, $failureReasonCode, $totalItemCount, $matchedItemCount,
                $reviewRequiredCount, $estimatedRecoveryTotal
            );
            """;
        command.Parameters.AddWithValue("$id", run.Id.ToString());
        command.Parameters.AddWithValue("$periodId", SqliteRepositoryHelpers.ToPeriodId(run.Period));
        command.Parameters.AddWithValue("$createdAtUtc", SqliteRepositoryHelpers.ToIsoString(run.CreatedAtUtc));
        command.Parameters.AddWithValue("$createdBy", run.CreatedBy);
        command.Parameters.AddWithValue("$status", run.Status.ToString());
        command.Parameters.AddWithNullableValue("$settingsSnapshotId", run.SettingsSnapshotId);
        command.Parameters.AddWithValue("$inputFileChecksums", ReconciliationJson.ToChecksumJson(run.InputFileChecksums));
        command.Parameters.AddWithNullableValue("$completedAtUtc", run.CompletedAtUtc is null ? null : SqliteRepositoryHelpers.ToIsoString(run.CompletedAtUtc.Value));
        command.Parameters.AddWithNullableValue("$failedAtUtc", run.FailedAtUtc is null ? null : SqliteRepositoryHelpers.ToIsoString(run.FailedAtUtc.Value));
        command.Parameters.AddWithNullableValue("$failureReasonCode", run.FailureReasonCode);
        command.Parameters.AddWithValue("$totalItemCount", run.TotalItemCount);
        command.Parameters.AddWithValue("$matchedItemCount", run.MatchedItemCount);
        command.Parameters.AddWithValue("$reviewRequiredCount", run.ReviewRequiredCount);
        command.Parameters.AddWithNullableValue("$estimatedRecoveryTotal", run.EstimatedRecoveryTotal?.Value);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public ReconciliationRun? GetById(Guid id)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = ReconciliationRunSelectSql + " WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadRun(reader) : null;
    }

    public ReconciliationRun? GetLatestForPeriod(FuelPeriod period)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = ReconciliationRunSelectSql + " WHERE PeriodId = $periodId ORDER BY CreatedAtUtc DESC, Id DESC LIMIT 1;";
        command.Parameters.AddWithValue("$periodId", SqliteRepositoryHelpers.ToPeriodId(period));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadRun(reader) : null;
    }

    private const string ReconciliationRunSelectSql = """
        SELECT Id, PeriodId, CreatedAtUtc, CreatedBy, Status, SettingsSnapshotId, InputFileChecksums,
               CompletedAtUtc, FailedAtUtc, FailureReasonCode, TotalItemCount, MatchedItemCount,
               ReviewRequiredCount, EstimatedRecoveryTotal
        FROM ReconciliationRuns
        """;

    private static ReconciliationRun ReadRun(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            FuelPeriod.Parse(reader.GetString(1)),
            DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.GetString(3),
            ReconciliationJson.FromChecksumJson(reader.GetString(6)),
            reader.GetNullableString(5),
            Enum.Parse<ReconciliationRunStatus>(reader.GetString(4)),
            reader.GetNullableDateTimeOffset(7),
            reader.GetNullableDateTimeOffset(8),
            reader.GetNullableString(9),
            reader.GetInt32(10),
            reader.GetInt32(11),
            reader.GetInt32(12),
            reader.IsDBNull(13) ? null : new MoneyAmount(reader.GetDecimalValue(13)));
}

public sealed class SqliteReconciliationItemRepository(SqliteConnectionFactory connectionFactory) : IReconciliationItemRepository
{
    public void Save(ReconciliationItem item) => SaveMany([item]);

    public void SaveMany(IEnumerable<ReconciliationItem> items)
    {
        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var item in items)
        {
            Insert(connection, transaction, item);
        }

        transaction.Commit();
    }

    public ReconciliationItem? GetById(Guid id)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = ReconciliationItemSelectSql + " WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadItem(reader) : null;
    }

    public IReadOnlyList<ReconciliationItem> ListByRun(Guid runId)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = ReconciliationItemSelectSql + " WHERE RunId = $runId ORDER BY Id;";
        command.Parameters.AddWithValue("$runId", runId.ToString());
        using var reader = command.ExecuteReader();
        var items = new List<ReconciliationItem>();
        while (reader.Read())
        {
            items.Add(ReadItem(reader));
        }

        return items;
    }

    private static void Insert(SqliteConnection connection, SqliteTransaction transaction, ReconciliationItem item)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ReconciliationItems (
                Id, RunId, PeriodId, BranchId, SystemStatus, ResolutionStatus, FinalStatus, ConfidenceBucket,
                ReasonCodes, HumanReadableReason, SupplierTransactionId, BranchLitresEntryId, CarsBillingEntryId,
                SupplierSourceFile, SupplierSourceSheet, SupplierSourceRowNumber, SupplierSourcePageNumber, SupplierSourceReferenceText,
                BranchSourceFile, BranchSourceSheet, BranchSourceRowNumber, BranchSourcePageNumber, BranchSourceReferenceText,
                CarsSourceFile, CarsSourceSheet, CarsSourceRowNumber, CarsSourcePageNumber, CarsSourceReferenceText,
                MatchCandidatesJson, LitresVariance, AmountVariance, CreatedAtUtc
            )
            VALUES (
                $id, $runId, $periodId, $branchId, $systemStatus, $resolutionStatus, $finalStatus, $confidenceBucket,
                $reasonCodes, $humanReadableReason, $supplierTransactionId, $branchLitresEntryId, $carsBillingEntryId,
                $supplierSourceFile, $supplierSourceSheet, $supplierSourceRowNumber, $supplierSourcePageNumber, $supplierSourceReferenceText,
                $branchSourceFile, $branchSourceSheet, $branchSourceRowNumber, $branchSourcePageNumber, $branchSourceReferenceText,
                $carsSourceFile, $carsSourceSheet, $carsSourceRowNumber, $carsSourcePageNumber, $carsSourceReferenceText,
                $matchCandidatesJson, $litresVariance, $amountVariance, $createdAtUtc
            );
            """;
        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$runId", item.RunId.ToString());
        command.Parameters.AddWithValue("$periodId", SqliteRepositoryHelpers.ToPeriodId(item.Period));
        command.Parameters.AddWithNullableValue("$branchId", item.BranchId?.Value);
        command.Parameters.AddWithValue("$systemStatus", item.SystemStatus.ToString());
        command.Parameters.AddWithValue("$resolutionStatus", item.ResolutionStatus.ToString());
        command.Parameters.AddWithNullableValue("$finalStatus", item.FinalStatus?.ToString());
        command.Parameters.AddWithValue("$confidenceBucket", item.ConfidenceBucket.ToString());
        command.Parameters.AddWithValue("$reasonCodes", SqliteRepositoryHelpers.ToJson(item.ReasonCodes));
        command.Parameters.AddWithNullableValue("$humanReadableReason", item.HumanReadableReason);
        command.Parameters.AddWithNullableValue("$supplierTransactionId", item.SupplierTransactionId?.ToString());
        command.Parameters.AddWithNullableValue("$branchLitresEntryId", item.BranchLitresEntryId?.ToString());
        command.Parameters.AddWithNullableValue("$carsBillingEntryId", item.CarsBillingEntryId?.ToString());
        AddNullableSourceReference(command, "supplier", item.SupplierSourceReference);
        AddNullableSourceReference(command, "branch", item.BranchSourceReference);
        AddNullableSourceReference(command, "cars", item.CarsSourceReference);
        command.Parameters.AddWithValue("$matchCandidatesJson", ReconciliationJson.ToCandidateJson(item.MatchCandidates));
        command.Parameters.AddWithNullableValue("$litresVariance", item.LitresVariance);
        command.Parameters.AddWithNullableValue("$amountVariance", item.AmountVariance?.Value);
        command.Parameters.AddWithValue("$createdAtUtc", SqliteRepositoryHelpers.ToIsoString(DateTimeOffset.UtcNow));
        command.ExecuteNonQuery();
    }

    private const string ReconciliationItemSelectSql = """
        SELECT Id, RunId, PeriodId, BranchId, SystemStatus, ResolutionStatus, FinalStatus, ConfidenceBucket,
               ReasonCodes, HumanReadableReason, SupplierTransactionId, BranchLitresEntryId, CarsBillingEntryId,
               SupplierSourceFile, SupplierSourceSheet, SupplierSourceRowNumber, SupplierSourcePageNumber, SupplierSourceReferenceText,
               BranchSourceFile, BranchSourceSheet, BranchSourceRowNumber, BranchSourcePageNumber, BranchSourceReferenceText,
               CarsSourceFile, CarsSourceSheet, CarsSourceRowNumber, CarsSourcePageNumber, CarsSourceReferenceText,
               MatchCandidatesJson, LitresVariance, AmountVariance
        FROM ReconciliationItems
        """;

    private static ReconciliationItem ReadItem(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            FuelPeriod.Parse(reader.GetString(2)),
            Enum.Parse<ReconciliationStatus>(reader.GetString(4)),
            Enum.Parse<ResolutionStatus>(reader.GetString(5)),
            Enum.Parse<ConfidenceBucket>(reader.GetString(7)),
            SqliteRepositoryHelpers.FromJson(reader.GetString(8)),
            reader.GetNullableString(3) is { } branchId ? new CanonicalBranchId(branchId) : null,
            reader.GetNullableString(6) is { } finalStatus ? Enum.Parse<ReconciliationStatus>(finalStatus) : null,
            reader.GetNullableString(9),
            reader.GetNullableString(10) is { } supplierId ? Guid.Parse(supplierId) : null,
            reader.GetNullableString(11) is { } branchEntryId ? Guid.Parse(branchEntryId) : null,
            reader.GetNullableString(12) is { } carsEntryId ? Guid.Parse(carsEntryId) : null,
            ReadNullableSourceReference(reader, 13),
            ReadNullableSourceReference(reader, 18),
            ReadNullableSourceReference(reader, 23),
            ReconciliationJson.FromCandidateJson(reader.GetNullableString(28)),
            reader.IsDBNull(29) ? null : reader.GetDecimalValue(29),
            reader.IsDBNull(30) ? null : new MoneyAmount(reader.GetDecimalValue(30)));

    private static void AddNullableSourceReference(SqliteCommand command, string prefix, SourceReference? sourceReference)
    {
        command.Parameters.AddWithNullableValue($"${prefix}SourceFile", sourceReference?.SourceFile);
        command.Parameters.AddWithNullableValue($"${prefix}SourceSheet", sourceReference?.SheetName);
        command.Parameters.AddWithNullableValue($"${prefix}SourceRowNumber", sourceReference?.RowNumber);
        command.Parameters.AddWithNullableValue($"${prefix}SourcePageNumber", sourceReference?.PageNumber);
        command.Parameters.AddWithNullableValue($"${prefix}SourceReferenceText", sourceReference?.ReferenceText);
    }

    private static SourceReference? ReadNullableSourceReference(SqliteDataReader reader, int sourceFileOrdinal) =>
        reader.IsDBNull(sourceFileOrdinal)
            ? null
            : SqliteRepositoryHelpers.ReadSourceReference(reader, sourceFileOrdinal);
}

public sealed class SqliteBranchReportRepository(SqliteConnectionFactory connectionFactory) : IBranchReportRepository
{
    public void Save(BranchReportVersion report, BranchSummary? summary = null)
    {
        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        SqliteRepositoryHelpers.EnsurePeriod(connection, transaction, report.Period);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO BranchReports (
                Id, RunId, PeriodId, BranchId, VersionNumber, CreatedAtUtc, CreatedBy, Status, Notes,
                SupplierLitres, BranchLitres, BilledLitres, UnbilledLitres, EstimatedRecovery, ReviewCount
            )
            VALUES (
                $id, $runId, $periodId, $branchId, $versionNumber, $createdAtUtc, $createdBy, $status, $notes,
                $supplierLitres, $branchLitres, $billedLitres, $unbilledLitres, $estimatedRecovery, $reviewCount
            );
            """;
        command.Parameters.AddWithValue("$id", report.Id.ToString());
        command.Parameters.AddWithValue("$runId", report.RunId.ToString());
        command.Parameters.AddWithValue("$periodId", SqliteRepositoryHelpers.ToPeriodId(report.Period));
        command.Parameters.AddWithValue("$branchId", report.BranchId.Value);
        command.Parameters.AddWithValue("$versionNumber", report.VersionNumber);
        command.Parameters.AddWithValue("$createdAtUtc", SqliteRepositoryHelpers.ToIsoString(report.CreatedAtUtc));
        command.Parameters.AddWithValue("$createdBy", report.CreatedBy);
        command.Parameters.AddWithValue("$status", report.Status.ToString());
        command.Parameters.AddWithNullableValue("$notes", report.Notes);
        command.Parameters.AddWithNullableValue("$supplierLitres", summary?.SupplierLitres.Value);
        command.Parameters.AddWithNullableValue("$branchLitres", summary?.BranchLitres.Value);
        command.Parameters.AddWithNullableValue("$billedLitres", summary?.BilledLitres.Value);
        command.Parameters.AddWithNullableValue("$unbilledLitres", summary?.UnbilledLitres.Value);
        command.Parameters.AddWithNullableValue("$estimatedRecovery", summary?.EstimatedRecovery.Value);
        command.Parameters.AddWithValue("$reviewCount", summary?.ReviewCount ?? 0);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public BranchReportVersion? GetById(Guid id)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = BranchReportSelectSql + " WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadReport(reader) : null;
    }

    public IReadOnlyList<BranchReportVersion> ListByRunAndBranch(Guid runId, CanonicalBranchId branchId)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = BranchReportSelectSql + " WHERE RunId = $runId AND BranchId = $branchId ORDER BY VersionNumber;";
        command.Parameters.AddWithValue("$runId", runId.ToString());
        command.Parameters.AddWithValue("$branchId", branchId.Value);
        using var reader = command.ExecuteReader();
        var reports = new List<BranchReportVersion>();
        while (reader.Read())
        {
            reports.Add(ReadReport(reader));
        }

        return reports;
    }

    public BranchReportPersistedMetrics? GetPersistedMetrics(Guid branchReportVersionId)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ReviewCount, Status, SupplierLitres, BranchLitres, BilledLitres, UnbilledLitres, EstimatedRecovery
            FROM BranchReports
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", branchReportVersionId.ToString());
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        static Litres ReadLitres(SqliteDataReader r, int ordinal) =>
            r.IsDBNull(ordinal) ? new Litres(0m) : new Litres(r.GetDecimalValue(ordinal));

        var estimated = reader.IsDBNull(6)
            ? new MoneyAmount(0m)
            : new MoneyAmount(reader.GetDecimalValue(6));

        return new BranchReportPersistedMetrics(
            reader.GetInt32(0),
            Enum.Parse<PeriodLifecycleStatus>(reader.GetString(1)),
            ReadLitres(reader, 2),
            ReadLitres(reader, 3),
            ReadLitres(reader, 4),
            ReadLitres(reader, 5),
            estimated);
    }

    private const string BranchReportSelectSql = """
        SELECT Id, RunId, BranchId, PeriodId, VersionNumber, CreatedAtUtc, CreatedBy, Status, Notes
        FROM BranchReports
        """;

    private static BranchReportVersion ReadReport(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            new CanonicalBranchId(reader.GetString(2)),
            FuelPeriod.Parse(reader.GetString(3)),
            reader.GetInt32(4),
            DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.GetString(6),
            Enum.Parse<PeriodLifecycleStatus>(reader.GetString(7)),
            reader.GetNullableString(8));
}

public sealed class SqliteBranchReportNoteRepository(SqliteConnectionFactory connectionFactory) : IBranchReportNoteRepository
{
    public void Save(BranchReportNote note)
    {
        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO BranchReportNotes (
                Id, BranchReportId, CreatedAtUtc, CreatedBy, NoteText, ReasonCode
            )
            VALUES (
                $id, $branchReportId, $createdAtUtc, $createdBy, $noteText, $reasonCode
            );
            """;
        command.Parameters.AddWithValue("$id", note.Id.ToString());
        command.Parameters.AddWithValue("$branchReportId", note.BranchReportVersionId.ToString());
        command.Parameters.AddWithValue("$createdAtUtc", SqliteRepositoryHelpers.ToIsoString(note.CreatedAtUtc));
        command.Parameters.AddWithValue("$createdBy", note.CreatedBy);
        command.Parameters.AddWithValue("$noteText", note.NoteText);
        command.Parameters.AddWithNullableValue("$reasonCode", note.ReasonCode);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public IReadOnlyList<BranchReportNote> ListByBranchReport(Guid branchReportVersionId)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, BranchReportId, CreatedAtUtc, CreatedBy, NoteText, ReasonCode
            FROM BranchReportNotes
            WHERE BranchReportId = $branchReportId
            ORDER BY CreatedAtUtc, Id;
            """;
        command.Parameters.AddWithValue("$branchReportId", branchReportVersionId.ToString());
        using var reader = command.ExecuteReader();
        var notes = new List<BranchReportNote>();
        while (reader.Read())
        {
            notes.Add(ReadNote(reader));
        }

        return notes;
    }

    private static BranchReportNote ReadNote(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetNullableString(5));
}

public sealed class SqliteBranchReportApprovalRepository(SqliteConnectionFactory connectionFactory)
    : IBranchReportApprovalRepository
{
    public void Save(BranchReportApprovalRecord approval)
    {
        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO BranchReportApprovals (
                Id, BranchReportId, RunId, ApprovedAtUtc, ApprovedBy, ApprovalNote, SnapshotJson
            )
            VALUES (
                $id, $branchReportId, $runId, $approvedAtUtc, $approvedBy, $approvalNote, $snapshotJson
            );
            """;
        command.Parameters.AddWithValue("$id", approval.Id.ToString());
        command.Parameters.AddWithValue("$branchReportId", approval.BranchReportVersionId.ToString());
        command.Parameters.AddWithValue("$runId", approval.RunId.ToString());
        command.Parameters.AddWithValue("$approvedAtUtc", SqliteRepositoryHelpers.ToIsoString(approval.ApprovedAtUtc));
        command.Parameters.AddWithValue("$approvedBy", approval.ApprovedBy);
        command.Parameters.AddWithNullableValue("$approvalNote", approval.ApprovalNote);
        command.Parameters.AddWithValue("$snapshotJson", approval.SnapshotJson);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public BranchReportApprovalRecord? FindByBranchReport(Guid branchReportVersionId)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, BranchReportId, RunId, ApprovedAtUtc, ApprovedBy, ApprovalNote, SnapshotJson
            FROM BranchReportApprovals
            WHERE BranchReportId = $branchReportId;
            """;
        command.Parameters.AddWithValue("$branchReportId", branchReportVersionId.ToString());
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadApproval(reader) : null;
    }

    private static BranchReportApprovalRecord ReadApproval(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(2)),
            DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.GetString(4),
            reader.GetString(6),
            reader.GetNullableString(5));
}

public sealed class SqlitePdfExportRepository(SqliteConnectionFactory connectionFactory) : IPdfExportRepository
{
    public void Save(PdfExportRecord exportRecord)
    {
        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO PdfExports (
                Id, BranchReportId, ExportedAtUtc, ExportedBy, Status, FilePath, TemplateName,
                TemplateVersion, ErrorCategory, CorrelationId
            )
            VALUES (
                $id, $branchReportId, $exportedAtUtc, $exportedBy, $status, $filePath, $templateName,
                $templateVersion, $errorCategory, $correlationId
            );
            """;
        command.Parameters.AddWithValue("$id", exportRecord.Id.ToString());
        command.Parameters.AddWithValue("$branchReportId", exportRecord.BranchReportVersionId.ToString());
        command.Parameters.AddWithValue("$exportedAtUtc", SqliteRepositoryHelpers.ToIsoString(exportRecord.ExportedAtUtc));
        command.Parameters.AddWithValue("$exportedBy", exportRecord.ExportedBy);
        command.Parameters.AddWithValue("$status", exportRecord.Status.ToString());
        command.Parameters.AddWithNullableValue("$filePath", exportRecord.FilePath);
        command.Parameters.AddWithNullableValue("$templateName", exportRecord.TemplateName);
        command.Parameters.AddWithNullableValue("$templateVersion", exportRecord.TemplateVersion);
        command.Parameters.AddWithNullableValue("$errorCategory", exportRecord.ErrorCategory);
        command.Parameters.AddWithNullableValue("$correlationId", exportRecord.CorrelationId?.Value);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public PdfExportRecord? GetById(Guid id)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = PdfExportSelectSql + " WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadExport(reader) : null;
    }

    public IReadOnlyList<PdfExportRecord> ListByBranchReport(Guid branchReportId)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = PdfExportSelectSql + " WHERE BranchReportId = $branchReportId ORDER BY ExportedAtUtc, Id;";
        command.Parameters.AddWithValue("$branchReportId", branchReportId.ToString());
        using var reader = command.ExecuteReader();
        var exports = new List<PdfExportRecord>();
        while (reader.Read())
        {
            exports.Add(ReadExport(reader));
        }

        return exports;
    }

    private const string PdfExportSelectSql = """
        SELECT Id, BranchReportId, ExportedAtUtc, ExportedBy, Status, FilePath,
               TemplateName, TemplateVersion, ErrorCategory, CorrelationId
        FROM PdfExports
        """;

    private static PdfExportRecord ReadExport(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.GetString(3),
            Enum.Parse<PdfExportStatus>(reader.GetString(4)),
            reader.GetNullableString(5),
            reader.GetNullableString(6),
            reader.GetNullableString(7),
            reader.GetNullableString(8),
            reader.GetNullableString(9) is { } correlationId ? new CorrelationId(correlationId) : null);
}

public sealed class SqliteAuditRepository(SqliteConnectionFactory connectionFactory) : IAuditRepository
{
    public void Save(AuditRecord auditRecord)
    {
        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO AuditRecords (
                Id, CreatedAtUtc, Actor, ActionType, EntityType, EntityId, Origin,
                OldValuesJson, NewValuesJson, Note, ReasonCode, CorrelationId, ContextJson
            )
            VALUES (
                $id, $createdAtUtc, $actor, $actionType, $entityType, $entityId, $origin,
                $oldValuesJson, $newValuesJson, $note, $reasonCode, $correlationId, $contextJson
            );
            """;
        command.Parameters.AddWithValue("$id", auditRecord.Id.ToString());
        command.Parameters.AddWithValue("$createdAtUtc", SqliteRepositoryHelpers.ToIsoString(auditRecord.CreatedAtUtc));
        command.Parameters.AddWithValue("$actor", auditRecord.Actor);
        command.Parameters.AddWithValue("$actionType", auditRecord.ActionType.ToString());
        command.Parameters.AddWithValue("$entityType", auditRecord.EntityType.ToString());
        command.Parameters.AddWithValue("$entityId", auditRecord.EntityId);
        command.Parameters.AddWithValue("$origin", auditRecord.Origin);
        command.Parameters.AddWithNullableValue("$oldValuesJson", auditRecord.OldValuesJson);
        command.Parameters.AddWithNullableValue("$newValuesJson", auditRecord.NewValuesJson);
        command.Parameters.AddWithNullableValue("$note", auditRecord.Note);
        command.Parameters.AddWithNullableValue("$reasonCode", auditRecord.ReasonCode);
        command.Parameters.AddWithNullableValue("$correlationId", auditRecord.CorrelationId?.Value);
        command.Parameters.AddWithNullableValue("$contextJson", auditRecord.ContextJson);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public AuditRecord? GetById(Guid id)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = AuditSelectSql + " WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadAudit(reader) : null;
    }

    public IReadOnlyList<AuditRecord> ListByEntity(AuditEntityType entityType, string entityId)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = AuditSelectSql + " WHERE EntityType = $entityType AND EntityId = $entityId ORDER BY CreatedAtUtc, Id;";
        command.Parameters.AddWithValue("$entityType", entityType.ToString());
        command.Parameters.AddWithValue("$entityId", entityId);
        using var reader = command.ExecuteReader();
        var audits = new List<AuditRecord>();
        while (reader.Read())
        {
            audits.Add(ReadAudit(reader));
        }

        return audits;
    }

    private const string AuditSelectSql = """
        SELECT Id, CreatedAtUtc, Actor, ActionType, EntityType, EntityId, Origin,
               OldValuesJson, NewValuesJson, Note, ReasonCode, CorrelationId, ContextJson
        FROM AuditRecords
        """;

    private static AuditRecord ReadAudit(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.GetString(2),
            Enum.Parse<AuditActionType>(reader.GetString(3)),
            Enum.Parse<AuditEntityType>(reader.GetString(4)),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetNullableString(7),
            reader.GetNullableString(8),
            reader.GetNullableString(9),
            reader.GetNullableString(10),
            reader.GetNullableString(11) is { } correlationId ? new CorrelationId(correlationId) : null,
            reader.GetNullableString(12));
}

internal static class ReconciliationJson
{
    internal static string ToChecksumJson(IReadOnlyList<FileChecksum> checksums) =>
        JsonSerializer.Serialize(checksums.Select(checksum => new FileChecksumDto(checksum.Algorithm, checksum.Value)).ToArray());

    internal static IReadOnlyList<FileChecksum> FromChecksumJson(string value) =>
        (JsonSerializer.Deserialize<FileChecksumDto[]>(value) ?? [])
        .Select(checksum => new FileChecksum(checksum.Algorithm, checksum.Value))
        .ToArray();

    internal static string ToCandidateJson(IReadOnlyList<MatchCandidate> candidates) =>
        JsonSerializer.Serialize(candidates.Select(ToDto).ToArray());

    internal static IReadOnlyList<MatchCandidate> FromCandidateJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<MatchCandidate>();
        }

        return (JsonSerializer.Deserialize<MatchCandidateDto[]>(value) ?? [])
            .Select(FromDto)
            .ToArray();
    }

    private static MatchCandidateDto ToDto(MatchCandidate candidate) =>
        new(
            candidate.Id,
            candidate.CandidateType.ToString(),
            candidate.SourceId,
            candidate.ConfidenceBucket.ToString(),
            candidate.MatchedFields.ToArray(),
            candidate.MissingFields.ToArray(),
            candidate.ConflictingFields.ToArray(),
            SourceReferenceDto.From(candidate.SourceReference));

    private static MatchCandidate FromDto(MatchCandidateDto dto) =>
        new(
            dto.Id,
            Enum.Parse<MatchCandidateType>(dto.CandidateType),
            dto.SourceId,
            Enum.Parse<ConfidenceBucket>(dto.ConfidenceBucket),
            dto.MatchedFields,
            dto.MissingFields,
            dto.ConflictingFields,
            dto.SourceReference?.ToSourceReference());

    private sealed record FileChecksumDto(string Algorithm, string Value);

    private sealed record MatchCandidateDto(
        Guid Id,
        string CandidateType,
        Guid SourceId,
        string ConfidenceBucket,
        string[] MatchedFields,
        string[] MissingFields,
        string[] ConflictingFields,
        SourceReferenceDto? SourceReference);

    private sealed record SourceReferenceDto(
        string SourceFile,
        string? SheetName,
        int? RowNumber,
        int? PageNumber,
        string? ReferenceText)
    {
        public static SourceReferenceDto? From(SourceReference? sourceReference) =>
            sourceReference is null
                ? null
                : new SourceReferenceDto(
                    sourceReference.SourceFile,
                    sourceReference.SheetName,
                    sourceReference.RowNumber,
                    sourceReference.PageNumber,
                    sourceReference.ReferenceText);

        public SourceReference ToSourceReference() =>
            new(SourceFile, SheetName, RowNumber, PageNumber, ReferenceText);
    }
}
