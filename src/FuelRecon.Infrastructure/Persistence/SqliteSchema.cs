namespace FuelRecon.Infrastructure.Persistence;

/// <summary>
/// Initial SQLite schema for the local Fuel Reconciliation Manager database.
/// </summary>
public static class SqliteSchema
{
    public const string InitialSchema = """
        PRAGMA foreign_keys = ON;

        CREATE TABLE IF NOT EXISTS Periods (
            Id TEXT PRIMARY KEY,
            Year INTEGER NOT NULL,
            Month INTEGER NOT NULL,
            LifecycleStatus TEXT NOT NULL,
            CreatedAtUtc TEXT NOT NULL,
            UpdatedAtUtc TEXT,
            ClosedAtUtc TEXT,
            ReopenedAtUtc TEXT,
            ReopenReason TEXT,
            UNIQUE (Year, Month)
        );

        CREATE TABLE IF NOT EXISTS SettingsSnapshots (
            Id TEXT PRIMARY KEY,
            CreatedAtUtc TEXT NOT NULL,
            CreatedBy TEXT NOT NULL,
            SnapshotJson TEXT NOT NULL,
            Description TEXT
        );

        CREATE TABLE IF NOT EXISTS ImportBatches (
            Id TEXT PRIMARY KEY,
            PeriodId TEXT NOT NULL,
            CreatedAtUtc TEXT NOT NULL,
            CreatedBy TEXT NOT NULL,
            Status TEXT NOT NULL,
            SourceDescription TEXT,
            SettingsSnapshotId TEXT,
            FOREIGN KEY (PeriodId) REFERENCES Periods (Id) ON DELETE RESTRICT,
            FOREIGN KEY (SettingsSnapshotId) REFERENCES SettingsSnapshots (Id) ON DELETE RESTRICT
        );

        CREATE TABLE IF NOT EXISTS ImportedFiles (
            Id TEXT PRIMARY KEY,
            ImportBatchId TEXT NOT NULL,
            PeriodId TEXT NOT NULL,
            InputSlot TEXT NOT NULL,
            OriginalFileName TEXT NOT NULL,
            StoredFilePath TEXT,
            FileStatus TEXT NOT NULL,
            ChecksumAlgorithm TEXT NOT NULL,
            ChecksumValue TEXT NOT NULL,
            ParserName TEXT,
            ParserVersion TEXT,
            ImportedAtUtc TEXT NOT NULL,
            CompletedAtUtc TEXT,
            FailureReasonCode TEXT,
            FailureMessage TEXT,
            FOREIGN KEY (ImportBatchId) REFERENCES ImportBatches (Id) ON DELETE RESTRICT,
            FOREIGN KEY (PeriodId) REFERENCES Periods (Id) ON DELETE RESTRICT
        );

        CREATE TABLE IF NOT EXISTS ValidationResults (
            Id TEXT PRIMARY KEY,
            ImportedFileId TEXT,
            ImportBatchId TEXT,
            SourceTable TEXT,
            SourceRecordId TEXT,
            Severity TEXT NOT NULL,
            ReasonCode TEXT NOT NULL,
            Message TEXT NOT NULL,
            SourceFile TEXT,
            SourceSheet TEXT,
            SourceRowNumber INTEGER,
            SourcePageNumber INTEGER,
            SourceReferenceText TEXT,
            CreatedAtUtc TEXT NOT NULL,
            FOREIGN KEY (ImportedFileId) REFERENCES ImportedFiles (Id) ON DELETE RESTRICT,
            FOREIGN KEY (ImportBatchId) REFERENCES ImportBatches (Id) ON DELETE RESTRICT
        );

        CREATE TABLE IF NOT EXISTS SupplierTransactions (
            Id TEXT PRIMARY KEY,
            ImportBatchId TEXT NOT NULL,
            ImportedFileId TEXT NOT NULL,
            PeriodId TEXT NOT NULL,
            SupplierName TEXT NOT NULL,
            BranchId TEXT,
            TransactionDate TEXT NOT NULL,
            RawBranchText TEXT,
            RawSiteText TEXT,
            Cardholder TEXT,
            VoucherOrInvoiceReference TEXT,
            Product TEXT,
            RawLitres TEXT,
            NormalisedLitres REAL NOT NULL,
            RawAmount TEXT,
            NormalisedAmount REAL,
            SourceFile TEXT NOT NULL,
            SourceSheet TEXT,
            SourceRowNumber INTEGER,
            SourcePageNumber INTEGER,
            SourceReferenceText TEXT,
            ValidationIssueCodes TEXT,
            CreatedAtUtc TEXT NOT NULL,
            FOREIGN KEY (ImportBatchId) REFERENCES ImportBatches (Id) ON DELETE RESTRICT,
            FOREIGN KEY (ImportedFileId) REFERENCES ImportedFiles (Id) ON DELETE RESTRICT,
            FOREIGN KEY (PeriodId) REFERENCES Periods (Id) ON DELETE RESTRICT
        );

        CREATE TABLE IF NOT EXISTS BranchLitresEntries (
            Id TEXT PRIMARY KEY,
            ImportBatchId TEXT NOT NULL,
            ImportedFileId TEXT NOT NULL,
            PeriodId TEXT NOT NULL,
            BranchId TEXT NOT NULL,
            EntryDate TEXT NOT NULL,
            RawRentalAgreementNumber TEXT,
            NormalisedRentalAgreementNumber TEXT,
            RawRego TEXT,
            NormalisedRego TEXT,
            NoteOrReference TEXT,
            RawLitres TEXT,
            NormalisedLitres REAL NOT NULL,
            SourceFile TEXT NOT NULL,
            SourceSheet TEXT,
            SourceRowNumber INTEGER,
            SourcePageNumber INTEGER,
            SourceReferenceText TEXT,
            ValidationIssueCodes TEXT,
            CreatedAtUtc TEXT NOT NULL,
            FOREIGN KEY (ImportBatchId) REFERENCES ImportBatches (Id) ON DELETE RESTRICT,
            FOREIGN KEY (ImportedFileId) REFERENCES ImportedFiles (Id) ON DELETE RESTRICT,
            FOREIGN KEY (PeriodId) REFERENCES Periods (Id) ON DELETE RESTRICT
        );

        CREATE TABLE IF NOT EXISTS CarsBillingEntries (
            Id TEXT PRIMARY KEY,
            ImportBatchId TEXT NOT NULL,
            ImportedFileId TEXT NOT NULL,
            PeriodId TEXT NOT NULL,
            BranchId TEXT,
            BillingDate TEXT,
            RawRentalAgreementNumber TEXT,
            NormalisedRentalAgreementNumber TEXT,
            RawRego TEXT,
            NormalisedRego TEXT,
            RawBilledLitres TEXT,
            NormalisedBilledLitres REAL,
            RawBilledAmount TEXT,
            NormalisedBilledAmount REAL,
            BillingStatus TEXT,
            SourceFile TEXT NOT NULL,
            SourceSheet TEXT,
            SourceRowNumber INTEGER,
            SourcePageNumber INTEGER,
            SourceReferenceText TEXT,
            ValidationIssueCodes TEXT,
            CreatedAtUtc TEXT NOT NULL,
            FOREIGN KEY (ImportBatchId) REFERENCES ImportBatches (Id) ON DELETE RESTRICT,
            FOREIGN KEY (ImportedFileId) REFERENCES ImportedFiles (Id) ON DELETE RESTRICT,
            FOREIGN KEY (PeriodId) REFERENCES Periods (Id) ON DELETE RESTRICT
        );

        CREATE TABLE IF NOT EXISTS ReconciliationRuns (
            Id TEXT PRIMARY KEY,
            PeriodId TEXT NOT NULL,
            CreatedAtUtc TEXT NOT NULL,
            CreatedBy TEXT NOT NULL,
            Status TEXT NOT NULL,
            SettingsSnapshotId TEXT,
            InputFileChecksums TEXT NOT NULL,
            CompletedAtUtc TEXT,
            FailedAtUtc TEXT,
            FailureReasonCode TEXT,
            TotalItemCount INTEGER NOT NULL DEFAULT 0,
            MatchedItemCount INTEGER NOT NULL DEFAULT 0,
            ReviewRequiredCount INTEGER NOT NULL DEFAULT 0,
            EstimatedRecoveryTotal REAL,
            FOREIGN KEY (PeriodId) REFERENCES Periods (Id) ON DELETE RESTRICT,
            FOREIGN KEY (SettingsSnapshotId) REFERENCES SettingsSnapshots (Id) ON DELETE RESTRICT
        );

        CREATE TABLE IF NOT EXISTS ReconciliationItems (
            Id TEXT PRIMARY KEY,
            RunId TEXT NOT NULL,
            PeriodId TEXT NOT NULL,
            BranchId TEXT,
            SystemStatus TEXT NOT NULL,
            ResolutionStatus TEXT NOT NULL,
            FinalStatus TEXT,
            ConfidenceBucket TEXT NOT NULL,
            ReasonCodes TEXT NOT NULL,
            HumanReadableReason TEXT,
            SupplierTransactionId TEXT,
            BranchLitresEntryId TEXT,
            CarsBillingEntryId TEXT,
            SupplierSourceFile TEXT,
            SupplierSourceSheet TEXT,
            SupplierSourceRowNumber INTEGER,
            SupplierSourcePageNumber INTEGER,
            SupplierSourceReferenceText TEXT,
            BranchSourceFile TEXT,
            BranchSourceSheet TEXT,
            BranchSourceRowNumber INTEGER,
            BranchSourcePageNumber INTEGER,
            BranchSourceReferenceText TEXT,
            CarsSourceFile TEXT,
            CarsSourceSheet TEXT,
            CarsSourceRowNumber INTEGER,
            CarsSourcePageNumber INTEGER,
            CarsSourceReferenceText TEXT,
            MatchCandidatesJson TEXT,
            LitresVariance REAL,
            AmountVariance REAL,
            CreatedAtUtc TEXT NOT NULL,
            FOREIGN KEY (RunId) REFERENCES ReconciliationRuns (Id) ON DELETE RESTRICT,
            FOREIGN KEY (PeriodId) REFERENCES Periods (Id) ON DELETE RESTRICT,
            FOREIGN KEY (SupplierTransactionId) REFERENCES SupplierTransactions (Id) ON DELETE RESTRICT,
            FOREIGN KEY (BranchLitresEntryId) REFERENCES BranchLitresEntries (Id) ON DELETE RESTRICT,
            FOREIGN KEY (CarsBillingEntryId) REFERENCES CarsBillingEntries (Id) ON DELETE RESTRICT
        );

        CREATE TABLE IF NOT EXISTS ManualActions (
            Id TEXT PRIMARY KEY,
            RunId TEXT NOT NULL,
            ReconciliationItemId TEXT NOT NULL,
            ActionType TEXT NOT NULL,
            CreatedAtUtc TEXT NOT NULL,
            CreatedBy TEXT NOT NULL,
            Note TEXT NOT NULL,
            OldResolutionStatus TEXT,
            NewResolutionStatus TEXT,
            OldValuesJson TEXT,
            NewValuesJson TEXT,
            CorrelationId TEXT,
            FOREIGN KEY (RunId) REFERENCES ReconciliationRuns (Id) ON DELETE RESTRICT,
            FOREIGN KEY (ReconciliationItemId) REFERENCES ReconciliationItems (Id) ON DELETE RESTRICT
        );

        CREATE TABLE IF NOT EXISTS BranchReports (
            Id TEXT PRIMARY KEY,
            RunId TEXT NOT NULL,
            PeriodId TEXT NOT NULL,
            BranchId TEXT NOT NULL,
            VersionNumber INTEGER NOT NULL,
            CreatedAtUtc TEXT NOT NULL,
            CreatedBy TEXT NOT NULL,
            Status TEXT NOT NULL,
            Notes TEXT,
            SupplierLitres REAL,
            BranchLitres REAL,
            BilledLitres REAL,
            UnbilledLitres REAL,
            EstimatedRecovery REAL,
            ReviewCount INTEGER NOT NULL DEFAULT 0,
            ApprovedAtUtc TEXT,
            ApprovedBy TEXT,
            UNIQUE (RunId, BranchId, VersionNumber),
            FOREIGN KEY (RunId) REFERENCES ReconciliationRuns (Id) ON DELETE RESTRICT,
            FOREIGN KEY (PeriodId) REFERENCES Periods (Id) ON DELETE RESTRICT
        );

        CREATE TABLE IF NOT EXISTS PdfExports (
            Id TEXT PRIMARY KEY,
            BranchReportId TEXT NOT NULL,
            ExportedAtUtc TEXT NOT NULL,
            ExportedBy TEXT NOT NULL,
            Status TEXT NOT NULL,
            FilePath TEXT,
            TemplateName TEXT,
            TemplateVersion TEXT,
            ErrorCategory TEXT,
            ErrorMessage TEXT,
            CorrelationId TEXT,
            ExportSettingsSnapshot TEXT,
            FOREIGN KEY (BranchReportId) REFERENCES BranchReports (Id) ON DELETE RESTRICT
        );

        CREATE TABLE IF NOT EXISTS AuditRecords (
            Id TEXT PRIMARY KEY,
            CreatedAtUtc TEXT NOT NULL,
            Actor TEXT NOT NULL,
            ActionType TEXT NOT NULL,
            EntityType TEXT NOT NULL,
            EntityId TEXT NOT NULL,
            Origin TEXT NOT NULL,
            OldValuesJson TEXT,
            NewValuesJson TEXT,
            Note TEXT,
            ReasonCode TEXT,
            CorrelationId TEXT,
            ContextJson TEXT
        );

        CREATE INDEX IF NOT EXISTS IX_ImportBatches_PeriodId ON ImportBatches (PeriodId);
        CREATE INDEX IF NOT EXISTS IX_ImportedFiles_ImportBatchId ON ImportedFiles (ImportBatchId);
        CREATE INDEX IF NOT EXISTS IX_ValidationResults_ImportedFileId ON ValidationResults (ImportedFileId);
        CREATE INDEX IF NOT EXISTS IX_SupplierTransactions_PeriodBranchDate ON SupplierTransactions (PeriodId, BranchId, TransactionDate);
        CREATE INDEX IF NOT EXISTS IX_BranchLitresEntries_RA ON BranchLitresEntries (PeriodId, BranchId, NormalisedRentalAgreementNumber);
        CREATE INDEX IF NOT EXISTS IX_BranchLitresEntries_RegoDate ON BranchLitresEntries (PeriodId, BranchId, NormalisedRego, EntryDate);
        CREATE INDEX IF NOT EXISTS IX_CarsBillingEntries_RA ON CarsBillingEntries (PeriodId, BranchId, NormalisedRentalAgreementNumber);
        CREATE INDEX IF NOT EXISTS IX_CarsBillingEntries_RegoDate ON CarsBillingEntries (PeriodId, BranchId, NormalisedRego, BillingDate);
        CREATE INDEX IF NOT EXISTS IX_ReconciliationRuns_PeriodId ON ReconciliationRuns (PeriodId);
        CREATE INDEX IF NOT EXISTS IX_ReconciliationItems_RunId ON ReconciliationItems (RunId);
        CREATE INDEX IF NOT EXISTS IX_ReconciliationItems_Status ON ReconciliationItems (SystemStatus, ResolutionStatus);
        CREATE INDEX IF NOT EXISTS IX_ManualActions_ItemId ON ManualActions (ReconciliationItemId);
        CREATE INDEX IF NOT EXISTS IX_BranchReports_RunBranch ON BranchReports (RunId, BranchId);
        CREATE INDEX IF NOT EXISTS IX_PdfExports_BranchReportId ON PdfExports (BranchReportId);
        CREATE INDEX IF NOT EXISTS IX_AuditRecords_Entity ON AuditRecords (EntityType, EntityId);

        CREATE TRIGGER IF NOT EXISTS TR_ReconciliationRuns_PreventUpdate
        BEFORE UPDATE ON ReconciliationRuns
        BEGIN
            SELECT RAISE(ABORT, 'ReconciliationRuns are append-only');
        END;

        CREATE TRIGGER IF NOT EXISTS TR_ReconciliationRuns_PreventDelete
        BEFORE DELETE ON ReconciliationRuns
        BEGIN
            SELECT RAISE(ABORT, 'ReconciliationRuns are append-only');
        END;

        CREATE TRIGGER IF NOT EXISTS TR_BranchReports_PreventUpdate
        BEFORE UPDATE ON BranchReports
        BEGIN
            SELECT RAISE(ABORT, 'BranchReports are append-only');
        END;

        CREATE TRIGGER IF NOT EXISTS TR_BranchReports_PreventDelete
        BEFORE DELETE ON BranchReports
        BEGIN
            SELECT RAISE(ABORT, 'BranchReports are append-only');
        END;

        CREATE TRIGGER IF NOT EXISTS TR_PdfExports_PreventUpdate
        BEFORE UPDATE ON PdfExports
        BEGIN
            SELECT RAISE(ABORT, 'PdfExports are append-only');
        END;

        CREATE TRIGGER IF NOT EXISTS TR_PdfExports_PreventDelete
        BEFORE DELETE ON PdfExports
        BEGIN
            SELECT RAISE(ABORT, 'PdfExports are append-only');
        END;

        CREATE TRIGGER IF NOT EXISTS TR_AuditRecords_PreventUpdate
        BEFORE UPDATE ON AuditRecords
        BEGIN
            SELECT RAISE(ABORT, 'AuditRecords are append-only');
        END;

        CREATE TRIGGER IF NOT EXISTS TR_AuditRecords_PreventDelete
        BEFORE DELETE ON AuditRecords
        BEGIN
            SELECT RAISE(ABORT, 'AuditRecords are append-only');
        END;
        """;

