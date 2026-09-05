namespace PaycomEntraProvisioner.Graph;

/// <summary>
/// Configuration for the custom app registration this solution uses to
/// call Microsoft Graph: SubmitBulkUpload against the API-driven inbound
/// provisioning job (app role SynchronizationData-User.Upload) and direct
/// directory operations for assigned-group reconciliation (app role
/// Group.ReadWrite.All / User.Read.All). See docs/entra-app-setup.md for
/// the full app registration and consent walkthrough.
/// </summary>
public sealed class EntraOptions
{
    public const string SectionName = "Entra";

    public required string TenantId { get; set; }
    public required string ClientId { get; set; }

    /// <summary>Client secret. Prefer a certificate or managed identity in production - see docs/entra-app-setup.md.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// When true, uses <c>DefaultAzureCredential</c> (managed identity when
    /// running in Azure) instead of a client secret.
    /// </summary>
    public bool UseManagedIdentity { get; set; }

    /// <summary>
    /// Object id of the service principal for the "API-driven provisioning"
    /// enterprise application instance created from the Entra gallery.
    /// </summary>
    public required string ProvisioningServicePrincipalId { get; set; }

    /// <summary>Synchronization job id under that service principal (created when the provisioning job is configured).</summary>
    public required string ProvisioningJobId { get; set; }

    /// <summary>
    /// Object ID (not the Application/client ID) of this solution's own
    /// sync-service app registration, from its Overview page in the Entra
    /// admin center. Used only to look up custom directory extension
    /// attributes registered on it
    /// (<c>GET /applications/{id}/extensionProperties</c>) so the admin UI
    /// can suggest them as field-mapping targets. Requires the
    /// Application.Read.All application permission; leave null to skip
    /// this suggestion source entirely.
    /// </summary>
    public string? SyncServiceAppObjectId { get; set; }

    public string GraphBaseUrl { get; set; } = "https://graph.microsoft.com/v1.0";

    /// <summary>Max SCIM operations per bulkUpload call. Entra currently documents a limit of 50.</summary>
    public int BulkUploadBatchSize { get; set; } = 50;

    /// <summary>Max bulkUpload calls allowed per <see cref="ThrottleWindowSeconds"/>. Entra currently documents 40 calls / 5 seconds.</summary>
    public int MaxCallsPerWindow { get; set; } = 40;

    public int ThrottleWindowSeconds { get; set; } = 5;
}
