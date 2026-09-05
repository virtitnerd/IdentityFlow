using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using PaycomEntraProvisioner.Core.Abstractions;

namespace PaycomEntraProvisioner.Graph;

/// <summary>
/// Direct Microsoft Graph operations for assigned (non-dynamic) security
/// group reconciliation, using the Graph SDK's stable v1.0 users/groups
/// surface (as opposed to <see cref="EntraProvisioningClient"/>, which
/// talks to the newer, SCIM-shaped bulkUpload endpoint by hand).
/// Requires the app registration to hold User.Read.All and
/// Group.ReadWrite.All application permissions.
/// </summary>
public sealed class EntraDirectoryClient(
    GraphServiceClient graphServiceClient,
    IOptionsMonitor<EntraOptions> optionsMonitor,
    ILogger<EntraDirectoryClient> logger) : IEntraDirectoryClient
{
    public async Task<string?> FindUserObjectIdAsync(string userPrincipalName, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await graphServiceClient.Users[userPrincipalName]
                .GetAsync(rc => rc.QueryParameters.Select = ["id"], cancellationToken);
            return user?.Id;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 404)
        {
            return null;
        }
    }

    public async Task<GroupReconciliationResult> ReconcileGroupMembersAsync(
        string groupObjectId,
        IReadOnlySet<string> desiredMemberObjectIds,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var currentMemberIds = await GetCurrentMemberIdsAsync(groupObjectId, errors, cancellationToken);

        var toAdd = desiredMemberObjectIds.Except(currentMemberIds, StringComparer.OrdinalIgnoreCase).ToList();
        var toRemove = currentMemberIds.Except(desiredMemberObjectIds, StringComparer.OrdinalIgnoreCase).ToList();

        var added = new List<string>();
        var removed = new List<string>();

        foreach (var userId in toAdd)
        {
            try
            {
                await graphServiceClient.Groups[groupObjectId].Members.Ref.PostAsync(new ReferenceCreate
                {
                    OdataId = $"https://graph.microsoft.com/v1.0/directoryObjects/{userId}"
                }, cancellationToken: cancellationToken);
                added.Add(userId);
            }
            catch (ODataError ex)
            {
                errors.Add($"Failed adding {userId} to group {groupObjectId}: {ex.Error?.Message}");
                logger.LogError(ex, "Failed adding user {UserId} to group {GroupId}", userId, groupObjectId);
            }
        }

        foreach (var userId in toRemove)
        {
            try
            {
                await graphServiceClient.Groups[groupObjectId].Members[userId].Ref.DeleteAsync(cancellationToken: cancellationToken);
                removed.Add(userId);
            }
            catch (ODataError ex)
            {
                errors.Add($"Failed removing {userId} from group {groupObjectId}: {ex.Error?.Message}");
                logger.LogError(ex, "Failed removing user {UserId} from group {GroupId}", userId, groupObjectId);
            }
        }

        return new GroupReconciliationResult(groupObjectId, added, removed, errors);
    }

    public async Task<IReadOnlyList<string>> GetCustomExtensionAttributeNamesAsync(CancellationToken cancellationToken = default)
    {
        var appObjectId = optionsMonitor.CurrentValue.SyncServiceAppObjectId;
        if (string.IsNullOrEmpty(appObjectId))
        {
            return [];
        }

        try
        {
            var page = await graphServiceClient.Applications[appObjectId].ExtensionProperties
                .GetAsync(rc => rc.QueryParameters.Select = ["name"], cancellationToken);

            var names = new List<string>();
            var iterator = PageIterator<ExtensionProperty, ExtensionPropertyCollectionResponse>
                .CreatePageIterator(graphServiceClient, page!, property =>
                {
                    if (!string.IsNullOrEmpty(property.Name))
                    {
                        names.Add(property.Name);
                    }
                    return true;
                });

            await iterator.IterateAsync(cancellationToken);
            return names;
        }
        catch (ODataError ex)
        {
            logger.LogWarning(ex, "Could not read registered directory extension properties; suggestions will fall back to the standard attribute list only.");
            return [];
        }
    }

    private async Task<HashSet<string>> GetCurrentMemberIdsAsync(
        string groupObjectId,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var memberIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var page = await graphServiceClient.Groups[groupObjectId].Members
                .GetAsync(rc => rc.QueryParameters.Select = ["id"], cancellationToken);

            var iterator = PageIterator<DirectoryObject, DirectoryObjectCollectionResponse>
                .CreatePageIterator(graphServiceClient, page!, member =>
                {
                    if (member.Id is not null)
                    {
                        memberIds.Add(member.Id);
                    }
                    return true;
                });

            await iterator.IterateAsync(cancellationToken);
        }
        catch (ODataError ex)
        {
            errors.Add($"Failed reading current membership for group {groupObjectId}: {ex.Error?.Message}");
            logger.LogError(ex, "Failed reading current membership for group {GroupId}", groupObjectId);
        }

        return memberIds;
    }
}
