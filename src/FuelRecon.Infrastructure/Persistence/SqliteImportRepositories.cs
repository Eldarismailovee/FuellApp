using System.Globalization;
using System.Text.Json;
using FuelRecon.Application.Persistence;
using FuelRecon.Domain;
using Microsoft.Data.Sqlite;

namespace FuelRecon.Infrastructure.Persistence;

public sealed class SqliteImportBatchRepository(SqliteConnectionFactory connectionFactory) : IImportBatchRepository
{
    public void Save(ImportBatchRecord batch)
    {
        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        SqliteRepositoryHelpers.EnsurePeriod(connection, transaction, batch.Period);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ImportBatches (
                Id,
                PeriodId,
                CreatedAtUtc,
                CreatedBy,
                Status,
                SourceDescription,
                SettingsSnapshotId
            )
            VALUES (
                $id,
                $periodId,
                $createdAtUtc,
                $createdBy,
                $status,
                $sourceDescription,
                $settingsSnapshotId
            );
            """;
        command.Parameters.AddWithValue("$id", batch.Id.ToString());
        command.Parameters.AddWithValue("$periodId", SqliteRepositoryHelpers.ToPeriodId(batch.Period));
        command.Parameters.AddWithValue("$createdAtUtc", SqliteRepositoryHelpers.ToIsoString(batch.CreatedAtUtc));
        command.Parameters.AddWithValue("$createdBy", batch.CreatedBy);
        command.Parameters.AddWithValue("$status", batch.Status);
        command.Parameters.AddWithNullableValue("$sourceDescription", batch.SourceDescription);
        command.Parameters.AddWithNullableValue("$settingsSnapshotId", batch.SettingsSnapshotId);
        command.ExecuteNonQuery();

        transaction.Commit();
    }

    public ImportBatchRecord? GetById(Guid id)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id,
                   PeriodId,
                   CreatedAtUtc,
                   CreatedBy,
                   Status,
                   SourceDescription,
                   SettingsSnapshotId
            FROM ImportBatches
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadBatch(reader) : null;
    }

    private static ImportBatchRecord ReadBatch(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            FuelPeriod.Parse(reader.GetString(1)),
            DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetNullableString(5),
            reader.GetNullableString(6));
}

public sealed class SqliteImportedFileRepository(SqliteConnectionFactory connectionFactory) : IImportedFileRepository
{
    public void Save(ImportedFileRecord file)
    {
        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ImportedFiles (
                Id,
                ImportBatchId,
                PeriodId,
                InputSlot,
                OriginalFileName,
                StoredFilePath,
                FileStatus,
                ChecksumAlgorithm,
                ChecksumValue,
                ParserName,
                ParserVersion,
                ImportedAtUtc,
                CompletedAtUtc,
                FailureReasonCode,
                FailureMessage
            )
            VALUES (
                $id,
                $importBatchId,
                $periodId,
                $inputSlot,
                $originalFileName,
                $storedFilePath,
                $fileStatus,
                $checksumAlgorithm,
                $checksumValue,
                $parserName,
                $parserVersion,
                $importedAtUtc,
                $completedAtUtc,
                $failureReasonCode,
                $failureMessage
            );
            """;
        command.Parameters.AddWithValue("$id", file.Id.ToString());
        command.Parameters.AddWithValue("$importBatchId", file.ImportBatchId.ToString());
        command.Parameters.AddWithValue("$periodId", SqliteRepositoryHelpers.ToPeriodId(file.Period));
        command.Parameters.AddWithValue("$inputSlot", file.InputSlot.ToString());
        command.Parameters.AddWithValue("$originalFileName", file.OriginalFileName);
        command.Parameters.AddWithNullableValue("$storedFilePath", file.StoredFilePath);
        command.Parameters.AddWithValue("$fileStatus", file.FileStatus.ToString());
        command.Parameters.AddWithValue("$checksumAlgorithm", file.Checksum.Algorithm);
        command.Parameters.AddWithValue("$checksumValue", file.Checksum.Value);
        command.Parameters.AddWithNullableValue("$parserName", file.ParserName);
        command.Parameters.AddWithNullableValue("$parserVersion", file.ParserVersion);
        command.Parameters.AddWithValue("$importedAtUtc", SqliteRepositoryHelpers.ToIsoString(file.ImportedAtUtc));
        command.Parameters.AddWithNullableValue("$completedAtUtc", file.CompletedAtUtc is null ? null : SqliteRepositoryHelpers.ToIsoString(file.CompletedAtUtc.Value));
        command.Parameters.AddWithNullableValue("$failureReasonCode", file.FailureReasonCode);
        command.Parameters.AddWithNullableValue("$failureMessage", file.FailureMessage);
        command.ExecuteNonQuery();

        transaction.Commit();
    }

