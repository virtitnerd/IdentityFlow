using PaycomEntraProvisioner.Core.Scim;

namespace PaycomEntraProvisioner.Core.Abstractions;

public sealed record BulkUploadResult(int BatchesSubmitted, IReadOnlyList<ScimBulkOperationResult> Results, IReadOnlyList<string> BatchErrors);

public sealed record ProvisioningLogEntry(
    string? EmployeeExternalId,
    string Action,
    string ProvisioningStatus,
    DateTimeOffset Timestamp,
    string? ErrorMessage);

/// <summary>
/// Talks to the Microsoft Entra ID API-driven inbound provisioning job:
/// submits SCIM bulk operations and reads back provisioning log results.
/// The provisioning service - not this client - decides whether each
/// operation is a create, update, enable, or disable based on its own
/// matching and attribute-mapping configuration.
/// </summary>
public interface IEntraProvisioningClient
{
    /// <summary>
    /// Submits operations to the job's bulkUpload endpoint, automatically
    /// chunking to the documented limits (up to 50 operations per call, 40
    /// calls per 5-second window) and honoring 429 responses.
    /// </summary>
    Task<BulkUploadResult> SubmitBulkUploadAsync(IReadOnlyList<ScimBulkOperation> operations, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProvisioningLogEntry>> GetRecentProvisioningLogAsync(int top = 100, CancellationToken cancellationToken = default);
}
