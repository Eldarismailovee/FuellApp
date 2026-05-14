# Fuel Reconciliation Manager — MVP v1 Backlog

Version: 1.0  
Status: Draft for Cursor/Codex implementation  
Target acceptance dataset: April/May 2026 agreed customer files  
Primary stack: C# / .NET 10 LTS, WinUI 3, Windows App SDK 1.8.x stable, SQLite, ClosedXML, PdfPig, MigraDoc/PDFsharp, Serilog, FluentValidation, xUnit

---

## How to use this backlog with Cursor/Codex

Use this file as the implementation queue. Each task is intentionally small enough to fit a focused coding session.

Recommended workflow:

1. Pick one task only.
2. Copy the task description and acceptance criteria into Cursor/Codex.
3. Ask it to implement only that task.
4. Run tests.
5. Commit.
6. Move to the next task.

Do not let AI implement out-of-scope features unless the task explicitly asks for them.

---

## Implementation Principles

- Build backend/domain first, UI second.
- Keep `FuelRecon.Domain` free of UI and infrastructure dependencies.
- Use deterministic parsing, normalisation and reconciliation.
- Preserve raw source values and normalised values.
- Never overwrite historical reconciliation runs, reports or PDF export records.
- Use SQLite transactions for critical writes.
- Use machine-readable reason codes for validation and reconciliation issues.
- Treat imported Excel/PDF files as untrusted input.
- Use Windows username for MVP audit identity unless full user accounts are added later.
- Use sample files and golden-file tests as the source of truth for parser/reconciliation correctness.

---

# EPIC-01 — Project Skeleton

Goal: create the .NET solution structure, baseline projects and development conventions.

## TASK-01.01 Create solution structure

Estimate: 1–2h  
Priority: P0  
Depends on: none

Create:

```text
FuelRecon.sln
src/
  FuelRecon.Domain/
  FuelRecon.Application/
  FuelRecon.Infrastructure/
  FuelRecon.Desktop/
tests/
  FuelRecon.Tests/
  FuelRecon.GoldenFiles/
docs/
samples/
installer/
```

Acceptance criteria:

- Solution builds from command line.
- Projects reference each other correctly:
  - Desktop → Application
  - Application → Domain
  - Infrastructure → Application + Domain
  - Tests → Domain/Application/Infrastructure as needed
- Domain does not reference Application, Infrastructure or Desktop.

Cursor/Codex prompt:

```text
Create a .NET 10 solution for Fuel Reconciliation Manager using Clean Architecture.
Create projects FuelRecon.Domain, FuelRecon.Application, FuelRecon.Infrastructure, FuelRecon.Desktop, FuelRecon.Tests and FuelRecon.GoldenFiles.
Set project references so Domain has no dependencies, Application references Domain, Infrastructure references Application and Domain, Desktop references Application and Infrastructure where needed.
Do not implement business logic yet.
```

---

## TASK-01.02 Add baseline packages

Estimate: 1–2h  
Priority: P0  
Depends on: TASK-01.01

Packages:

- CommunityToolkit.Mvvm
- Microsoft.Data.Sqlite
- ClosedXML
- ExcelDataReader
- UglyToad.PdfPig
- PDFsharp / MigraDoc
- Serilog
- FluentValidation
- xUnit
- FluentAssertions or Shouldly

Acceptance criteria:

- Packages installed in correct projects.
- Solution builds.
- No UI grid component added yet unless license decision is confirmed.

---

## TASK-01.03 Add repository coding rules

Estimate: 1h  
Priority: P0  
Depends on: TASK-01.01

Create:

```text
.cursor/rules/fuel-recon.md
docs/coding-standards.md
```

Acceptance criteria:

- Rules mention deterministic reconciliation.
- Rules mention no historical overwrite.
- Rules mention raw + normalised values.
- Rules mention out-of-scope guardrails.

---

# EPIC-02 — Domain Model

Goal: create domain entities, enums and value objects before infrastructure/UI.

## TASK-02.01 Create core enums

Estimate: 1–2h  
Priority: P0  
Depends on: EPIC-01

Create enums:

- InputSlot
- FileStatus
- ValidationSeverity
- MappingResultState
- ReconciliationStatus
- ResolutionStatus
- ConfidenceBucket
- PeriodLifecycleStatus
- AuditActionType
- AuditEntityType
- PdfExportStatus

