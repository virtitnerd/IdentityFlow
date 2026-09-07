using PaycomEntraProvisioner.Core.Configuration;
using PaycomEntraProvisioner.Core.Domain;
using PaycomEntraProvisioner.Core.Expressions;
using PaycomEntraProvisioner.Core.Scim;

namespace PaycomEntraProvisioner.Core.Mapping;

/// <summary>
/// Builds the SCIM user resource submitted to Entra ID's API-driven
/// inbound provisioning job from an <see cref="EmployeeRecord"/> and the
/// tenant's configured <see cref="FieldMapping"/> list.
/// </summary>
public sealed class MappingEngine
{
    /// <summary>
    /// Single source of truth for every core SCIM/User attribute this
    /// engine recognizes by name: its aliases and how to assign it onto a
    /// <see cref="ScimUserResource"/>. Both <see cref="AssignTargetAttribute"/>
    /// and <see cref="KnownEntraAttributes.StandardAttributes"/> are driven
    /// from this one table so the two can no longer drift out of sync with
    /// each other the way a hand-maintained switch and a hand-maintained
    /// suggestion list could.
    /// </summary>
    private static readonly IReadOnlyList<AttributeHandler> AttributeHandlers =
    [
        new("userPrincipalName", ["username"], (r, v) => r.UserName = v),
        new("displayName", [], (r, v) => r.DisplayName = v),
        new("givenName", ["firstname"], (r, v) => (r.Name ??= new()).GivenName = v),
        new("familyName", ["lastname"], (r, v) => (r.Name ??= new()).FamilyName = v),
        new("middleName", [], (r, v) => (r.Name ??= new()).MiddleName = v),
        new("honorificSuffix", [], (r, v) => (r.Name ??= new()).HonorificSuffix = v),
        new("jobTitle", ["title"], (r, v) => r.Title = v),
        new("department", [], (r, v) => r.Department = v),
        new("mail", ["email", "workemail"], (r, v) =>
        {
            if (!string.IsNullOrEmpty(v))
            {
                r.Emails ??= [];
                r.Emails.Add(new ScimTypedValue { Value = v, Type = "work", Primary = true });
            }
        }),
        new("mobilePhone", [], (r, v) =>
        {
            if (!string.IsNullOrEmpty(v))
            {
                r.PhoneNumbers ??= [];
                r.PhoneNumbers.Add(new ScimTypedValue { Value = v, Type = "mobile" });
            }
        }),
        new("workPhone", [], (r, v) =>
        {
            if (!string.IsNullOrEmpty(v))
            {
                r.PhoneNumbers ??= [];
                r.PhoneNumbers.Add(new ScimTypedValue { Value = v, Type = "work" });
            }
        }),
        new("manager", ["manageremployeecode"], (r, v) =>
        {
            if (!string.IsNullOrEmpty(v))
            {
                r.Manager = new ScimManager { Value = v };
            }
        }),
        new("streetAddress", [], (r, v) => (r.Addresses ??= [new ScimAddress()])[0].StreetAddress = v),
        new("city", [], (r, v) => (r.Addresses ??= [new ScimAddress()])[0].Locality = v),
        new("state", [], (r, v) => (r.Addresses ??= [new ScimAddress()])[0].Region = v),
        new("postalCode", [], (r, v) => (r.Addresses ??= [new ScimAddress()])[0].PostalCode = v),
        new("country", [], (r, v) => (r.Addresses ??= [new ScimAddress()])[0].Country = v)
    ];

    /// <summary>
    /// Canonical names of every attribute in <see cref="AttributeHandlers"/>,
    /// used by <see cref="KnownEntraAttributes.StandardAttributes"/> to
    /// populate the admin UI's suggestion list from the same table that
    /// actually drives attribute assignment.
    /// </summary>
    internal static IReadOnlyList<string> SupportedTargetAttributeNames { get; } =
        [.. AttributeHandlers.Select(h => h.CanonicalName)];

