# Fuel Reconciliation Manager — Cursor/Codex Coding Rules

These rules are mandatory for AI-assisted implementation of the Fuel Reconciliation Manager MVP.

Project stack:
- Language: C#
- Runtime: .NET 10 LTS
- UI: WinUI 3
- Windows SDK: Windows App SDK 1.8.x stable
- Architecture: Clean Architecture + MVVM
- Database: SQLite via Microsoft.Data.Sqlite
- Excel: ClosedXML first, ExcelDataReader fallback only when in scope
- PDF parsing: PdfPig
- PDF generation: MigraDoc/PDFsharp
- Logging: Serilog
- Validation: FluentValidation
- Tests: xUnit + golden-file tests

---

## 1. Scope control

Do not implement features outside MVP v1 unless a task explicitly asks for them.

Out of scope unless explicitly assigned:
- arbitrary supplier PDF layouts;
- OCR/scanned PDF parsing;
- cloud sync/storage;
- server/multi-user deployment;
- accounting/Cars+ write-back;
- full identity management/SSO;
- advanced admin platform;
- backup/archive UI;
- custom report builder;
- automatic updates;
- notifications/escalations;
- mobile/web client.

When uncertain, implement the smallest MVP-safe version.

---

## 2. Architecture boundaries

Use Clean Architecture.

Expected project dependency direction:

```text
FuelRecon.Domain
  ↑
FuelRecon.Application
  ↑
FuelRecon.Infrastructure
  ↑
FuelRecon.Desktop
```

Rules:
- `FuelRecon.Domain` must not reference `Infrastructure`, `Desktop`, WinUI, SQLite, ClosedXML, PdfPig, PDFsharp, Serilog, or any UI/storage package.
- `FuelRecon.Application` may reference `Domain` and define interfaces/use cases.
- `FuelRecon.Infrastructure` implements parsing, SQLite, PDF export, file system and logging adapters.
- `FuelRecon.Desktop` contains WinUI views, view models, navigation and UI composition only.
- Business rules must not live in WinUI code-behind.
- Reconciliation logic must be testable without starting the desktop app.

---

## 3. Domain and data rules

Use explicit domain models for:
- FuelPeriod
- ImportBatch
- ImportedFile
- SupplierTransaction
- BranchLitresEntry
- CarsBillingEntry
- ReconciliationRun
- ReconciliationItem
- BranchReport
- PdfExportRecord
- AuditRecord
- SettingsSnapshot

Always preserve:
- raw source values;
- normalised values;
- source file references;
- row/page references where available;
- reason codes;
- run/report/export snapshot IDs where applicable.

Never silently discard transaction-like data.

---

## 4. Determinism

Reconciliation must be deterministic.

Given the same:
- input files;
- parser version;
- mapping templates;
- aliases;
- normalisation rules;
- matching tolerances;
- settings snapshot;

the system must produce the same:
- parsed rows;
- validation results;
- reconciliation items;
- branch totals;
- PDF report content, except runtime metadata such as export timestamp/export ID.

Do not use non-deterministic ordering in matching. If multiple candidates tie, create a review item instead of silently choosing one.

---

## 5. Historical immutability

Never overwrite historical reconciliation evidence.

Rules:
- A new import creates a new import batch.
- A new reconciliation creates a new reconciliation run.
- A re-run does not modify old runs.
- A new PDF export creates a new export record.
- A settings/rules change creates or references a new snapshot.
- Manual resolution must not overwrite system status.
- Manual linking must preserve original system candidates.
- Audit records must be append-only.

---

## 6. SQLite persistence

Use SQLite transactions for critical writes.

Transactional operations include:
- import batch creation;
- saving parsed rows;
- validation result save;
- reconciliation run save;
- reconciliation item save;
- manual resolution/linking;
- report approval;
- PDF export record save;
- audit record write.

If an operation fails mid-way:
- do not leave false success records;
- mark operation failed/incomplete where applicable;
- preserve committed historical data.

---

## 7. Validation and reason codes

Use machine-readable reason codes plus human-readable messages.

Examples:
- UnsupportedPdfLayout
- MissingRequiredColumns
- EmptyWorkbook
- CorruptedFile
- WrongPeriod
- PasswordProtectedFile
- ParserFailed
- BranchAliasNotFound
- SupplierBranchUnresolved
- InvalidRA
- InvalidRego
- InvalidDateFormat
- NormalizationFailed_Litres
- NormalizationFailed_Amount
- AmbiguousDateFormat
- AmbiguousNumericFormat
- MissingCharge
- RANotFoundInCars
- LitresVariance
- AmountVariance
- DuplicatePossible
- MultipleSupplierCandidates
- MultipleBranchCandidates
- MultipleCarsCandidates
- MissingRA
- RegoMismatch
- SupplierOnly
- CarsOnly
- LowConfidenceMatch

Do not use vague reasons such as `Unknown` unless there is also a correlation ID and technical log entry.

---

## 8. Import and parser rules

Treat Excel and PDF files as untrusted input.

Rules:
- Do not execute macros, scripts, external links or embedded code.
- Parse only agreed supplier PDF layouts for MVP.
- Unsupported PDFs must return `UnsupportedPdfLayout`.
- `.xlsx` is mandatory MVP Excel format.
- `.xls`, `.xlsb`, `.csv` are allowed only if explicitly in task/scope.
- Store source file checksum.
- Store parser warnings/errors.
- Store source file, sheet, row, page or reference where available.