Acceptance criteria:

- Enums compile.
- Names align with requirements.
- Unit test verifies enum parsing/string output where needed.

---

## TASK-02.02 Create core value objects

Estimate: 2–3h  
Priority: P0  
Depends on: TASK-02.01

Create value objects:

- FuelPeriod
- CanonicalBranchId
- Rego
- RentalAgreementNumber
- MoneyAmount
- Litres
- SourceReference
- CorrelationId
- FileChecksum

Acceptance criteria:

- Value objects validate inputs where useful.
- Value objects are immutable.
- Unit tests cover basic construction and invalid values.

---

## TASK-02.03 Create source row models

Estimate: 2–4h  
Priority: P0  
Depends on: TASK-02.02

Create domain models:

- SupplierTransaction
- BranchLitresEntry
- CarsBillingEntry

Each should store:

- raw values
- normalised values
- source references
- branch/period where resolved
- validation state or reason codes where applicable

Acceptance criteria:

- Domain models do not depend on pandas/PDF/Excel/SQLite/UI.
- Unit tests confirm raw and normalised values can coexist.

---

## TASK-02.04 Create reconciliation result models

Estimate: 2–4h  
Priority: P0  
Depends on: TASK-02.03

Create:

- ReconciliationRun
- ReconciliationItem
- MatchCandidate
- MatchGroup
- BranchSummary
- ManualAction
- BranchReportVersion
- PdfExportRecord

Acceptance criteria:

- ReconciliationItem has system status and final/override status separated.
- ReconciliationItem stores reason codes.
- ReconciliationRun is immutable after completion conceptually.

---

# EPIC-03 — SQLite Persistence

Goal: create local database schema and repository interfaces.

## TASK-03.01 Define database schema

Estimate: 3–4h  
Priority: P0  
Depends on: EPIC-02

Create SQL schema/migrations for:

- Periods
- ImportBatches
- ImportedFiles
- ValidationResults
- SupplierTransactions
- BranchLitresEntries
- CarsBillingEntries
- ReconciliationRuns
- ReconciliationItems
- ManualActions
- BranchReports
- PdfExports
- AuditRecords
- SettingsSnapshots

Acceptance criteria:

- SQLite database can be created from empty state.
- Tables include IDs, timestamps and foreign keys where practical.
- Runs and exports are append-only by design.

---

## TASK-03.02 Implement database connection and migration runner

Estimate: 2–3h  
Priority: P0  
Depends on: TASK-03.01

Acceptance criteria:

- App can create local SQLite DB.
- Migrations run once.
- DB path is configurable for tests.

---

## TASK-03.03 Implement repositories for imports and parsed rows

Estimate: 3–4h  
Priority: P0  
Depends on: TASK-03.02

Repositories:

- IImportBatchRepository
- IImportedFileRepository
- ISupplierTransactionRepository
- IBranchLitresRepository
- ICarsBillingRepository

Acceptance criteria:

- Save and retrieve import batch.
- Save and retrieve parsed rows.
- Tests use temporary SQLite database.

---

## TASK-03.04 Implement repositories for runs/reports/audit

Estimate: 3–4h  
Priority: P0  
Depends on: TASK-03.02

Repositories:

- IReconciliationRunRepository
- IReconciliationItemRepository
- IBranchReportRepository
- IPdfExportRepository
- IAuditRepository
- ISettingsSnapshotRepository

Acceptance criteria:

- Can save immutable reconciliation run.
- Can query latest run for period.
- Can write and query audit records.

---

# EPIC-04 — Normalisation

Goal: implement deterministic normalisation before parser/reconciliation logic.

## TASK-04.01 Implement RA normaliser

Estimate: 1–2h  
Priority: P0  
Depends on: EPIC-02

Rules:

- Preserve raw RA.
- Normalise according to configured RA rule.
- Numeric-only rule can remove spaces, hyphens, `.0` and non-digit chars.
- Alphanumeric rule preserves letters/digits and removes formatting only.

Acceptance criteria:

- Unit tests for numeric RA.
- Unit tests for alphanumeric RA.
- Invalid/empty result returns reason code `InvalidRA`.

