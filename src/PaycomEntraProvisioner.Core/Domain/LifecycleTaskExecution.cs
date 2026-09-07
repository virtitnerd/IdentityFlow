namespace PaycomEntraProvisioner.Core.Domain;

/// <summary>
/// A durable, queryable audit record of one lifecycle task actually
/// running for one employee - the artifact an auditor would want to see
/// regardless of which compliance framework is in play. Also doubles as
/// the idempotency guard: a task is not re-executed for the same employee
/// once a successful record exists here.
/// </summary>
public sealed class LifecycleTaskExecution
{
    public int Id { get; set; }
    public int LifecycleTaskId { get; set; }
    public required string EmployeeCode { get; set; }
    public LifecycleTaskType TaskType { get; set; }
    public DateTimeOffset ExecutedAt { get; set; }
    public bool Success { get; set; }
    public string? Detail { get; set; }
}
