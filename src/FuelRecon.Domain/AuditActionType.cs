namespace FuelRecon.Domain;

/// <summary>
/// Business audit action classification (append-only audit trail).
/// </summary>
public enum AuditActionType
{
    Create = 0,
    Update = 1,
    Delete = 2,
    Import = 3,
    Validate = 4,
    Map = 5,
    Reconcile = 6,
    Resolve = 7,
    Link = 8,
    Approve = 9,
    ExportPdf = 10,
    ChangeSettings = 11,
    LifecycleTransition = 12,
}