---

## TASK-04.02 Implement rego normaliser

Estimate: 1h  
Priority: P0

Rules:

- Uppercase.
- Remove spaces/hyphens.
- Validate minimum pattern.

Acceptance criteria:

- `abc-123` → `ABC123`.
- Invalid rego returns `InvalidRego`.

---

## TASK-04.03 Implement date/number normalisation

Estimate: 2–3h  
Priority: P0

Implement:

- Excel serial date parsing.
- Text date parsing with New Zealand/English defaults.
- DateOnly output.
- Litres decimal parsing.
- Money amount parsing.

Acceptance criteria:

- Supports day-first dates.
- Supports `$`, commas and spaces in amounts.
- Rounds litres and money to 2 decimals by default.
- Invalid values return reason codes.

---

## TASK-04.04 Implement branch alias resolver

Estimate: 2–3h  
Priority: P0

Implement:

- branch master
- branch alias table
- case-insensitive matching
- whitespace-insensitive matching
- unresolved branch reason codes

Acceptance criteria:

- `Mobil Taupo`, `Mobile - Taupo`, `Hertz Taupo`, `Taupo` map to same canonical branch if configured.
- Alias cannot point to missing canonical branch ID.

---

# EPIC-05 — Excel Import

Goal: parse branch litres workbook and Cars+ export from agreed sample files.

## TASK-05.01 Inspect sample Excel files and document columns

Estimate: 1–2h  
Priority: P0  
Depends on: real sample files

Create:

```text
docs/sample-file-analysis/excel-samples.md
```

Include:

- workbook names
- sheet names
- columns
- required fields
- optional fields
- known layout quirks

Acceptance criteria:

- Branch workbook sheets documented.
- Cars+ export columns documented.
- Any ambiguous columns noted.

---

## TASK-05.02 Implement branch litres workbook parser

Estimate: 3–4h  
Priority: P0  
Depends on: TASK-05.01, EPIC-04

Acceptance criteria:

- Reads `.xlsx`.
- Supports agreed sheets/layouts.
- Extracts branch, date, litres and RA/rego/note where available.
- Stores source sheet/row.
- Produces parsed logical rows.
- Invalid/skipped rows include reason codes.

---

## TASK-05.03 Implement Cars+ export parser

Estimate: 3–4h  
Priority: P0  
Depends on: TASK-05.01, EPIC-04

Acceptance criteria:

- Reads `.xlsx`.
- Extracts date/period, RA or rego, billed litres or billed amount, status where available.
- Stores source sheet/row.
- Handles missing optional fields.
- Missing required fields fail validation.

---

## TASK-05.04 Add Excel parser golden output tests

Estimate: 2–3h  
Priority: P0  
Depends on: TASK-05.02, TASK-05.03

Acceptance criteria:

- Parser output exported to CSV/JSON.
- Golden expected files can be compared.
- Tests run in CI/local command line.

---

# EPIC-06 — PDF Import

Goal: parse Mobil and Farmlands/Caltex supplier PDF statements.

## TASK-06.01 Inspect supplier PDF layouts

Estimate: 2–3h  
Priority: P0  
Depends on: real sample PDFs

Create:

```text
docs/sample-file-analysis/pdf-samples.md
```

Document:

- Mobil statement period/invoice fields
- Mobil transaction patterns
- Farmlands/Caltex statement period
- Farmlands/Caltex transaction patterns
- duplicate/page-break behavior
- credits/refunds handling

Acceptance criteria:

- Supported layouts documented.
- Unsupported/scanned PDF assumptions documented.

---

## TASK-06.02 Implement supplier PDF layout detector

Estimate: 2h  
Priority: P0  
Depends on: TASK-06.01

Acceptance criteria:

- Detects Mobil.
- Detects Farmlands/Caltex.
- Unknown layout returns `UnsupportedPdfLayout`.
- Does not silently partially import unsupported PDFs.

---

## TASK-06.03 Implement Mobil PDF parser

Estimate: 3–4h  
Priority: P0  
Depends on: TASK-06.02, EPIC-04

Acceptance criteria:

- Extracts transaction date, site, voucher, litres, product where available, card/cardholder where available.
- Resolves branch via alias/cardholder/site rules where possible.
- Positive fuel transactions become SupplierTransaction rows.
- Source page/reference stored where available.

