namespace PaycomEntraProvisioner.Core.Mapping;

/// <summary>
/// Reference lists used to populate suggestions (not hard restrictions -
/// see <see cref="Configuration.FieldMapping.TargetAttribute"/>) for the
/// Entra target attribute field in the admin UI.
/// </summary>
public static class KnownEntraAttributes
{
    /// <summary>
    /// Every core SCIM/User attribute name <see cref="MappingEngine"/>
    /// recognizes explicitly, sourced directly from the same table that
    /// drives attribute assignment (<c>MappingEngine.AttributeHandlers</c>)
    /// so the two can't drift out of sync the way two independently
    /// hand-maintained lists could. Anything not in this list still works
    /// as a target - it's written as a flat attribute path via
    /// <c>ScimUserResource.AdditionalAttributes</c> - so this is a
    /// suggestion list, not a validation whitelist.
    /// </summary>
    public static readonly IReadOnlyList<string> StandardAttributes = MappingEngine.SupportedTargetAttributeNames;

    /// <summary>
    /// The fixed set of fifteen generic extension attributes every Entra
    /// ID tenant has, inherited from the on-premises Exchange schema.
    /// </summary>
    public static readonly IReadOnlyList<string> ExtensionAttributeSlots =
        Enumerable.Range(1, 15).Select(i => $"extensionAttribute{i}").ToArray();
}
