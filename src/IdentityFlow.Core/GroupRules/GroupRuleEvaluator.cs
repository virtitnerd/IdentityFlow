using IdentityFlow.Core.Configuration;
using IdentityFlow.Core.Domain;
using IdentityFlow.Core.Exceptions;
using IdentityFlow.Core.Expressions;

namespace IdentityFlow.Core.GroupRules;

/// <summary>
/// Evaluates <see cref="GroupAssignmentRule.Condition"/> expressions against
/// an employee to decide static/assigned group membership. Kept separate
/// from dynamic-membership groups, which Entra ID evaluates on its own once
/// the relevant attributes (including extension attributes) are synced.
/// </summary>
public sealed class GroupRuleEvaluator
{
    public bool Evaluate(GroupAssignmentRule rule, EmployeeRecord employee)
    {
        var result = DynamicExpressionEvaluator.Evaluate(
            rule.Condition,
            $"group rule '{rule.Name}' (employee {employee.EmployeeCode})",
            typeof(bool),
            [("employee", typeof(EmployeeRecord), employee)]);

        return (bool)result!;
    }

    /// <summary>
    /// Computes the desired set of assigned-group object IDs for an
    /// employee from all enabled rules, reporting any rules that failed to
    /// evaluate rather than throwing, so one bad expression doesn't block
    /// every other rule.
    /// </summary>
    public GroupEvaluationResult EvaluateAll(EmployeeRecord employee, IEnumerable<GroupAssignmentRule> rules)
    {
        var desired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var removable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<ExpressionEvaluationException>();

        foreach (var rule in rules.Where(r => r.Enabled))
        {
            bool matches;
            try
            {
                matches = Evaluate(rule, employee);
            }
            catch (ExpressionEvaluationException ex)
            {
                errors.Add(ex);
                continue;
            }

            if (matches)
            {
                desired.Add(rule.TargetGroupObjectId);
            }
            else if (rule.RemoveWhenConditionFails)
            {
                removable.Add(rule.TargetGroupObjectId);
            }
        }

        // A group can be reached by more than one rule; never remove a
        // group that some other satisfied rule still wants the user in.
        removable.ExceptWith(desired);

        return new GroupEvaluationResult(desired, removable, errors);
    }
}

public sealed record GroupEvaluationResult(
    IReadOnlySet<string> GroupsToAdd,
    IReadOnlySet<string> GroupsToRemove,
    IReadOnlyList<ExpressionEvaluationException> Errors);
