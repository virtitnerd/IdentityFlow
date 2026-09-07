namespace IdentityFlow.Core.Domain;

/// <summary>
/// A concrete, code-backed lifecycle action - deliberately a fixed catalog
/// rather than a free-text expression (unlike <c>FieldMapping.TransformExpression</c>
/// or <c>GroupAssignmentRule.Condition</c>), since account lifecycle actions
/// are exactly the wrong place to add another dynamic-expression surface.
/// </summary>
public enum LifecycleTaskType
{
    /// <summary>
    /// Not a one-time action - <see cref="Domain.LifecycleTrigger.Joiner"/>
    /// only. Its configured day offset feeds directly into
    /// <c>MappingEngine.BuildScimResource</c>'s <c>active</c> computation
    /// every run, rather than being executed and recorded once.
    /// </summary>
    EnableAccount,

    /// <summary>
    /// Not a one-time action - <see cref="Domain.LifecycleTrigger.Leaver"/>
    /// only. Its configured day offset feeds directly into
    /// <c>MappingEngine.BuildScimResource</c>'s <c>active</c> computation
    /// every run, rather than being executed and recorded once.
    /// </summary>
    DisableAccount,

    /// <summary>
    /// One-time action, <see cref="Domain.LifecycleTrigger.Leaver"/> only:
    /// revokes all of the user's active sign-in sessions and refresh
    /// tokens. Disabling an account alone does not do this - a session or
    /// refresh token issued before disablement otherwise remains usable
    /// until it naturally expires.
    /// </summary>
    RevokeSignInSessions,

    /// <summary>
    /// One-time action, <see cref="Domain.LifecycleTrigger.Leaver"/> only:
    /// removes the user from every assigned (non-dynamic) security group
    /// they currently belong to, regardless of what any
    /// <c>GroupAssignmentRule</c> condition would otherwise decide - a
    /// deliberate override so offboarding cleanup doesn't depend on every
    /// rule author remembering to add a status check.
    /// </summary>
    RemoveFromAllAssignedGroups,

    /// <summary>
    /// One-time action, <see cref="Domain.LifecycleTrigger.Leaver"/> only:
    /// deletes the Entra ID user object (Entra soft-deletes for 30 days,
    /// so this is recoverable in that window but not indefinitely). The
    /// only task type with no built-in default - an admin must
    /// deliberately configure this and choose a retention offset.
    /// </summary>
    DeleteAccount
}