    public ImportedFileRecord? GetById(Guid id)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = ImportedFileSelectSql + " WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadFile(reader) : null;
    }

    public IReadOnlyList<ImportedFileRecord> ListByImportBatch(Guid importBatchId)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = ImportedFileSelectSql + " WHERE ImportBatchId = $importBatchId ORDER BY ImportedAtUtc, Id;";
        command.Parameters.AddWithValue("$importBatchId", importBatchId.ToString());

        using var reader = command.ExecuteReader();
        var files = new List<ImportedFileRecord>();
        while (reader.Read())
        {
            files.Add(ReadFile(reader));
        }

        return files;
    }

    private const string ImportedFileSelectSql = """
        SELECT Id,
               ImportBatchId,
               PeriodId,
               InputSlot,
               OriginalFileName,
               StoredFilePath,
               FileStatus,
               ChecksumAlgorithm,
               ChecksumValue,
               ParserName,
               ParserVersion,
               ImportedAtUtc,
               CompletedAtUtc,
               FailureReasonCode,
               FailureMessage
        FROM ImportedFiles
        """;

    private static ImportedFileRecord ReadFile(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            FuelPeriod.Parse(reader.GetString(2)),
            Enum.Parse<InputSlot>(reader.GetString(3)),
            reader.GetString(4),
            Enum.Parse<FileStatus>(reader.GetString(6)),
            new FileChecksum(reader.GetString(7), reader.GetString(8)),
            DateTimeOffset.Parse(reader.GetString(11), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.GetNullableString(5),
            reader.GetNullableString(9),
            reader.GetNullableString(10),
            reader.GetNullableDateTimeOffset(12),
            reader.GetNullableString(13),
            reader.GetNullableString(14));
}

public sealed class SqliteSupplierTransactionRepository(SqliteConnectionFactory connectionFactory) : ISupplierTransactionRepository
{
    public void Save(Guid importBatchId, Guid importedFileId, SupplierTransaction transaction) =>
        SaveMany(importBatchId, importedFileId, [transaction]);

    public void SaveMany(Guid importBatchId, Guid importedFileId, IEnumerable<SupplierTransaction> transactions)
    {
        using var connection = connectionFactory.OpenConnection();
        using var dbTransaction = connection.BeginTransaction();

        foreach (var transaction in transactions)
        {
            Insert(connection, dbTransaction, importBatchId, importedFileId, transaction);
        }

        dbTransaction.Commit();
    }