---

## TASK-06.04 Implement Farmlands/Caltex PDF parser

Estimate: 4h  
Priority: P0  
Depends on: TASK-06.02, EPIC-04

Acceptance criteria:

- Extracts date, invoice/reference, site, card number, product, litres, gross/net where available.
- Deduplicates repeated OCR/page-break lines.
- Credits/negative litres handled according to MVP rule.
- Source page/reference stored where available.

---

## TASK-06.05 Add PDF parser golden output tests

Estimate: 2–3h  
Priority: P0  
Depends on: TASK-06.03, TASK-06.04

Acceptance criteria:

- Mobil parser output is deterministic.
- Farmlands/Caltex parser output is deterministic.
- Golden CSV/JSON comparisons pass.

---

# EPIC-07 — Validation and Mapping

Goal: implement file-level and row-level validation.

## TASK-07.01 Implement validation result model and reason codes

Estimate: 2h  
Priority: P0  
Depends on: EPIC-02

Acceptance criteria:

- Supports Info, Warning, Error.
- Supports file-level and row-level validation.
- Stores machine-readable reason code and human-readable reason.

---

## TASK-07.02 Implement file-level validation

Estimate: 2–3h  
Priority: P0

Acceptance criteria:

- Supplier statement valid only if parser extracts at least one selected-period transaction.
- Branch workbook valid only if required fields are detected/mapped.
- Cars+ export valid only if required fields are detected/mapped.
- Blocking errors prevent reconciliation.

---

## TASK-07.03 Implement row-level validation

Estimate: 3–4h  
Priority: P0

Acceptance criteria:

- Invalid rows do not break full import unless threshold exceeded.
- Skipped/invalid rows have row/page/source reference.
- Zero valid rows blocks reconciliation.
- More than 20% row-level Error blocks reconciliation.

---

## TASK-07.04 Implement mapping template model

Estimate: 2–3h  
Priority: P1

Acceptance criteria:

- Stores file role, source/template name and version where available.
- Supports AutoMapped, TemplateMapped, ManualMapped and IncompleteMapping.
- Can save/reuse mappings.

---

# EPIC-08 — Reconciliation Engine

Goal: implement deterministic matching and status assignment.

## TASK-08.01 Implement matching rules snapshot

Estimate: 2h  
Priority: P0

Acceptance criteria:

- Stores date tolerance, litre tolerance, amount tolerance, variance threshold, average fuel price and confidence thresholds.
- Snapshot saved per run.

---

## TASK-08.02 Implement supplier-to-branch matching

Estimate: 3–4h  
Priority: P0

Acceptance criteria:

- Matches by canonical branch + date + litres.
- One-to-one creates candidate.
- Many-to-one/one-to-many becomes DuplicatePossible/ReviewRequired.
- Uses deterministic tie handling.

---

## TASK-08.03 Implement branch-to-Cars+ RA matching

Estimate: 3–4h  
Priority: P0

Acceptance criteria:

- Uses normalised RA as primary key.
- Checks period/branch/variance.
- Duplicate candidates go to review.
- Matched candidate preserves source references.

---

## TASK-08.04 Implement branch-to-Cars+ fallback matching

Estimate: 3–4h  
Priority: P0

Acceptance criteria:

- Uses rego + date tolerance + litres tolerance.
- Fallback confidence lower than RA match by default.
- Missing/invalid RA generates MissingRA where fallback insufficient.

---

## TASK-08.05 Implement reconciliation statuses

Estimate: 3–4h  
Priority: P0

Statuses:

- Matched
- Unbilled
- Variance
- DuplicatePossible
- MissingRA
- RegoMismatch
- SupplierOnly
- CarsOnly
- ReviewRequired

Acceptance criteria:

- Each non-matched exception has reason code.
- Review Queue trigger rules applied.
- Status assigned to ReconciliationItem, not raw row.

---

## TASK-08.06 Implement branch and period totals

Estimate: 2–3h  
Priority: P0

Acceptance criteria:

- Totals calculated from ReconciliationItems.
- Supplier litres, branch litres, billed litres, unbilled litres, variance and estimated recovery available.
- Totals are deterministic.

---

