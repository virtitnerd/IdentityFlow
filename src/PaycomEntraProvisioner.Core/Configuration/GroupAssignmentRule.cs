namespace PaycomEntraProvisioner.Core.Configuration;

/// <summary>
/// Declarative rule that assigns (or removes) membership in an assigned
/// (non-dynamic) Entra ID security group based on the employee's attributes.
/// Dynamic-membership groups should be preferred wherever possible since
/// Entra ID evaluates those automatically once the mapped/extension
/// attributes are synced; this exists for the groups that cannot be
/// expressed as a dynamic membership rule (e.g. app role groups gated by
/// business logic spanning multiple fields, or legacy groups with manual
/// exception members that must be preserved).
/// </summary>
public sealed class GroupAssignmentRule
{
    public int Id { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// System.Linq.Dynamic.Core boolean expression evaluated against the
    /// <see cref="Domain.EmployeeRecord"/> instance (implicit variable
    /// <c>employee</c>), e.g.
    /// "employee.Department == \"Sales\" and employee.Status == \"Active\"".
    /// </summary>
    public required string Condition { get; set; }

    public required string TargetGroupObjectId { get; set; }

    public string? TargetGroupDisplayName { get; set; }

    /// <summary>
    /// When true, employees who no longer satisfy <see cref="Condition"/>
    /// are removed from the group by the reconciler. When false, membership
    /// is additive only (never removed by this rule) - useful for groups
    /// that also carry manually-added members you don't want the sync to
    /// touch.
    /// </summary>
    public bool RemoveWhenConditionFails { get; set; } = true;

    public bool Enabled { get; set; } = true;

    public string? Notes { get; set; }
}
