namespace PaycomEntraProvisioner.Core.Domain;

/// <summary>
/// Canonical, source-agnostic representation of a worker pulled from Paycom.
/// Field names follow Paycom's common "Employee Master File" report layout;
/// customers whose Paycom report differs can adjust <see cref="RawFields"/>
/// and the field mapping configuration without touching this shape.
/// </summary>
public sealed class EmployeeRecord
{
    public required string EmployeeCode { get; init; }

    public string? FirstName { get; init; }
    public string? MiddleName { get; init; }
    public string? LastName { get; init; }
    public string? PreferredFirstName { get; init; }
    public string? Suffix { get; init; }

    public string? WorkEmail { get; init; }
    public string? PersonalEmail { get; init; }
    public string? WorkPhone { get; init; }
    public string? MobilePhone { get; init; }

    public string? JobTitle { get; init; }
    public string? Department { get; init; }
    public string? DepartmentCode { get; init; }
    public string? Division { get; init; }
    public string? CostCenter { get; init; }
    public string? WorkLocationCode { get; init; }
    public string? WorkLocationName { get; init; }
    public string? EmployeeType { get; init; }
    public string? ManagerEmployeeCode { get; init; }

    public string? StreetAddress { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }

    public EmploymentStatus Status { get; init; } = EmploymentStatus.Unknown;
    public DateOnly? HireDate { get; init; }
    public DateOnly? RehireDate { get; init; }
    public DateOnly? TerminationDate { get; init; }

    /// <summary>
    /// Every field Paycom returned for this worker, keyed by the source field
    /// name as configured in the Paycom report/export. Field mappings resolve
    /// against this dictionary first, then fall back to the strongly typed
    /// properties above by convention name.
    /// </summary>
    public IReadOnlyDictionary<string, string?> RawFields { get; init; } =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether this record has enough of an identity anchor (a work email)
    /// to attempt provisioning at all. Deliberately status-independent: a
    /// Terminated worker still needs to flow through so the provisioning
    /// job can flip them to disabled - only workers we can't match at all
    /// are skipped.
    /// </summary>
    public bool IsProvisionable => !string.IsNullOrWhiteSpace(WorkEmail);

    /// <summary>
    /// Stable SHA-256 hash of every raw field, used to detect whether an
    /// employee's data changed since the last run without diffing every
    /// property by hand.
    /// </summary>
    public string ComputeContentHash()
    {
        var canonical = string.Join(
            '',
            RawFields.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(bytes);
    }
}
