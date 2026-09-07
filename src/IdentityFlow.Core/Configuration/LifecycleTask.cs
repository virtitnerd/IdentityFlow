using IdentityFlow.Core.Domain;

namespace IdentityFlow.Core.Configuration;

/// <summary>
/// An admin-configured joiner/leaver action: what should happen, timed
/// relative to the employee's hire or termination date. Deliberately a
/// fixed <see cref="LifecycleTaskType"/> catalog rather than a free-text
/// expression - see the type's own doc comments for why.
/// </summary>
public sealed class LifecycleTask
{
    public int Id { get; set; }

    public LifecycleTrigger Trigger { get; set; }

    public LifecycleTaskType TaskType { get; set; }

    /// <summary>
    /// Days relative to the trigger date (<see cref="EmployeeRecord.HireDate"/>
    /// for a Joiner task, <see cref="EmployeeRecord.TerminationDate"/> - or
    /// the last-seen date for a worker who vanished from the feed - for a
    /// Leaver task). Negative runs before the date, 0 on the date itself,
    /// positive after it (e.g. +30 for a 30-day retention window before
    /// <see cref="LifecycleTaskType.DeleteAccount"/>).
    /// </summary>
    public int DayOffset { get; set; }

    public bool Enabled { get; set; } = true;

    public string? Notes { get; set; }
}
