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
    /// Other standard Microsoft Entra ID / Azure AD Connect provisioning-
    /// schema attributes with no special SCIM sub-object shape (unlike, say,
    /// <c>name</c> or <c>emails</c> in <see cref="StandardAttributes"/>).
    /// <see cref="MappingEngine"/> already writes any target attribute name
    /// it doesn't explicitly recognize as a flat attribute on the outgoing
    /// SCIM resource, so nothing here is required for a mapping to work -
    /// this list exists purely so these commonly-useful attributes show up
    /// as suggestions instead of having to be typed from memory.
    ///
    /// Before relying on any of these, confirm it's also listed on the
    /// API-driven provisioning job's own Attribute Mapping (Advanced Options
    /// -&gt; Edit target User attributes) - the same requirement documented
    /// for custom directory extensions in docs/entra-app-setup.md, since
    /// Entra's provisioning job (not this app) is what actually decides
    /// which attributes on an incoming record get written to the directory.
    /// <c>employeeLeaveDateTime</c> specifically is treated by Microsoft as
    /// a sensitive attribute requiring an extra one-time consent/role grant
    /// beyond normal Graph API permissions - verify current Microsoft Learn
    /// guidance before mapping to it.
    /// </summary>
    public static readonly IReadOnlyList<string> AdditionalWritableAttributes =
    [
        "employeeId",
        "employeeType",
        "employeeHireDate",
        "employeeLeaveDateTime",
        "usageLocation",
        "preferredLanguage",
        "mailNickname",
        "userType",
        "physicalDeliveryOfficeName",
        "telephoneNumber",
        "facsimileTelephoneNumber",
        "showInAddressList"
    ];
}
