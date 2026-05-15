using System.Globalization;
using System.Text;
using FuelRecon.Application.Processing;
using FuelRecon.Application.Reconciliation;
using FuelRecon.Domain;

namespace FuelRecon.Application.Diagnostics;

/// <summary>
/// Writes human-readable reconciliation diagnostics under <c>artifacts/reconciliation-diagnostics/</c>
/// for offline inspection. Output is deterministic for a given <see cref="ProcessFuelReconciliationResult"/>.
/// </summary>
public static class ReconciliationDiagnosticsWriter
{
    public const int FirstItemsLimit = 100;

    public static readonly string[] ExpectedRelativeFileNames =
    [
        "run-summary.txt",
        "counts-by-status.tsv",
        "counts-by-branch.tsv",
        "branch-totals.tsv",
        "reconciliation-items-first-100.tsv",
        "reason-code-counts.tsv",
        "source-reference-summaries.tsv",
    ];

    /// <summary>
    /// Writes diagnostics into <c>{repositoryRoot}/artifacts/reconciliation-diagnostics/{period}_{supplier-stem}/</c>.
    /// </summary>
    /// <returns>The absolute output directory path.</returns>
    /// <exception cref="ArgumentException">When reconciliation did not produce engine output.</exception>
    public static string Write(
        ProcessFuelReconciliationResult result,
        string repositoryRoot,
        string supplierPdfPath,
        FuelPeriod period)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(supplierPdfPath);

        if (result.ReconciliationEngineResult is null)
        {
            throw new ArgumentException(
                "Reconciliation diagnostics require ReconciliationEngineResult; import may have failed.",
                nameof(result));
        }

        var supplierStem = Path.GetFileNameWithoutExtension(supplierPdfPath);
        var folderSegment = $"{period.ToSortableString()}_{ToSafeFolderSegment(supplierStem)}";
        var outputDirectory = Path.Combine(repositoryRoot, "artifacts", "reconciliation-diagnostics", folderSegment);
        Directory.CreateDirectory(outputDirectory);

        var engineResult = result.ReconciliationEngineResult;
        var items = SortItemsDeterministically(engineResult.Items);

        File.WriteAllText(
            Path.Combine(outputDirectory, "run-summary.txt"),
            BuildRunSummaryText(result, supplierPdfPath, period),
            Utf8NoBom);

        WriteCountsByStatus(Path.Combine(outputDirectory, "counts-by-status.tsv"), items);
        WriteCountsByBranch(Path.Combine(outputDirectory, "counts-by-branch.tsv"), items);
        WriteBranchTotals(Path.Combine(outputDirectory, "branch-totals.tsv"), engineResult.BranchSummaries);
        WriteItemsSample(Path.Combine(outputDirectory, "reconciliation-items-first-100.tsv"), items, FirstItemsLimit);
        WriteReasonCodeCounts(Path.Combine(outputDirectory, "reason-code-counts.tsv"), items);
        WriteSourceReferenceSummaries(Path.Combine(outputDirectory, "source-reference-summaries.tsv"), items);

