namespace PaycomEntraProvisioner.Core.Domain;

/// <summary>The HR event a <see cref="LifecycleTaskType"/> is timed relative to.</summary>
public enum LifecycleTrigger
{
    /// <summary>Relative to <see cref="EmployeeRecord.HireDate"/>.</summary>
    Joiner,

    /// <summary>
    /// Relative to <see cref="EmployeeRecord.TerminationDate"/> - or, for a
    /// worker who simply vanished from the Paycom feed with no explicit
    /// termination date, the date they were last seen.
    /// </summary>
    Leaver
}
