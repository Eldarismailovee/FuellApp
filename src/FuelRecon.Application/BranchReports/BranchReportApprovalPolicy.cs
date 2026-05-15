namespace FuelRecon.Application.BranchReports;

/// <summary>
/// Controls whether an approval must carry an explicit note when reconciliation items remain unresolved for the branch.
/// </summary>
public sealed record BranchReportApprovalPolicy(bool RequireApprovalNoteWhenUnresolvedItems = true)
{
    public static BranchReportApprovalPolicy Default { get; } = new();

    public static BranchReportApprovalPolicy Lenient { get; } = new(RequireApprovalNoteWhenUnresolvedItems: false);
}
