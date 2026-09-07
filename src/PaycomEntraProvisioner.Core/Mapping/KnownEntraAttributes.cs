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

    /// <summary>
    /// Other genuine SCIM Core User schema attributes (RFC 7643 §4.1) with
    /// no special sub-object shape that <see cref="MappingEngine"/> doesn't
    /// give a dedicated handler to - it still writes them correctly as flat
    /// top-level attributes via <c>ScimUserResource.AdditionalAttributes</c>,
    /// same as <see cref="StandardAttributes"/>'s handled ones, just without
    /// needing a handler since they need no nesting. This list exists purely
    /// for suggestion purposes.
    ///
    /// IMPORTANT: this is deliberately a short, spec-verified list, not a
    /// grab-bag of "things Entra can store." A name like <c>employeeHireDate</c>
    /// or <c>usageLocation</c> is a real Microsoft Graph *directory*
    /// attribute name - but it is not a SCIM attribute name, and typing it
    /// as a target here would produce a bare top-level JSON key that sits
    /// outside every schema Entra's provisioning job recognizes, so it
    /// couldn't be attribute-mapped on the Entra side at all (this is
    /// exactly the bug this list previously had - see git history). To
    /// reach a directory-only attribute that has no SCIM equivalent, the
    /// admin must first extend the provisioning job's own schema with a
    /// custom SCIM attribute for it (Attribute Mapping -&gt; Advanced Options
    /// -&gt; Edit target User attributes - the same mechanism documented for
    /// custom directory extensions in docs/entra-app-setup.md), then target
    /// that custom SCIM attribute name here - not the directory attribute's
    /// own name.
    /// </summary>
    public static readonly IReadOnlyList<string> AdditionalWritableAttributes =
    [
        "userType",
        "preferredLanguage",
        "nickName",
        "profileUrl",
        "locale",
        "timezone"
    ];
}