    public SupplierTransaction? GetById(Guid id)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SupplierTransactionSelectSql + " WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadSupplierTransaction(reader) : null;
    }

    public IReadOnlyList<SupplierTransaction> ListByImportBatch(Guid importBatchId)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SupplierTransactionSelectSql + " WHERE ImportBatchId = $importBatchId ORDER BY TransactionDate, Id;";
        command.Parameters.AddWithValue("$importBatchId", importBatchId.ToString());

        using var reader = command.ExecuteReader();
        var transactions = new List<SupplierTransaction>();
        while (reader.Read())
        {
            transactions.Add(ReadSupplierTransaction(reader));
        }

        return transactions;
    }

    private static void Insert(SqliteConnection connection, SqliteTransaction dbTransaction, Guid importBatchId, Guid importedFileId, SupplierTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = dbTransaction;
        command.CommandText = """
            INSERT INTO SupplierTransactions (
                Id, ImportBatchId, ImportedFileId, PeriodId, SupplierName, BranchId, TransactionDate,
                RawBranchText, RawSiteText, Cardholder, VoucherOrInvoiceReference, Product,
                RawLitres, NormalisedLitres, RawAmount, NormalisedAmount,
                SourceFile, SourceSheet, SourceRowNumber, SourcePageNumber, SourceReferenceText,
                ValidationIssueCodes, CreatedAtUtc
            )
            VALUES (
                $id, $importBatchId, $importedFileId, $periodId, $supplierName, $branchId, $transactionDate,
                $rawBranchText, $rawSiteText, $cardholder, $voucherOrInvoiceReference, $product,
                $rawLitres, $normalisedLitres, $rawAmount, $normalisedAmount,
                $sourceFile, $sourceSheet, $sourceRowNumber, $sourcePageNumber, $sourceReferenceText,
                $validationIssueCodes, $createdAtUtc
            );
            """;
        command.Parameters.AddWithValue("$id", transaction.Id.ToString());
        command.Parameters.AddWithValue("$importBatchId", importBatchId.ToString());
        command.Parameters.AddWithValue("$importedFileId", importedFileId.ToString());
        command.Parameters.AddWithValue("$periodId", SqliteRepositoryHelpers.ToPeriodId(transaction.Period));
        command.Parameters.AddWithValue("$supplierName", transaction.SupplierName);
        command.Parameters.AddWithNullableValue("$branchId", transaction.BranchId?.Value);
        command.Parameters.AddWithValue("$transactionDate", SqliteRepositoryHelpers.ToIsoDateString(transaction.TransactionDate));
        command.Parameters.AddWithNullableValue("$rawBranchText", transaction.RawBranchText);
        command.Parameters.AddWithNullableValue("$rawSiteText", transaction.RawSiteText);
        command.Parameters.AddWithNullableValue("$cardholder", transaction.Cardholder);
        command.Parameters.AddWithNullableValue("$voucherOrInvoiceReference", transaction.VoucherOrInvoiceReference);
        command.Parameters.AddWithNullableValue("$product", transaction.Product);
        command.Parameters.AddWithValue("$rawLitres", transaction.Litres.ToString());
        command.Parameters.AddWithValue("$normalisedLitres", transaction.Litres.Value);
        command.Parameters.AddWithNullableValue("$rawAmount", transaction.Amount?.ToString());
        command.Parameters.AddWithNullableValue("$normalisedAmount", transaction.Amount?.Value);
        SqliteRepositoryHelpers.AddSourceReferenceParameters(command, transaction.SourceReference);
        command.Parameters.AddWithValue("$validationIssueCodes", SqliteRepositoryHelpers.ToJson(transaction.ValidationIssueCodes));
        command.Parameters.AddWithValue("$createdAtUtc", SqliteRepositoryHelpers.ToIsoString(DateTimeOffset.UtcNow));
        command.ExecuteNonQuery();
    }

    private const string SupplierTransactionSelectSql = """
        SELECT Id, PeriodId, SupplierName, BranchId, TransactionDate, RawBranchText, RawSiteText, Cardholder,
               VoucherOrInvoiceReference, Product, NormalisedLitres, NormalisedAmount,
               SourceFile, SourceSheet, SourceRowNumber, SourcePageNumber, SourceReferenceText, ValidationIssueCodes
        FROM SupplierTransactions
        """;

    private static SupplierTransaction ReadSupplierTransaction(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(2),
            FuelPeriod.Parse(reader.GetString(1)),
            DateOnly.ParseExact(reader.GetString(4), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            new Litres(reader.GetDecimalValue(10)),
            SqliteRepositoryHelpers.ReadSourceReference(reader, 12),
            reader.GetNullableString(3) is { } branchId ? new CanonicalBranchId(branchId) : null,
            reader.GetNullableString(5),
            reader.GetNullableString(6),
            reader.GetNullableString(7),
            reader.GetNullableString(8),
            reader.GetNullableString(9),
            reader.IsDBNull(11) ? null : new MoneyAmount(reader.GetDecimalValue(11)),
            SqliteRepositoryHelpers.FromJson(reader.GetNullableString(17)));
}

public sealed class SqliteBranchLitresRepository(SqliteConnectionFactory connectionFactory) : IBranchLitresRepository
{
    public void Save(Guid importBatchId, Guid importedFileId, BranchLitresEntry entry) =>
        SaveMany(importBatchId, importedFileId, [entry]);

    public void SaveMany(Guid importBatchId, Guid importedFileId, IEnumerable<BranchLitresEntry> entries)
    {
        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        foreach (var entry in entries)
        {
            Insert(connection, transaction, importBatchId, importedFileId, entry);
        }

        transaction.Commit();
    }

