namespace PaycomEntraProvisioner.Core.Configuration;

/// <summary>
/// Maps a single Paycom field onto a target attribute on the Entra ID user
/// object, expressed as it must appear in the SCIM bulk payload sent to the
/// API-driven inbound provisioning job.
/// </summary>
public sealed class FieldMapping
{
    public int Id { get; set; }

    /// <summary>
    /// Name of the field as it appears in <see cref="Domain.EmployeeRecord.RawFields"/>,
    /// e.g. "Employee_Code", "Work_Email", "Department_Description".
    /// </summary>
    public required string SourceField { get; set; }

    /// <summary>
    /// Target attribute path. Either a core SCIM/user attribute
    /// (e.g. "displayName", "userPrincipalName", "department") or a custom
    /// extension attribute (e.g. "extensionAttribute1" mapped through the
    /// Entra ID custom SCIM schema extension, or a directory extension such
    /// as "extension_&lt;appId&gt;_CostCenter").
    /// </summary>
    public required string TargetAttribute { get; set; }

    /// <summary>
    /// True when <see cref="TargetAttribute"/> should be written under the
    /// Entra custom extension schema
    /// (urn:ietf:params:scim:schemas:extension:ExtensionAttributes) instead
    /// of the base SCIM user schema. Used for extensionAttribute1-15 and
    /// directory schema extensions surfaced for dynamic group rules or other
    /// applications.
    /// </summary>
    public bool IsExtensionAttribute { get; set; }

    /// <summary>
    /// Optional transform applied to the source value before it is written,
    /// evaluated as a System.Linq.Dynamic.Core expression against a single
    /// implicit variable named <c>value</c> (the raw source string) and
    /// <c>employee</c> (the full <see cref="Domain.EmployeeRecord"/>).
    /// Examples: "value.ToUpper()", "employee.FirstName + \".\" + employee.LastName + \"@contoso.com\"".
    /// Leave null/empty to copy the source value verbatim.
    /// </summary>
    public string? TransformExpression { get; set; }

    /// <summary>
    /// When true, a null/blank source value is still sent (clearing the
    /// target attribute in Entra ID). When false, blank values are omitted
    /// from the payload entirely so the existing Entra value is left alone.
    /// </summary>
    public bool SendNullToClearValue { get; set; }

    /// <summary>
    /// Marks this mapping as one of the identity anchor fields Entra ID uses
    /// to match an incoming record to an existing user (e.g. employeeId,
    /// userPrincipalName, mail). At least one mapping must set this.
    /// </summary>
    public bool IsMatchingAttribute { get; set; }

    public bool Enabled { get; set; } = true;

    public string? Notes { get; set; }
}
