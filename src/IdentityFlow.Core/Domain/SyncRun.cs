namespace IdentityFlow.Core.Domain;

public enum SyncTrigger
{
    Timer,
    Manual,
    Api
}

public enum SyncRunStatus
{
    Running,
    Succeeded,
    CompletedWithErrors,
    Failed
}

public sealed class SyncRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public SyncTrigger Trigger { get; set; }
    public string? TriggeredByUser { get; set; }
    public SyncRunStatus Status { get; set; } = SyncRunStatus.Running;

    /// <summary>When true, no writes were made to Entra ID - mapping and
    /// group rules were evaluated and recorded for review only.</summary>
    public bool DryRun { get; set; }

    public int EmployeesEvaluated { get; set; }
    public int RecordsSubmitted { get; set; }
    public int RecordsSkipped { get; set; }
    public int RecordsFailed { get; set; }
    public int GroupMembershipsAdded { get; set; }
    public int GroupMembershipsRemoved { get; set; }

    public string? ErrorSummary { get; set; }

    public List<SyncRunEmployeeResult> EmployeeResults { get; set; } = [];
}

public sealed class SyncRunEmployeeResult
{
    public int Id { get; set; }
    public Guid SyncRunId { get; set; }
    public required string EmployeeCode { get; set; }
    public string? DisplayName { get; set; }
    public required string Outcome { get; set; }
    public bool Success { get; set; }
    public string? Detail { get; set; }
}

/// <summary>
/// Last-known state for an employee, used to detect changes cheaply between
/// runs and as a safety net for terminations when Paycom's feed omits a
/// worker entirely rather than flagging them Terminated.
/// </summary>
public sealed class EmployeeSnapshot
{
    public required string EmployeeCode { get; set; }
    public string? WorkEmail { get; set; }
    public EmploymentStatus Status { get; set; }
    public required string ContentHash { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public string? EntraObjectId { get; set; }
}