    public BranchLitresEntry? GetById(Guid id)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = BranchLitresSelectSql + " WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadBranchLitresEntry(reader) : null;
    }

    public IReadOnlyList<BranchLitresEntry> ListByImportBatch(Guid importBatchId)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = BranchLitresSelectSql + " WHERE ImportBatchId = $importBatchId ORDER BY EntryDate, Id;";
        command.Parameters.AddWithValue("$importBatchId", importBatchId.ToString());

        using var reader = command.ExecuteReader();
        var entries = new List<BranchLitresEntry>();
        while (reader.Read())
        {
            entries.Add(ReadBranchLitresEntry(reader));
        }

        return entries;
    }

    private static void Insert(SqliteConnection connection, SqliteTransaction transaction, Guid importBatchId, Guid importedFileId, BranchLitresEntry entry)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO BranchLitresEntries (
                Id, ImportBatchId, ImportedFileId, PeriodId, BranchId, EntryDate,
                RawRentalAgreementNumber, NormalisedRentalAgreementNumber, RawRego, NormalisedRego,
                NoteOrReference, RawLitres, NormalisedLitres,
                SourceFile, SourceSheet, SourceRowNumber, SourcePageNumber, SourceReferenceText,
                ValidationIssueCodes, CreatedAtUtc
            )
            VALUES (
                $id, $importBatchId, $importedFileId, $periodId, $branchId, $entryDate,
                $rawRentalAgreementNumber, $normalisedRentalAgreementNumber, $rawRego, $normalisedRego,
                $noteOrReference, $rawLitres, $normalisedLitres,
                $sourceFile, $sourceSheet, $sourceRowNumber, $sourcePageNumber, $sourceReferenceText,
                $validationIssueCodes, $createdAtUtc
            );
            """;
        command.Parameters.AddWithValue("$id", entry.Id.ToString());
        command.Parameters.AddWithValue("$importBatchId", importBatchId.ToString());
        command.Parameters.AddWithValue("$importedFileId", importedFileId.ToString());
        command.Parameters.AddWithValue("$periodId", SqliteRepositoryHelpers.ToPeriodId(entry.Period));
        command.Parameters.AddWithValue("$branchId", entry.BranchId.Value);
        command.Parameters.AddWithValue("$entryDate", SqliteRepositoryHelpers.ToIsoDateString(entry.Date));
        command.Parameters.AddWithNullableValue("$rawRentalAgreementNumber", entry.RentalAgreementNumber?.RawValue);
        command.Parameters.AddWithNullableValue("$normalisedRentalAgreementNumber", entry.RentalAgreementNumber?.NormalisedValue);
        command.Parameters.AddWithNullableValue("$rawRego", entry.Rego?.RawValue);
        command.Parameters.AddWithNullableValue("$normalisedRego", entry.Rego?.NormalisedValue);
        command.Parameters.AddWithNullableValue("$noteOrReference", entry.NoteOrReference);
        command.Parameters.AddWithValue("$rawLitres", entry.Litres.ToString());
        command.Parameters.AddWithValue("$normalisedLitres", entry.Litres.Value);
        SqliteRepositoryHelpers.AddSourceReferenceParameters(command, entry.SourceReference);
        command.Parameters.AddWithValue("$validationIssueCodes", SqliteRepositoryHelpers.ToJson(entry.ValidationIssueCodes));
        command.Parameters.AddWithValue("$createdAtUtc", SqliteRepositoryHelpers.ToIsoString(DateTimeOffset.UtcNow));
        command.ExecuteNonQuery();
    }

    private const string BranchLitresSelectSql = """
        SELECT Id, PeriodId, BranchId, EntryDate, RawRentalAgreementNumber, RawRego, NoteOrReference,
               NormalisedLitres, SourceFile, SourceSheet, SourceRowNumber, SourcePageNumber, SourceReferenceText,
               ValidationIssueCodes
        FROM BranchLitresEntries
        """;

    private static BranchLitresEntry ReadBranchLitresEntry(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            FuelPeriod.Parse(reader.GetString(1)),
            new CanonicalBranchId(reader.GetString(2)),
            DateOnly.ParseExact(reader.GetString(3), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            new Litres(reader.GetDecimalValue(7)),
            SqliteRepositoryHelpers.ReadSourceReference(reader, 8),
            reader.GetNullableString(4) is { } ra ? new RentalAgreementNumber(ra) : null,
            reader.GetNullableString(5) is { } rego ? new Rego(rego) : null,
            reader.GetNullableString(6),
            SqliteRepositoryHelpers.FromJson(reader.GetNullableString(13)));
}

public sealed class SqliteCarsBillingRepository(SqliteConnectionFactory connectionFactory) : ICarsBillingRepository
{
    public void Save(Guid importBatchId, Guid importedFileId, CarsBillingEntry entry) =>
        SaveMany(importBatchId, importedFileId, [entry]);

    public void SaveMany(Guid importBatchId, Guid importedFileId, IEnumerable<CarsBillingEntry> entries)
    {
        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        foreach (var entry in entries)
        {
            Insert(connection, transaction, importBatchId, importedFileId, entry);
        }

        transaction.Commit();
    }

    public CarsBillingEntry? GetById(Guid id)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = CarsBillingSelectSql + " WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadCarsBillingEntry(reader) : null;
    }

    public IReadOnlyList<CarsBillingEntry> ListByImportBatch(Guid importBatchId)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = CarsBillingSelectSql + " WHERE ImportBatchId = $importBatchId ORDER BY BillingDate, Id;";
        command.Parameters.AddWithValue("$importBatchId", importBatchId.ToString());

        using var reader = command.ExecuteReader();
        var entries = new List<CarsBillingEntry>();
        while (reader.Read())
        {
            entries.Add(ReadCarsBillingEntry(reader));
        }

        return entries;
    }

    private static void Insert(SqliteConnection connection, SqliteTransaction transaction, Guid importBatchId, Guid importedFileId, CarsBillingEntry entry)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO CarsBillingEntries (
                Id, ImportBatchId, ImportedFileId, PeriodId, BranchId, BillingDate,
                RawRentalAgreementNumber, NormalisedRentalAgreementNumber, RawRego, NormalisedRego,
                RawBilledLitres, NormalisedBilledLitres, RawBilledAmount, NormalisedBilledAmount,
                BillingStatus, SourceFile, SourceSheet, SourceRowNumber, SourcePageNumber, SourceReferenceText,
                ValidationIssueCodes, CreatedAtUtc
            )
            VALUES (
                $id, $importBatchId, $importedFileId, $periodId, $branchId, $billingDate,
                $rawRentalAgreementNumber, $normalisedRentalAgreementNumber, $rawRego, $normalisedRego,
                $rawBilledLitres, $normalisedBilledLitres, $rawBilledAmount, $normalisedBilledAmount,
                $billingStatus, $sourceFile, $sourceSheet, $sourceRowNumber, $sourcePageNumber, $sourceReferenceText,
                $validationIssueCodes, $createdAtUtc
            );
            """;
        command.Parameters.AddWithValue("$id", entry.Id.ToString());
        command.Parameters.AddWithValue("$importBatchId", importBatchId.ToString());
        command.Parameters.AddWithValue("$importedFileId", importedFileId.ToString());
        command.Parameters.AddWithValue("$periodId", SqliteRepositoryHelpers.ToPeriodId(entry.Period));
        command.Parameters.AddWithNullableValue("$branchId", entry.BranchId?.Value);
        command.Parameters.AddWithNullableValue("$billingDate", entry.Date is null ? null : SqliteRepositoryHelpers.ToIsoDateString(entry.Date.Value));
        command.Parameters.AddWithNullableValue("$rawRentalAgreementNumber", entry.RentalAgreementNumber?.RawValue);
        command.Parameters.AddWithNullableValue("$normalisedRentalAgreementNumber", entry.RentalAgreementNumber?.NormalisedValue);
        command.Parameters.AddWithNullableValue("$rawRego", entry.Rego?.RawValue);
        command.Parameters.AddWithNullableValue("$normalisedRego", entry.Rego?.NormalisedValue);
        command.Parameters.AddWithNullableValue("$rawBilledLitres", entry.BilledLitres?.ToString());
        command.Parameters.AddWithNullableValue("$normalisedBilledLitres", entry.BilledLitres?.Value);
        command.Parameters.AddWithNullableValue("$rawBilledAmount", entry.BilledAmount?.ToString());
        command.Parameters.AddWithNullableValue("$normalisedBilledAmount", entry.BilledAmount?.Value);
        command.Parameters.AddWithNullableValue("$billingStatus", entry.BillingStatus);
        SqliteRepositoryHelpers.AddSourceReferenceParameters(command, entry.SourceReference);
        command.Parameters.AddWithValue("$validationIssueCodes", SqliteRepositoryHelpers.ToJson(entry.ValidationIssueCodes));
        command.Parameters.AddWithValue("$createdAtUtc", SqliteRepositoryHelpers.ToIsoString(DateTimeOffset.UtcNow));
        command.ExecuteNonQuery();
    }

    private const string CarsBillingSelectSql = """
        SELECT Id, PeriodId, BranchId, BillingDate, RawRentalAgreementNumber, RawRego, NormalisedBilledLitres,
               NormalisedBilledAmount, BillingStatus, SourceFile, SourceSheet, SourceRowNumber, SourcePageNumber,
               SourceReferenceText, ValidationIssueCodes
        FROM CarsBillingEntries
        """;

    private static CarsBillingEntry ReadCarsBillingEntry(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            FuelPeriod.Parse(reader.GetString(1)),
            SqliteRepositoryHelpers.ReadSourceReference(reader, 9),
            reader.GetNullableString(2) is { } branchId ? new CanonicalBranchId(branchId) : null,
            reader.GetNullableString(3) is { } date ? DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
            reader.GetNullableString(4) is { } ra ? new RentalAgreementNumber(ra) : null,
            reader.GetNullableString(5) is { } rego ? new Rego(rego) : null,
            reader.IsDBNull(6) ? null : new Litres(reader.GetDecimalValue(6)),
            reader.IsDBNull(7) ? null : new MoneyAmount(reader.GetDecimalValue(7)),
            reader.GetNullableString(8),
            SqliteRepositoryHelpers.FromJson(reader.GetNullableString(14)));
}

