namespace PaycomEntraProvisioner.Core.Abstractions;

public sealed record GroupReconciliationResult(
    string GroupObjectId,
    IReadOnlyList<string> MembersAdded,
    IReadOnlyList<string> MembersRemoved,
    IReadOnlyList<string> Errors);

/// <summary>
/// Direct Microsoft Graph operations that sit outside the provisioning
/// job's own attribute sync: resolving a user's Entra object id and
/// reconciling membership in assigned (non-dynamic) security groups.
/// Dynamic-membership groups need none of this - Entra evaluates those on
/// its own from synced attributes.
/// </summary>
public interface IEntraDirectoryClient
{
    /// <summary>
    /// Resolves an Entra user object id, trying <paramref name="userPrincipalName"/>
    /// first and falling back to a lookup by <paramref name="employeeId"/>
    /// (Entra's immutable <c>employeeId</c> directory attribute) when that
    /// lookup finds no user and an employee id was supplied. The fallback
    /// exists because this app's own group-reconciliation and leaver-task
    /// correlation would otherwise lose track of an employee whose UPN
    /// changed (a name change, a typo fix) in the window before Entra's
    /// provisioning job has re-synced the new value - employeeId shouldn't
    /// change, so it survives that window. Returns null if no user matches
    /// by either anchor (e.g. the provisioning job hasn't created them yet).
    /// </summary>
    Task<string?> FindUserObjectIdAsync(string userPrincipalName, string? employeeId = null, CancellationToken cancellationToken = default);

    Task<GroupReconciliationResult> ReconcileGroupMembersAsync(
        string groupObjectId,
        IReadOnlySet<string> desiredMemberObjectIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the custom directory extension attributes registered on the
    /// sync service's own app registration (<c>GET
    /// /applications/{id}/extensionProperties</c>), returned as their full
    /// flat name (e.g. "extension_3f1a2b...9c_CostCenter") ready to use as
    /// a <see cref="Configuration.FieldMapping.TargetAttribute"/>. Used
    /// only to populate suggestions in the admin UI - returns an empty
    /// list (never throws) if Graph is unreachable or none are registered.
    /// </summary>
    Task<IReadOnlyList<string>> GetCustomExtensionAttributeNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes every active sign-in session and refresh token for the
    /// user. Disabling an account alone does not do this - a session or
    /// refresh token issued beforehand otherwise remains usable until it
    /// naturally expires.
    /// </summary>
    Task RevokeSignInSessionsAsync(string userObjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the user from every assigned (non-dynamic) security group
    /// they currently belong to. Dynamic-membership groups are skipped
    /// (Graph rejects a direct member removal from one; membership there
    /// is Entra's own attribute-driven evaluation to unwind, not this
    /// call's) - failures on individual groups are collected, not thrown,
    /// so one ungovernable group doesn't block cleanup of the rest.
    /// </summary>
    Task<LeaverGroupCleanupResult> RemoveUserFromAllGroupsAsync(string userObjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the Entra ID user object. Entra soft-deletes for 30 days
    /// (recoverable in that window via the admin center or Graph), but
    /// this is otherwise the one genuinely hard-to-reverse action this
    /// solution can take - callers should only invoke it from a
    /// deliberately admin-configured <see cref="Domain.LifecycleTaskType.DeleteAccount"/> task.
    /// </summary>
    Task DeleteUserAsync(string userObjectId, CancellationToken cancellationToken = default);
}

public sealed record LeaverGroupCleanupResult(IReadOnlyList<string> GroupsRemovedFrom, IReadOnlyList<string> Errors);