    public const string Migration002BranchReportNotesAndApprovals = """
        CREATE TABLE IF NOT EXISTS BranchReportNotes (
            Id TEXT PRIMARY KEY,
            BranchReportId TEXT NOT NULL,
            CreatedAtUtc TEXT NOT NULL,
            CreatedBy TEXT NOT NULL,
            NoteText TEXT NOT NULL,
            ReasonCode TEXT,
            FOREIGN KEY (BranchReportId) REFERENCES BranchReports (Id) ON DELETE RESTRICT
        );

        CREATE INDEX IF NOT EXISTS IX_BranchReportNotes_BranchReportId ON BranchReportNotes (BranchReportId, CreatedAtUtc);

        CREATE TABLE IF NOT EXISTS BranchReportApprovals (
            Id TEXT PRIMARY KEY,
            BranchReportId TEXT NOT NULL,
            RunId TEXT NOT NULL,
            ApprovedAtUtc TEXT NOT NULL,
            ApprovedBy TEXT NOT NULL,
            ApprovalNote TEXT,
            SnapshotJson TEXT NOT NULL,
            FOREIGN KEY (BranchReportId) REFERENCES BranchReports (Id) ON DELETE RESTRICT,
            FOREIGN KEY (RunId) REFERENCES ReconciliationRuns (Id) ON DELETE RESTRICT
        );

        CREATE UNIQUE INDEX IF NOT EXISTS UX_BranchReportApprovals_BranchReportId ON BranchReportApprovals (BranchReportId);

        CREATE TRIGGER IF NOT EXISTS TR_BranchReportNotes_PreventUpdate
        BEFORE UPDATE ON BranchReportNotes
        BEGIN
            SELECT RAISE(ABORT, 'BranchReportNotes are append-only');
        END;

        CREATE TRIGGER IF NOT EXISTS TR_BranchReportNotes_PreventDelete
        BEFORE DELETE ON BranchReportNotes
        BEGIN
            SELECT RAISE(ABORT, 'BranchReportNotes are append-only');
        END;

        CREATE TRIGGER IF NOT EXISTS TR_BranchReportApprovals_PreventUpdate
        BEFORE UPDATE ON BranchReportApprovals
        BEGIN
            SELECT RAISE(ABORT, 'BranchReportApprovals are append-only');
        END;

        CREATE TRIGGER IF NOT EXISTS TR_BranchReportApprovals_PreventDelete
        BEFORE DELETE ON BranchReportApprovals
        BEGIN
            SELECT RAISE(ABORT, 'BranchReportApprovals are append-only');
        END;
        """;
}
