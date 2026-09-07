using System.Collections.Concurrent;
using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using PaycomEntraProvisioner.Core.Exceptions;

namespace PaycomEntraProvisioner.Core.Expressions;

/// <summary>
/// Parses and compiles a System.Linq.Dynamic.Core expression once per
/// distinct expression text + parameter shape, then reuses the compiled
/// delegate on every later call. Both <see cref="Mapping.MappingEngine"/>
/// and <see cref="GroupRules.GroupRuleEvaluator"/> evaluate admin-authored
/// expressions once per employee per run - without caching, the same
/// expression text was being re-parsed and re-compiled (the expensive
/// part, comparable to a small JIT step) on every single call instead of
/// once per distinct expression.
/// </summary>
internal static class DynamicExpressionEvaluator
{
    private static readonly ConcurrentDictionary<string, Delegate> CompiledCache = new();

    /// <summary>
    /// Evaluates <paramref name="expression"/> against the given parameters,
    /// wrapping any parse, compile, or invocation failure in a single
    /// <see cref="ExpressionEvaluationException"/> tagged with
    /// <paramref name="context"/>.
    /// </summary>
    public static object? Evaluate(
        string expression,
        string context,
        Type? resultType,
        (string Name, Type Type, object? Value)[] parameters)
    {
        var cacheKey = BuildCacheKey(expression, resultType, parameters);

        Delegate compiled;
        try
        {
            compiled = CompiledCache.GetOrAdd(cacheKey, _ =>
            {
                var parameterExpressions = parameters
                    .Select(p => Expression.Parameter(p.Type, p.Name))
                    .ToArray();

                return DynamicExpressionParser.ParseLambda(parameterExpressions, resultType, expression).Compile();
            });
        }
        catch (Exception ex)
        {
            throw new ExpressionEvaluationException(expression, context, ex);
        }

        try
        {
            return compiled.DynamicInvoke(parameters.Select(p => p.Value).ToArray());
        }
        catch (Exception ex)
        {
            throw new ExpressionEvaluationException(expression, context, ex.InnerException ?? ex);
        }
    }

    private static string BuildCacheKey(string expression, Type? resultType, (string Name, Type Type, object? Value)[] parameters) =>
        $"{resultType?.FullName}|{string.Join(',', parameters.Select(p => p.Type.FullName))}|{expression}";
}
