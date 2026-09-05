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
    /// Resolves an Entra user object id from the same anchor used for
    /// provisioning matching (typically userPrincipalName or the employeeId
    /// extension attribute). Returns null if no matching user exists yet
    /// (e.g. the provisioning job hasn't created them on this run).
    /// </summary>
    Task<string?> FindUserObjectIdAsync(string userPrincipalName, CancellationToken cancellationToken = default);

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
}