## TASK-08.07 Add reconciliation golden-file tests

Estimate: 3–4h  
Priority: P0

Acceptance criteria:

- Uses frozen dataset.
- Compares branch totals.
- Compares expected unbilled list.
- Compares expected variance/review list.
- Produces diff output when mismatch occurs.

---

# EPIC-09 — Audit and History

Goal: implement business audit and technical logging separately.

## TASK-09.01 Implement Serilog technical logging

Estimate: 1–2h  
Priority: P0

Acceptance criteria:

- Logs to local file.
- Includes timestamp, severity, operation, error category and correlation ID.
- Does not log unnecessary sensitive data.

---

## TASK-09.02 Implement audit service

Estimate: 2–3h  
Priority: P0

Acceptance criteria:

- Writes AuditRecords to SQLite.
- Mandatory fields: audit ID, timestamp, actor, action type, entity type, entity ID, origin.
- Contextual fields supported.

---

## TASK-09.03 Add audit events to import/reconciliation/export flows

Estimate: 3–4h  
Priority: P1

Acceptance criteria:

- Import created logged.
- Validation completed/failed logged.
- Reconciliation started/completed/failed logged.
- Manual actions logged.
- PDF export success/failure logged.

---

# EPIC-10 — Branch Report

Goal: create branch report read model and service before UI.

## TASK-10.01 Implement branch report service

Estimate: 3–4h  
Priority: P0

Acceptance criteria:

- Selects branch and run.
- Shows run context.
- Produces totals.
- Produces exception tables.
- Includes notes/prepared/approved metadata where available.

---

## TASK-10.02 Implement report versioning

Estimate: 2–3h  
Priority: P1

Acceptance criteria:

- Report version created for selected branch/run.
- Re-run or settings change creates new version.
- Old report versions remain viewable.

---

## TASK-10.03 Implement branch notes and approval records

Estimate: 2–3h  
Priority: P1

Acceptance criteria:

- Notes do not modify reconciliation data.
- Approval records preserve original system result.
- Approval with unresolved items requires note if policy allows.

---

# EPIC-11 — PDF Export

Goal: export branch reports to PDF using MigraDoc/PDFsharp.

## TASK-11.01 Implement PDF template service

Estimate: 2–3h  
Priority: P1

Acceptance criteria:

- Loads active template configuration.
- Stores template name/version.
- Missing template returns `TemplateNotFound`.

---

## TASK-11.02 Implement branch PDF export

Estimate: 3–4h  
Priority: P1

Acceptance criteria:

- Exports branch, period, run ID, report version and status.
- Includes totals and exception tables.
- Includes prepared/approved fields where applicable.
- PDF totals match Branch Report.

---

## TASK-11.03 Implement PDF export history

Estimate: 2–3h  
Priority: P1

Acceptance criteria:

- Each export attempt stored.
- Success/failure status stored.
- File path/reference stored.
- Export settings snapshot stored.

---

## TASK-11.04 Add PDF export smoke tests

Estimate: 2–3h  
Priority: P2

Acceptance criteria:

- PDF file generated.
- Basic metadata present.
- Failure cases logged.

---

# EPIC-12 — WinUI Shell and Screens

Goal: create Windows desktop UI after core workflows are testable.

## TASK-12.01 Create WinUI shell with NavigationView

Estimate: 2–3h  
Priority: P1

Screens:

- Dashboard
- Import Files
- Reconciliation
- Branch Reports
- Review Queue
- History/Audit
- Settings
- Help/User Guide

Acceptance criteria:

- Navigation works.
- MVVM structure used.
- No business logic in code-behind except view wiring.

---

## TASK-12.02 Implement Dashboard screen

Estimate: 3–4h  
Priority: P1

Acceptance criteria:

- Shows period overview.
- Shows input readiness.
- Shows KPI summary.
- Shows alerts.
- CTA depends on lifecycle status.

---

## TASK-12.03 Implement Import/Validate screen

Estimate: 4h  
Priority: P1

Acceptance criteria:

- Three file slots.
- Drag/drop and file picker.
- Validation summary.
- Preview first 10 rows.
- Missing columns and errors visible.

---

## TASK-12.04 Implement Reconciliation Detail screen

Estimate: 4h  
Priority: P1