    private sealed record AttributeHandler(string CanonicalName, string[] Aliases, Action<ScimUserResource, string?> Assign)
    {
        public bool Matches(string targetAttribute) =>
            string.Equals(targetAttribute, CanonicalName, StringComparison.OrdinalIgnoreCase)
            || Aliases.Contains(targetAttribute, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds the SCIM resource for one employee.
    /// </summary>
    /// <param name="asOfDate">
    /// Passed in explicitly (rather than read from the clock in here) so
    /// activation/deactivation timing stays a pure, easily-testable
    /// function of its inputs.
    /// </param>
    /// <param name="enableAccountDayOffset">
    /// Days relative to <see cref="EmployeeRecord.HireDate"/> at which a
    /// PreHire worker's account becomes active - 0 means exactly on the
    /// hire date, negative activates early, positive delays activation.
    /// Sourced from the admin-configured
    /// <see cref="Domain.LifecycleTaskType.EnableAccount"/> task; defaults
    /// to 0 (activate on the hire date, never before) if none is configured.
    /// </param>
    /// <param name="disableAccountDayOffset">
    /// Days relative to <see cref="EmployeeRecord.TerminationDate"/> at
    /// which a Terminated worker's account is disabled - 0 means exactly
    /// on the termination date (a real termination date in the future, as
    /// HR systems commonly enter during a notice period, keeps the account
    /// active until then rather than disabling it immediately), negative
    /// disables early, positive grants a short grace period. Sourced from
    /// the admin-configured <see cref="Domain.LifecycleTaskType.DisableAccount"/>
    /// task; defaults to 0 if none is configured.
    /// </param>
    public ScimUserResource BuildScimResource(
        EmployeeRecord employee,
        IEnumerable<FieldMapping> mappings,
        DateOnly asOfDate,
        int enableAccountDayOffset = 0,
        int disableAccountDayOffset = 0)
    {
        var resource = new ScimUserResource
        {
            ExternalId = employee.EmployeeCode,
            Active = ComputeActive(employee, asOfDate, enableAccountDayOffset, disableAccountDayOffset)
        };

        foreach (var mapping in mappings.Where(m => m.Enabled).OrderBy(m => m.IsExtensionAttribute))
        {
            var rawValue = ResolveSourceValue(employee, mapping.SourceField);
            var finalValue = ApplyTransform(mapping, rawValue, employee);

            if (string.IsNullOrEmpty(finalValue) && !mapping.SendNullToClearValue)
            {
                continue;
            }

            AssignTargetAttribute(resource, mapping, finalValue);
        }

        resource.FinalizeExtensionSchema();
        return resource;
    }

    private static bool ComputeActive(EmployeeRecord employee, DateOnly asOfDate, int enableAccountDayOffset, int disableAccountDayOffset) =>
        employee.Status switch
        {
            EmploymentStatus.Active or EmploymentStatus.OnLeave => true,

            // Not active until the configured offset from the hire date -
            // a worker entered in Paycom weeks ahead of their first day
            // should be created (so IT can pre-stage access) but stay
            // disabled until it's actually their start date.
            EmploymentStatus.PreHire => employee.HireDate is { } hireDate
                && asOfDate >= hireDate.AddDays(enableAccountDayOffset),

            // Stays active until the configured offset from the
            // termination date, not the moment the status changes - HR
            // systems commonly record a termination during a notice
            // period with a future last day, and disabling immediately
            // would cut access before that day actually arrives. No
            // termination date at all is treated as "disable now" - the
            // safer direction to guess wrong in.
            EmploymentStatus.Terminated => employee.TerminationDate is { } terminationDate
                && asOfDate < terminationDate.AddDays(disableAccountDayOffset),

            _ => false
        };

    /// <summary>
    /// Returns the identity anchor values (matching attributes) for an
    /// employee, used for diagnostics and for the "dry run" preview in the
    /// admin UI - not sent as a distinct payload, since matching itself is
    /// performed by the Entra provisioning service based on job configuration.
    /// </summary>
    public IReadOnlyDictionary<string, string?> ResolveMatchingAttributes(EmployeeRecord employee, IEnumerable<FieldMapping> mappings)
    {
        // Case-insensitive so a lookup like GetValueOrDefault("userPrincipalName")
        // still finds a mapping the admin entered as "UserPrincipalName" -
        // TargetAttribute is free text, not validated against a fixed casing.
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in mappings.Where(m => m.Enabled && m.IsMatchingAttribute))
        {
            result[mapping.TargetAttribute] = ApplyTransform(mapping, ResolveSourceValue(employee, mapping.SourceField), employee);
        }

        return result;
    }

    // RawFields already contains every field Paycom returned, verbatim,
    // under its native Paycom name - the only names FieldMapping.SourceField
    // is documented to reference. A reflection fallback onto EmployeeRecord's
    // typed properties used to sit here too, but it was a pure footgun: a
    // mapping could reference a typed property name by convention (e.g.
    // "WorkEmail" instead of "Email_Work") and silently stop working the
    // moment that property was ever renamed, with no compiler or run-time
    // error anywhere in the pipeline.
    private static string? ResolveSourceValue(EmployeeRecord employee, string sourceField) =>
        employee.RawFields.GetValueOrDefault(sourceField);

    private static string? ApplyTransform(FieldMapping mapping, string? rawValue, EmployeeRecord employee)
    {
        if (string.IsNullOrWhiteSpace(mapping.TransformExpression))
        {
            return rawValue;
        }

        var result = DynamicExpressionEvaluator.Evaluate(
            mapping.TransformExpression,
            $"field mapping '{mapping.SourceField}' -> '{mapping.TargetAttribute}' (employee {employee.EmployeeCode})",
            typeof(object),
            [("value", typeof(string), rawValue), ("employee", typeof(EmployeeRecord), employee)]);

        return result?.ToString();
    }

    private static void AssignTargetAttribute(ScimUserResource resource, FieldMapping mapping, string? value)
    {
        if (mapping.IsExtensionAttribute)
        {
            resource.ExtensionAttributes[mapping.TargetAttribute] = value;
            return;
        }

        var handler = AttributeHandlers.FirstOrDefault(h => h.Matches(mapping.TargetAttribute));
        if (handler is not null)
        {
            handler.Assign(resource, value);
        }
        else
        {
            // Arbitrary flat attribute path not covered above, e.g. a
            // directory schema extension property name.
            resource.AdditionalAttributes[mapping.TargetAttribute] = value;
        }
    }
}
