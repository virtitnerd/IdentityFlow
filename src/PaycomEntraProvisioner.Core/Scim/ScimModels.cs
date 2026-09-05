using System.Text.Json.Serialization;

namespace PaycomEntraProvisioner.Core.Scim;

/// <summary>
/// Wire model for the payload accepted by the Microsoft Entra ID
/// API-driven inbound provisioning "bulkUpload" endpoint:
///   POST /servicePrincipals/{servicePrincipalId}/synchronization/jobs/{jobId}/bulkUpload
/// The service applies SCIM Bulk (RFC 7644 §3.7) conventions: each
/// operation carries a full resource under <see cref="ScimBulkOperation.Data"/>
/// and the provisioning engine - not the caller - determines whether the
/// result is a create, update, enable, or disable based on its own matching
/// and attribute-mapping rules. Verify the exact field names against the
/// current Microsoft Learn reference for your tenant's API version before
/// go-live; this model is intentionally forgiving (extra/unknown properties
/// round-trip via <see cref="ScimUserResource.AdditionalSchemas"/>).
/// </summary>
public sealed class ScimBulkRequest
{
    [JsonPropertyName("schemas")]
    public List<string> Schemas { get; init; } = ["urn:ietf:params:scim:api:messages:2.0:BulkRequest"];

    [JsonPropertyName("Operations")]
    public List<ScimBulkOperation> Operations { get; init; } = [];
}

public sealed class ScimBulkOperation
{
    [JsonPropertyName("method")]
    public string Method { get; init; } = "POST";

    [JsonPropertyName("bulkId")]
    public required string BulkId { get; init; }

    [JsonPropertyName("data")]
    public required ScimUserResource Data { get; init; }
}

/// <summary>
/// SCIM core User resource plus the Entra custom extension schema used to
/// carry extensionAttribute1-15 / directory schema extension values.
/// </summary>
public sealed class ScimUserResource
{
    public const string CoreUserSchema = "urn:ietf:params:scim:schemas:core:2.0:User";
    public const string EntraExtensionSchema = "urn:ietf:params:scim:schemas:extension:ExtensionAttributes";

    [JsonPropertyName("schemas")]
    public List<string> Schemas { get; init; } = [CoreUserSchema];

    [JsonPropertyName("externalId")]
    public string? ExternalId { get; set; }

    [JsonPropertyName("userName")]
    public string? UserName { get; set; }

    [JsonPropertyName("active")]
    public bool Active { get; set; } = true;

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("name")]
    public ScimName? Name { get; set; }

    [JsonPropertyName("emails")]
    public List<ScimTypedValue>? Emails { get; set; }

    [JsonPropertyName("phoneNumbers")]
    public List<ScimTypedValue>? PhoneNumbers { get; set; }

    [JsonPropertyName("addresses")]
    public List<ScimAddress>? Addresses { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("department")]
    public string? Department { get; set; }

    [JsonPropertyName("manager")]
    public ScimManager? Manager { get; set; }

    /// <summary>
    /// Additional flat attributes (core schema attributes not modeled above,
    /// e.g. "employeeOrgData.division", "employeeOrgData.costCenter") keyed
    /// exactly as the target field mapping specifies.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, object?> AdditionalAttributes { get; init; } = [];

    /// <summary>
    /// Values destined for the Entra extension attribute schema, e.g.
    /// { "extensionAttribute1": "12345", "extensionAttribute2": "Sales" }.
    /// Serialized under the <see cref="EntraExtensionSchema"/> key.
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, object?> ExtensionAttributes { get; } = [];

    /// <summary>
    /// Populates the extension schema key in <see cref="AdditionalAttributes"/>
    /// and registers the schema URN. Call once all extension values have
    /// been set.
    /// </summary>
    public void FinalizeExtensionSchema()
    {
        if (ExtensionAttributes.Count == 0)
        {
            return;
        }

        if (!Schemas.Contains(EntraExtensionSchema))
        {
            Schemas.Add(EntraExtensionSchema);
        }

        AdditionalAttributes[EntraExtensionSchema] = ExtensionAttributes;
    }

    [JsonIgnore]
    public Dictionary<string, object?> AdditionalSchemas => AdditionalAttributes;
}

public sealed class ScimName
{
    [JsonPropertyName("givenName")]
    public string? GivenName { get; set; }

    [JsonPropertyName("familyName")]
    public string? FamilyName { get; set; }

    [JsonPropertyName("middleName")]
    public string? MiddleName { get; set; }

    [JsonPropertyName("honorificSuffix")]
    public string? HonorificSuffix { get; set; }
}

public sealed class ScimTypedValue
{
    [JsonPropertyName("value")]
    public required string Value { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("primary")]
    public bool? Primary { get; init; }
}

public sealed class ScimAddress
{
    [JsonPropertyName("streetAddress")]
    public string? StreetAddress { get; set; }

    [JsonPropertyName("locality")]
    public string? Locality { get; set; }

    [JsonPropertyName("region")]
    public string? Region { get; set; }

    [JsonPropertyName("postalCode")]
    public string? PostalCode { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = "work";
}

public sealed class ScimManager
{
    [JsonPropertyName("value")]
    public required string Value { get; init; }
}

/// <summary>
/// Deserialized response from the bulkUpload endpoint / the provisioning
/// log, used to correlate per-employee outcomes back to the request.
/// </summary>
public sealed class ScimBulkResponse
{
    [JsonPropertyName("Operations")]
    public List<ScimBulkOperationResult> Operations { get; init; } = [];
}

public sealed class ScimBulkOperationResult
{
    [JsonPropertyName("bulkId")]
    public string? BulkId { get; init; }

    [JsonPropertyName("status")]
    public ScimStatus? Status { get; init; }

    [JsonPropertyName("location")]
    public string? Location { get; init; }
}

public sealed class ScimStatus
{
    [JsonPropertyName("code")]
    public string? Code { get; init; }
}