Current agreed supplier parser targets:
- Mobil digital PDF statements.
- Farmlands/Caltex digital PDF statements.

---

## 9. Normalisation rules

Normalisation must be deterministic and idempotent.

Required normalisation:
- branch names to canonical branch IDs;
- RA values according to configured RA rule;
- rego uppercase with spaces/hyphens removed;
- dates to DateOnly;
- litres to decimal with configured precision;
- amounts to decimal with configured precision.

Always keep both:
- raw source value;
- normalised value.

Do not assume RA is numeric-only unless the task/sample confirms it.

---

## 10. Reconciliation rules

Status belongs to `ReconciliationItem`, not raw rows.

Supported item statuses:
- Matched
- Unbilled
- Variance
- DuplicatePossible
- MissingRA
- RegoMismatch
- SupplierOnly
- CarsOnly
- ReviewRequired

Matching priority:
1. Supplier to branch: canonical branch + date + litres.
2. Branch to Cars+: normalised RA.
3. Fallback: rego + date tolerance + litres tolerance.

Prefer one-to-one matches.

One-to-many or many-to-one candidates must become review candidates unless a task explicitly defines a deterministic resolution rule.

---

## 11. Manual resolution and linking

Manual resolution:
- requires a note;
- creates a final/override status;
- does not overwrite original system status;
- does not delete source references;
- creates audit record.

Manual linking:
- stores selected linked record IDs;
- stores original candidate list;
- requires user confirmation;
- creates audit record;
- applies to current reconciliation run only unless task explicitly says otherwise.

---

## 12. Audit and logging

Separate business audit from technical logs.

Business audit:
- stored in SQLite;
- visible in History/Audit UI;
- append-only;
- contains actor, timestamp, action type, entity type, entity ID, old/new values where applicable, note/reason and correlation ID where applicable.

Technical logs:
- written with Serilog;
- local only by default;
- include timestamp, severity, operation, error category and correlation ID;
- must not contain unnecessary personal/financial data.

User-visible errors should include:
- friendly message;
- error category;
- recommended next action;
- correlation ID where applicable.

---

## 13. PDF/export rules

PDF export must use a specific branch report context:
- branch;
- period;
- reconciliation run ID;
- report version;
- template version;
- export settings.

Rules:
- PDF totals must match Branch Report totals for the same context.
- Export records are immutable.
- Re-export creates a new export history record.
- PDF failures are logged and audited.
- Do not overwrite existing PDF silently.

---

## 14. UI rules

WinUI should follow MVVM.

Rules:
- Use CommunityToolkit.Mvvm for view models and commands.
- No business logic in code-behind.
- UI must be lifecycle-aware and permission-aware.
- Primary CTA must match lifecycle state:
  - Draft → Import Files
  - Files Imported → Validate & Map
  - Validated → Run Reconciliation
  - Review Required → Continue Review
  - Reviewed → Approve
  - Approved/Closed → View Report
- If status is Review Required, do not show Run Reconciliation as the primary CTA.

Screens:
- Dashboard
- Import Files
- Reconciliation
- Branch Reports
- Review Queue
- History/Audit
- Settings
- Help/User Guide

Do not add SaaS/cloud/admin screens unless explicitly tasked.

---

## 15. Accessibility and UX

Required:
- keyboard-accessible primary flows;
- visible focus;
- no drag-and-drop-only actions;
- status not communicated by colour alone;
- readable tables;
- clear empty/error/loading states;
- disabled actions must explain why;
- high contrast support for main screens;
- text scaling support at 125% and 150% where feasible.

Tables should prioritise readability over dense layouts.

---

## 16. Security and data handling

Local-only by default.

Rules:
- Do not send data to cloud.
- Do not add telemetry.
- Do not add external update checks.
- Store DB/logs/source copies in controlled local folders.
- Do not expose stack traces, raw SQL or internal absolute paths in user-facing errors.
- Use Windows username as MVP audit actor unless a task explicitly adds user accounts.
- Apply least privilege.
- Treat source files as sensitive customer/business data.

---

## 17. Testing rules

Tests are required for:
- normalisation;
- validation;
- parser outputs;
- reconciliation status assignment;
- branch totals;
- golden-file acceptance dataset;
- SQLite repository basics;
- PDF export smoke tests where practical.

Golden-file tests should compare:
- parsed supplier rows;
- parsed branch litres rows;
- parsed Cars+ rows;
- branch totals;
- unbilled list;
- variance/review list.

When actual output differs from expected output, produce a useful diff.

---

## 18. Cursor/Codex behaviour

When asked to implement a task:

- Implement only the requested task.
- Do not redesign architecture unless asked.
- Do not introduce new libraries unless needed and approved by task.
- Do not add out-of-scope features.
- Do not remove audit/history/snapshot behavior for simplicity.
- Do not replace deterministic review candidates with heuristic silent matches.
- Add or update tests when logic changes.
- Keep changes small and reviewable.
- Prefer clear code over clever code.
- If sample data is missing, create interfaces/stubs and mark TODO clearly instead of guessing business rules.

---

## 19. Definition of done for code tasks

A task is complete only when:

- project builds;
- relevant tests pass;
- public methods/classes have clear purpose;
- errors use reason codes/correlation IDs where applicable;
- no historical overwrite behavior is introduced;
- no out-of-scope feature is added;
- code follows the architecture boundaries;
- acceptance criteria in the task are satisfied.
