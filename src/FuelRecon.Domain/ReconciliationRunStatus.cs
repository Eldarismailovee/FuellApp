namespace FuelRecon.Domain;

public enum ReconciliationRunStatus
{
    Created,
    Running,
    Completed,
    Failed,
    Superseded,
}