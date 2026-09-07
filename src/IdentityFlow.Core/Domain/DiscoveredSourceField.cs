namespace IdentityFlow.Core.Domain;

/// <summary>
/// A Paycom field name actually observed in a sync run's employee data.
/// Populated automatically by <see cref="Sync.SyncOrchestrator"/> on every
/// run (dry run included) so the Field Mappings admin page can suggest
/// real, tenant-specific source field names instead of a static guess.
/// </summary>
public sealed class DiscoveredSourceField
{
    public required string FieldName { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}
