using IdentityFlow.Core.Configuration;
using IdentityFlow.Core.Domain;
using IdentityFlow.Core.Exceptions;
using IdentityFlow.Core.GroupRules;
using Xunit;

namespace IdentityFlow.Core.Tests;

public class GroupRuleEvaluatorTests
{
    private static EmployeeRecord Employee(string department, EmploymentStatus status = EmploymentStatus.Active) => new()
    {
        EmployeeCode = "E1",
        WorkEmail = "user@contoso.com",
        Department = department,
        Status = status
    };

    [Fact]
    public void Evaluate_ReturnsTrueWhenConditionMatches()
    {
        var evaluator = new GroupRuleEvaluator();
        var rule = new GroupAssignmentRule
        {
            Name = "Sales",
            Condition = "employee.Department == \"Sales\"",
            TargetGroupObjectId = "grp-1"
        };

        Assert.True(evaluator.Evaluate(rule, Employee("Sales")));
        Assert.False(evaluator.Evaluate(rule, Employee("Engineering")));
    }

    [Fact]
    public void Evaluate_ThrowsExpressionEvaluationExceptionOnBadExpression()
    {
        var evaluator = new GroupRuleEvaluator();
        var rule = new GroupAssignmentRule
        {
            Name = "Broken",
            Condition = "employee.NotAField ===",
            TargetGroupObjectId = "grp-1"
        };

        Assert.Throws<ExpressionEvaluationException>(() => evaluator.Evaluate(rule, Employee("Sales")));
    }

    [Fact]
    public void EvaluateAll_AddsToDesiredAndRemovesFromNonMatchingManagedGroup()
    {
        var evaluator = new GroupRuleEvaluator();
        var rules = new List<GroupAssignmentRule>
        {
            new() { Name = "Sales", Condition = "employee.Department == \"Sales\"", TargetGroupObjectId = "grp-sales", RemoveWhenConditionFails = true },
            new() { Name = "Eng", Condition = "employee.Department == \"Engineering\"", TargetGroupObjectId = "grp-eng", RemoveWhenConditionFails = true }
        };

        var result = evaluator.EvaluateAll(Employee("Sales"), rules);

        Assert.Contains("grp-sales", result.GroupsToAdd);
        Assert.Contains("grp-eng", result.GroupsToRemove);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void EvaluateAll_DoesNotRemoveFromAdditiveOnlyGroup()
    {
        var evaluator = new GroupRuleEvaluator();
        var rules = new List<GroupAssignmentRule>
        {
            new() { Name = "Legacy", Condition = "employee.Department == \"Engineering\"", TargetGroupObjectId = "grp-legacy", RemoveWhenConditionFails = false }
        };

        var result = evaluator.EvaluateAll(Employee("Sales"), rules);

        Assert.Empty(result.GroupsToAdd);
        Assert.Empty(result.GroupsToRemove);
    }

    [Fact]
    public void EvaluateAll_CollectsErrorsWithoutThrowing()
    {
        var evaluator = new GroupRuleEvaluator();
        var rules = new List<GroupAssignmentRule>
        {
            new() { Name = "Broken", Condition = "employee.NotAField ===", TargetGroupObjectId = "grp-1" },
            new() { Name = "Sales", Condition = "employee.Department == \"Sales\"", TargetGroupObjectId = "grp-2" }
        };

        var result = evaluator.EvaluateAll(Employee("Sales"), rules);

        Assert.Single(result.Errors);
        Assert.Contains("grp-2", result.GroupsToAdd);
    }
}
