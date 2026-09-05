namespace PaycomEntraProvisioner.Core.Exceptions;

/// <summary>
/// Thrown when a configured System.Linq.Dynamic.Core expression (a field
/// mapping transform or a group assignment rule condition) fails to parse
/// or evaluate for a given employee. Callers should catch this per-rule /
/// per-mapping so a single bad expression doesn't abort an entire sync run.
/// </summary>
public sealed class ExpressionEvaluationException : Exception
{
    public string Expression { get; }

    public ExpressionEvaluationException(string expression, string context, Exception inner)
        : base($"Failed to evaluate expression for {context}: \"{expression}\" - {inner.Message}", inner)
    {
        Expression = expression;
    }
}