        return outputDirectory;
    }

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static List<ReconciliationItem> SortItemsDeterministically(IReadOnlyList<ReconciliationItem> items) =>
        items
            .OrderBy(item => item.BranchId?.Value ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(item => item.SystemStatus.ToString(), StringComparer.Ordinal)
            .ThenBy(
                item => string.Join(";", item.ReasonCodes.Order(StringComparer.Ordinal)),
                StringComparer.Ordinal)
            .ThenBy(item => item.Id.ToString("D"), StringComparer.Ordinal)
            .ToList();

    private static string BuildRunSummaryText(
        ProcessFuelReconciliationResult result,
        string supplierPdfPath,
        FuelPeriod period)
    {
        var import = result.ImportValidationResult;
        var engine = result.ReconciliationEngineResult!;
        var builder = new StringBuilder();

        builder.AppendLine($"Period: {period.ToSortableString()} ({period})");
        builder.AppendLine($"PipelineSuccess: {result.Success}");
        builder.AppendLine($"SupplierPdfPath: {supplierPdfPath}");
        builder.AppendLine($"ImportBatchId: {result.ImportPersistenceResponse.ImportBatchId}");

        if (result.ReconciliationPersistenceResponse is not null)
        {
            builder.AppendLine($"ReconciliationRunId: {result.ReconciliationPersistenceResponse.RunId}");
            builder.AppendLine($"PersistedItemCount: {result.ReconciliationPersistenceResponse.SavedItemCount}");
        }

        builder.AppendLine($"ReconciliationRunEntityId: {engine.Run.Id}");
        builder.AppendLine($"ReconciliationRunStatus: {engine.Run.Status}");
        builder.AppendLine($"ReconciliationItemCount: {engine.Items.Count}");
        builder.AppendLine($"BranchSummaryCount: {engine.BranchSummaries.Count}");
        builder.AppendLine($"SupplierTransactionCount: {import.SupplierTransactions.Count}");
        builder.AppendLine($"BranchLitresEntryCount: {import.BranchLitresEntries.Count}");
        builder.AppendLine($"CarsBillingEntryCount: {import.CarsBillingEntries.Count}");
        builder.AppendLine();

        builder.AppendLine("Import files:");
        foreach (var file in import.Files.OrderBy(f => f.InputSlot.ToString(), StringComparer.Ordinal))
        {
            builder.AppendLine(
                $"  {file.InputSlot}: path={file.FilePath ?? "(null)"}, status={file.Status}, rowCount={file.RowCount}, valid={file.ValidRowCount}, skipped={file.SkippedRowCount}");
            var errors = file.Issues.Count(i => i.Severity == ValidationSeverity.Error);
            var warnings = file.Issues.Count(i => i.Severity == ValidationSeverity.Warning);
            builder.AppendLine($"    issues: errors={errors}, warnings={warnings}");
        }

        return builder.ToString();
    }

    private static void WriteCountsByStatus(string path, IReadOnlyList<ReconciliationItem> sortedItems)
    {
        var grouped = sortedItems
            .GroupBy(item => item.SystemStatus.ToString())
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (Status: g.Key, Count: g.Count()));

        var builder = new StringBuilder();
        builder.AppendLine("Status\tCount");
        foreach (var row in grouped)
        {
            builder.AppendLine($"{EscapeTsv(row.Status)}\t{row.Count}");
        }

        File.WriteAllText(path, builder.ToString(), Utf8NoBom);
    }

    private static void WriteCountsByBranch(string path, IReadOnlyList<ReconciliationItem> sortedItems)
    {
        var grouped = sortedItems
            .GroupBy(item => item.BranchId?.Value ?? string.Empty)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (BranchId: string.IsNullOrEmpty(g.Key) ? "(none)" : g.Key, Count: g.Count()));

        var builder = new StringBuilder();
        builder.AppendLine("BranchId\tCount");
        foreach (var row in grouped)
        {
            builder.AppendLine($"{EscapeTsv(row.BranchId)}\t{row.Count}");
        }

        File.WriteAllText(path, builder.ToString(), Utf8NoBom);
    }

    private static void WriteBranchTotals(string path, IReadOnlyList<BranchSummary> summaries)
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            "BranchId\tSupplierLitres\tBranchLitres\tBilledLitres\tUnbilledLitres\tEstimatedRecovery\tReviewCount\tStatus");

        foreach (var summary in summaries.OrderBy(s => s.BranchId.Value, StringComparer.Ordinal))
        {
            builder.AppendLine(string.Join('\t', new[]
            {
                EscapeTsv(summary.BranchId.Value),
                EscapeTsv(summary.SupplierLitres.ToString()),
                EscapeTsv(summary.BranchLitres.ToString()),
                EscapeTsv(summary.BilledLitres.ToString()),
                EscapeTsv(summary.UnbilledLitres.ToString()),
                EscapeTsv(summary.EstimatedRecovery.ToString()),
                summary.ReviewCount.ToString(CultureInfo.InvariantCulture),
                EscapeTsv(summary.Status.ToString()),
            }));
        }

        File.WriteAllText(path, builder.ToString(), Utf8NoBom);
    }

    private static void WriteItemsSample(string path, IReadOnlyList<ReconciliationItem> sortedItems, int limit)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join('\t', new[]
        {
            "ItemId",
            "BranchId",
            "SystemStatus",
            "ResolutionStatus",
            "ConfidenceBucket",
            "ReasonCodes",
            "HumanReadableReason",
            "SupplierTransactionId",
            "BranchLitresEntryId",
            "CarsBillingEntryId",
            "MatchCandidateCount",
            "SupplierSourceRef",
            "BranchSourceRef",
            "CarsSourceRef",
            "LitresVariance",
            "AmountVariance",
        }));

        foreach (var item in sortedItems.Take(limit))
        {
            var reasonCodes = string.Join(";", item.ReasonCodes.Order(StringComparer.Ordinal));
            builder.AppendLine(string.Join('\t', new[]
            {
                EscapeTsv(item.Id.ToString("D")),
                EscapeTsv(item.BranchId?.Value ?? string.Empty),
                EscapeTsv(item.SystemStatus.ToString()),
                EscapeTsv(item.ResolutionStatus.ToString()),
                EscapeTsv(item.ConfidenceBucket.ToString()),
                EscapeTsv(reasonCodes),
                EscapeTsv(item.HumanReadableReason ?? string.Empty),
                EscapeTsv(item.SupplierTransactionId?.ToString("D") ?? string.Empty),
                EscapeTsv(item.BranchLitresEntryId?.ToString("D") ?? string.Empty),
                EscapeTsv(item.CarsBillingEntryId?.ToString("D") ?? string.Empty),
                item.MatchCandidates.Count.ToString(CultureInfo.InvariantCulture),
                EscapeTsv(FormatSourceReferenceSummary(item.SupplierSourceReference)),
                EscapeTsv(FormatSourceReferenceSummary(item.BranchSourceReference)),
                EscapeTsv(FormatSourceReferenceSummary(item.CarsSourceReference)),
                EscapeTsv(item.LitresVariance?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty),
                EscapeTsv(item.AmountVariance?.ToString() ?? string.Empty),
            }));
        }

        File.WriteAllText(path, builder.ToString(), Utf8NoBom);
    }

    private static void WriteReasonCodeCounts(string path, IReadOnlyList<ReconciliationItem> sortedItems)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in sortedItems)
        {
            foreach (var code in item.ReasonCodes)
            {
                counts.TryGetValue(code, out var n);
                counts[code] = n + 1;
            }
        }

        var builder = new StringBuilder();
        builder.AppendLine("ReasonCode\tItemOccurrences");
        foreach (var pair in counts.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            builder.AppendLine($"{EscapeTsv(pair.Key)}\t{pair.Value}");
        }

        File.WriteAllText(path, builder.ToString(), Utf8NoBom);
    }

    private static void WriteSourceReferenceSummaries(string path, IReadOnlyList<ReconciliationItem> sortedItems)
    {
        var counts = new Dictionary<(string Layer, string Key), int>();

        void Bump(string layer, SourceReference? reference)
        {
            var key = FormatSourceReferenceSummary(reference);
            var tuple = (layer, key);
            counts.TryGetValue(tuple, out var n);
            counts[tuple] = n + 1;
        }

        foreach (var item in sortedItems)
        {
            Bump("Supplier", item.SupplierSourceReference);
            Bump("BranchLitres", item.BranchSourceReference);
            Bump("CarsBilling", item.CarsSourceReference);
        }

        var builder = new StringBuilder();
        builder.AppendLine("Layer\tSourcePattern\tItemOccurrences");
        foreach (var row in counts.OrderBy(r => r.Key.Layer, StringComparer.Ordinal).ThenBy(r => r.Key.Key, StringComparer.Ordinal))
        {
            builder.AppendLine($"{EscapeTsv(row.Key.Layer)}\t{EscapeTsv(row.Key.Key)}\t{row.Value}");
        }

        File.WriteAllText(path, builder.ToString(), Utf8NoBom);
    }

    private static string FormatSourceReferenceSummary(SourceReference? reference)
    {
        if (reference is null)
        {
            return "(missing)";
        }

        var fileName = Path.GetFileName(reference.SourceFile);
        if (reference.PageNumber is { } page)
        {
            return $"{fileName}|page={page.ToString(CultureInfo.InvariantCulture)}";
        }

        if (reference.SheetName is not null && reference.RowNumber is { } row)
        {
            return $"{fileName}|sheet={reference.SheetName}|row={row.ToString(CultureInfo.InvariantCulture)}";
        }

        if (reference.SheetName is not null)
        {
            return $"{fileName}|sheet={reference.SheetName}";
        }

        if (reference.RowNumber is { } rowOnly)
        {
            return $"{fileName}|row={rowOnly.ToString(CultureInfo.InvariantCulture)}";
        }

        return fileName;
    }

    private static string EscapeTsv(string? value)
    {
        var text = value ?? string.Empty;
        if (text.IndexOfAny(['\t', '\r', '\n', '"']) < 0)
        {
            return text;
        }

        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string ToSafeFolderSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        }

        return builder.Length > 0 ? builder.ToString() : "supplier";
    }
}
