using System.Text.Json;
using FuelRecon.Application.Persistence;
using FuelRecon.Domain;

namespace FuelRecon.Application.BranchReports;

public sealed record ApproveBranchReportVersionRequest(
    Guid BranchReportVersionId,
    string ApprovedBy,
    DateTimeOffset ApprovedAtUtc,
    string? ApprovalNote,
    BranchReportApprovalPolicy? Policy = null);

public sealed record ApproveBranchReportVersionResponse(BranchReportApprovalRecord Approval);

public interface IApproveBranchReportVersionUseCase
{
    /// <summary>
    /// Records an append-only approval with a JSON snapshot of persisted branch-report metrics at approval time.
    /// Does not mutate reconciliation items or branch report version rows.
    /// </summary>
    ApproveBranchReportVersionResponse Execute(ApproveBranchReportVersionRequest request);
}

public sealed class ApproveBranchReportVersionUseCase(
    IBranchReportRepository branchReportRepository,
    IBranchReportApprovalRepository branchReportApprovalRepository,
    IReconciliationItemRepository reconciliationItemRepository,
    IAuditRepository auditRepository) : IApproveBranchReportVersionUseCase
{
    private const string AuditOrigin = "FuelRecon.Application.BranchReports";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public ApproveBranchReportVersionResponse Execute(ApproveBranchReportVersionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ApprovedBy);

        if (request.BranchReportVersionId == Guid.Empty)
        {
            throw new ArgumentException("Branch report version id cannot be empty.", nameof(request));
        }

        var report = branchReportRepository.GetById(request.BranchReportVersionId);
        if (report is null)
        {
            throw new InvalidOperationException(
                $"Branch report version '{request.BranchReportVersionId:D}' was not found.");
        }

        var metrics = branchReportRepository.GetPersistedMetrics(report.Id);
        if (metrics is null)
        {
            throw new InvalidOperationException(
                $"Persisted metrics for branch report '{report.Id:D}' were not found.");
        }

        if (branchReportApprovalRepository.FindByBranchReport(report.Id) is not null)
        {
            throw new InvalidOperationException(
                $"Branch report version '{report.Id:D}' has already been approved.");
        }

        var policy = request.Policy ?? BranchReportApprovalPolicy.Default;
        var items = reconciliationItemRepository.ListByRun(report.RunId);
        if (policy.RequireApprovalNoteWhenUnresolvedItems
            && BranchHasUnresolvedItems(report.BranchId, items)
            && string.IsNullOrWhiteSpace(request.ApprovalNote))
        {
            throw new ArgumentException(
                $"{BranchReportAuditReasonCodes.ApprovalNoteRequiredForUnresolvedItems}: Approval note is required while unresolved reconciliation items exist for this branch.",
                nameof(request.ApprovalNote));
        }

        var snapshotJson = BuildSnapshotJson(report, metrics);

        var approval = new BranchReportApprovalRecord(
            Guid.NewGuid(),
            report.Id,
            report.RunId,
            request.ApprovedAtUtc,
            request.ApprovedBy,
            snapshotJson,
            request.ApprovalNote);

        branchReportApprovalRepository.Save(approval);

        var auditPayload = JsonSerializer.Serialize(
            new
            {
                approvalId = approval.Id.ToString("D"),
                branchReportVersionId = report.Id.ToString("D"),
                runId = report.RunId.ToString("D"),
            },
            JsonOptions);

        auditRepository.Save(
            new AuditRecord(
                Guid.NewGuid(),
                request.ApprovedAtUtc,
                request.ApprovedBy,
                AuditActionType.Approve,
                AuditEntityType.BranchReport,
                report.Id.ToString("D"),
                AuditOrigin,
                NewValuesJson: auditPayload,
                Note: approval.ApprovalNote,
                ReasonCode: BranchReportAuditReasonCodes.Approved));

        return new ApproveBranchReportVersionResponse(approval);
    }

    private static bool BranchHasUnresolvedItems(
        CanonicalBranchId branchId,
        IReadOnlyList<ReconciliationItem> items) =>
        items.Any(item =>
            item.BranchId?.Value == branchId.Value
            && item.ResolutionStatus != ResolutionStatus.Resolved);

    private static string BuildSnapshotJson(BranchReportVersion report, BranchReportPersistedMetrics metrics)
    {
        var payload = new ApprovalSnapshotPayload(
            report.Id.ToString("D"),
            report.RunId.ToString("D"),
            report.BranchId.Value,
            report.VersionNumber,
            metrics.LifecycleStatus.ToString(),
            metrics.ReviewCount,
            metrics.SupplierLitres.Value,
            metrics.BranchLitres.Value,
            metrics.BilledLitres.Value,
            metrics.UnbilledLitres.Value,
            metrics.EstimatedRecovery.Value);

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private sealed record ApprovalSnapshotPayload(
        string BranchReportId,
        string RunId,
        string BranchId,
        int VersionNumber,
        string LifecycleStatus,
        int ReviewCount,
        decimal SupplierLitres,
        decimal BranchLitres,
        decimal BilledLitres,
        decimal UnbilledLitres,
        decimal EstimatedRecovery);
}