Acceptance criteria:

- Shows reconciliation items.
- Filters by status/reason/branch.
- Shows details pane with source refs.
- Row height/font readable.

---

## TASK-12.05 Implement Review Queue screen

Estimate: 3–4h  
Priority: P1

Acceptance criteria:

- Shows unresolved work.
- Filters by branch/reason/severity/age.
- Open item detail.
- Mark resolved with note.
- Manual link flow stub or implementation.

---

## TASK-12.06 Implement Branch Report screen

Estimate: 3–4h  
Priority: P1

Acceptance criteria:

- Branch selector.
- Run/report context.
- Totals and exception tables.
- Notes.
- Export history.
- Export PDF button.

---

## TASK-12.07 Implement History/Audit screen

Estimate: 3–4h  
Priority: P2

Acceptance criteria:

- Filters by date/user/action/entity/branch/run.
- Drill-down where available.
- Audit records read-only.

---

## TASK-12.08 Implement Settings screen

Estimate: 3–4h  
Priority: P2

Acceptance criteria:

- Shows MVP settings only.
- Branch/supplier aliases.
- Tolerances/financial assumptions.
- Report/export defaults.
- Settings changes audited.

---

# EPIC-13 — Exception Resolution Workflow

Goal: implement user resolution and manual linking flows.

## TASK-13.01 Implement resolution status update

Estimate: 2–3h  
Priority: P1

Acceptance criteria:

- User can mark item Resolved with note.
- Original system status preserved.
- Manual action and audit record created.

---

## TASK-13.02 Implement manual linking

Estimate: 3–4h  
Priority: P1

Acceptance criteria:

- Shows candidate records.
- User can select link.
- Original candidates preserved.
- Final link stored.
- Audit record created.

---

## TASK-13.03 Implement assignment support

Estimate: 2–3h  
Priority: P2

Acceptance criteria:

- Assign/reassign/unassign if users in scope.
- Windows username supported for MVP.
- Audit record created.

---

# EPIC-14 — Period Lifecycle

Goal: enforce period-level control states.

## TASK-14.01 Implement lifecycle service

Estimate: 3–4h  
Priority: P1

Acceptance criteria:

- Supports Draft, Files Imported, Validated, Reconciled, Review Required, Reviewed, Approved, Closed, Reopened flag/state.
- Invalid transitions blocked.
- Transitions audited.

---

## TASK-14.02 Enforce lifecycle permissions

Estimate: 2–3h  
Priority: P1

Acceptance criteria:

- Closed period blocks changes.
- Reopen requires reason.
- Re-run after approved/closed requires reopen.
- CTA changes by lifecycle state.

---

# EPIC-15 — Security and Data Handling

Goal: implement local-only secure-by-default handling.

## TASK-15.01 Configure local app data folders

Estimate: 2h  
Priority: P1

Acceptance criteria:

- SQLite DB stored in controlled app data folder.
- Logs stored in controlled log folder.
- Temp files stored in controlled temp folder.
- Paths documented.

---

## TASK-15.02 Implement file storage mode

Estimate: 2–3h  
Priority: P1

Acceptance criteria:

- Stores controlled copies OR stores references/checksums.
- Mode documented.
- Checksums computed for imports.

---

## TASK-15.03 Implement permission service

Estimate: 2–3h  
Priority: P2

Acceptance criteria:

- Supports Operator, Reviewer, Approver, Admin-equivalent.
- Uses Windows username for MVP identity.
- Restricted actions checked.

---

# EPIC-16 — Accessibility, States and UX Polish

Goal: make MVP usable and safe for real users.

## TASK-16.01 Implement error/empty/loading state components

Estimate: 3–4h  
Priority: P1

Acceptance criteria:

- First launch empty state.
- Missing file state.
- Validation failed state.
- Reconciliation running/failed state.
- PDF export running/failed/success states.

---

## TASK-16.02 Keyboard and focus pass

Estimate: 3–4h  
Priority: P2

Acceptance criteria:

- Main flows usable by keyboard.
- Visible focus.
- Dialog focus returns to trigger.
- No drag-only file import.

---

## TASK-16.03 Table readability pass

Estimate: 2–3h  
Priority: P2

Acceptance criteria:

- Reconciliation, Review Queue, Branch Report and Audit tables readable.
- Row height/font size increased for long sessions.
- No-results state includes Clear Filters.

---

# EPIC-17 — Packaging and Release

Goal: deliver self-contained Windows build and handover package.

## TASK-17.01 Create self-contained publish profile

Estimate: 2h  
Priority: P1

Acceptance criteria:

- Builds self-contained Windows package.
- End user does not manually install .NET.
- Version/build number visible.

---

## TASK-17.02 Create installer or portable package

Estimate: 2–4h  
Priority: P2

Acceptance criteria:

- Inno Setup or MSIX decision made.
- Package installs/runs on clean test Windows PC.
- Includes templates/default settings.

---

## TASK-17.03 Create release documentation

Estimate: 2–3h  
Priority: P2

Acceptance criteria:

- User guide.
- Setup/admin notes.
- Known limitations.
- Third-party licenses.
- Acceptance test notes.

---

# EPIC-18 — Acceptance Testing

Goal: prove MVP against agreed frozen dataset.

## TASK-18.01 Freeze sample dataset register

Estimate: 1–2h  
Priority: P0

Acceptance criteria:

- Supplier PDFs listed.
- Branch workbook listed.
- Cars+ export listed.
- Manual reference result listed.
- Checksums recorded.
- Period confirmed: April 2026 or May 2026.

---

## TASK-18.02 Create expected result files

Estimate: 2–4h  
Priority: P0

Files:

```text
expected/branch_totals.csv
expected/unbilled.csv
expected/variance_review.csv
expected/pdf_checklist.md
```

Acceptance criteria:

- Expected totals approved.
- Expected exceptions approved.
- Known assumptions documented.

---

## TASK-18.03 Run end-to-end acceptance test

Estimate: 3–4h  
Priority: P0

Acceptance criteria:

- Import all three mandatory files.
- Validate.
- Run reconciliation.
- Compare totals.
- Compare unbilled/variance/review lists.
- Generate branch PDF.
- Confirm audit records created.

---

# Recommended Build Order

Use this order for development:

```text
1. EPIC-01 Project Skeleton
2. EPIC-02 Domain Model
3. EPIC-04 Normalisation
4. EPIC-03 SQLite Persistence
5. EPIC-05 Excel Import
6. EPIC-06 PDF Import
7. EPIC-07 Validation/Mapping
8. EPIC-08 Reconciliation Engine
9. EPIC-09 Audit/History
10. EPIC-10 Branch Report
11. EPIC-11 PDF Export
12. EPIC-14 Period Lifecycle
13. EPIC-13 Exception Workflow
14. EPIC-12 WinUI Shell/Screens
15. EPIC-16 UX/States/Accessibility
16. EPIC-15 Security/Data Handling
17. EPIC-17 Packaging/Release
18. EPIC-18 Acceptance Testing
```

---

# MVP Milestones

## Milestone 1 — Core skeleton

Deliver:

- solution builds
- domain models
- basic SQLite schema
- normalisation services
- unit tests

Exit criteria:

- `dotnet build` passes
- domain tests pass

---

## Milestone 2 — Parsers

Deliver:

- branch workbook parser
- Cars+ export parser
- Mobil PDF parser
- Farmlands/Caltex PDF parser
- parser golden tests

Exit criteria:

- sample files parse into deterministic CSV/JSON outputs

---

## Milestone 3 — Reconciliation core

Deliver:

- validation service
- matching engine
- status assignment
- branch totals
- golden-file reconciliation tests

Exit criteria:

- actual totals/exceptions match expected reference result or differences are documented

---

## Milestone 4 — Reporting/export

Deliver:

- branch report service
- PDF export
- export history
- audit records

Exit criteria:

- branch report and PDF use the same run data and totals

---

## Milestone 5 — Desktop MVP

Deliver:

- WinUI shell
- import/validate screen
- dashboard
- reconciliation detail
- review queue
- branch report
- PDF export modal

Exit criteria:

- user can complete full workflow without developer tools

---

## Milestone 6 — Handover

Deliver:

- self-contained build
- installer/portable package
- user guide
- setup/admin notes
- known limitations
- acceptance test package

Exit criteria:

- app runs on clean modern Windows PC
- agreed acceptance dataset passes