internal static class SqliteRepositoryHelpers
{
    internal static string ToPeriodId(FuelPeriod period) => period.ToSortableString();

    internal static string ToIsoDateString(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal static string ToIsoString(DateTimeOffset timestamp) => timestamp.ToString("O", CultureInfo.InvariantCulture);

    internal static void EnsurePeriod(SqliteConnection connection, SqliteTransaction transaction, FuelPeriod period)
    {
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
        command.Parameters.AddWithValue("$id", ToPeriodId(period));
        command.Parameters.AddWithValue("$year", period.Year);
        command.Parameters.AddWithValue("$month", period.Month);
        command.Parameters.AddWithValue("$lifecycleStatus", PeriodLifecycleStatus.Draft.ToString());
        command.Parameters.AddWithValue("$createdAtUtc", ToIsoString(DateTimeOffset.UtcNow));
        command.ExecuteNonQuery();
    }

    internal static void AddSourceReferenceParameters(SqliteCommand command, SourceReference sourceReference)
    {
        command.Parameters.AddWithValue("$sourceFile", sourceReference.SourceFile);
        command.Parameters.AddWithNullableValue("$sourceSheet", sourceReference.SheetName);
        command.Parameters.AddWithNullableValue("$sourceRowNumber", sourceReference.RowNumber);
        command.Parameters.AddWithNullableValue("$sourcePageNumber", sourceReference.PageNumber);
        command.Parameters.AddWithNullableValue("$sourceReferenceText", sourceReference.ReferenceText);
    }

    internal static SourceReference ReadSourceReference(SqliteDataReader reader, int sourceFileOrdinal) =>
        new(
            reader.GetString(sourceFileOrdinal),
            reader.GetNullableString(sourceFileOrdinal + 1),
            reader.GetNullableInt32(sourceFileOrdinal + 2),
            reader.GetNullableInt32(sourceFileOrdinal + 3),
            reader.GetNullableString(sourceFileOrdinal + 4));

    internal static string ToJson(IReadOnlyList<string> values) => JsonSerializer.Serialize(values);

    internal static IReadOnlyList<string> FromJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return JsonSerializer.Deserialize<string[]>(value) ?? Array.Empty<string>();
    }
}

internal static class SqliteCommandExtensions
{
    internal static void AddWithNullableValue(this SqliteParameterCollection parameters, string name, object? value) =>
        parameters.AddWithValue(name, value ?? DBNull.Value);
}

internal static class SqliteDataReaderExtensions
{
    internal static string? GetNullableString(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    internal static int? GetNullableInt32(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    internal static DateTimeOffset? GetNullableDateTimeOffset(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    internal static decimal GetDecimalValue(this SqliteDataReader reader, int ordinal) =>
        Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
}
