using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using System.Reflection;
using PaycomEntraProvisioner.Core.Configuration;
using PaycomEntraProvisioner.Core.Domain;
using PaycomEntraProvisioner.Core.Exceptions;
using PaycomEntraProvisioner.Core.Scim;

namespace PaycomEntraProvisioner.Core.Mapping;

/// <summary>
/// Builds the SCIM user resource submitted to Entra ID's API-driven
/// inbound provisioning job from an <see cref="EmployeeRecord"/> and the
/// tenant's configured <see cref="FieldMapping"/> list.
/// </summary>
public sealed class MappingEngine
{
    public ScimUserResource BuildScimResource(EmployeeRecord employee, IEnumerable<FieldMapping> mappings)
    {
        var resource = new ScimUserResource
        {
            ExternalId = employee.EmployeeCode,
            Active = employee.Status is EmploymentStatus.Active or EmploymentStatus.OnLeave or EmploymentStatus.PreHire
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

    /// <summary>
    /// Returns the identity anchor values (matching attributes) for an
    /// employee, used for diagnostics and for the "dry run" preview in the
    /// admin UI - not sent as a distinct payload, since matching itself is
    /// performed by the Entra provisioning service based on job configuration.
    /// </summary>
    public IReadOnlyDictionary<string, string?> ResolveMatchingAttributes(EmployeeRecord employee, IEnumerable<FieldMapping> mappings)
    {
        var result = new Dictionary<string, string?>();
        foreach (var mapping in mappings.Where(m => m.Enabled && m.IsMatchingAttribute))
        {
            result[mapping.TargetAttribute] = ApplyTransform(mapping, ResolveSourceValue(employee, mapping.SourceField), employee);
        }

        return result;
    }

    private static string? ResolveSourceValue(EmployeeRecord employee, string sourceField)
    {
        if (employee.RawFields.TryGetValue(sourceField, out var rawValue))
        {
            return rawValue;
        }

        var property = typeof(EmployeeRecord).GetProperty(
            sourceField,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        return property?.GetValue(employee)?.ToString();
    }

    private static string? ApplyTransform(FieldMapping mapping, string? rawValue, EmployeeRecord employee)
    {
        if (string.IsNullOrWhiteSpace(mapping.TransformExpression))
        {
            return rawValue;
        }

        try
        {
            var valueParam = Expression.Parameter(typeof(string), "value");
            var employeeParam = Expression.Parameter(typeof(EmployeeRecord), "employee");
            var lambda = DynamicExpressionParser.ParseLambda(
                [valueParam, employeeParam],
                typeof(object),
                mapping.TransformExpression);

            var result = lambda.Compile().DynamicInvoke(rawValue, employee);
            return result?.ToString();
        }
        catch (Exception ex) when (ex is not ExpressionEvaluationException)
        {
            throw new ExpressionEvaluationException(
                mapping.TransformExpression,
                $"field mapping '{mapping.SourceField}' -> '{mapping.TargetAttribute}' (employee {employee.EmployeeCode})",
                ex);
        }
    }

    private static void AssignTargetAttribute(ScimUserResource resource, FieldMapping mapping, string? value)
    {
        if (mapping.IsExtensionAttribute)
        {
            resource.ExtensionAttributes[mapping.TargetAttribute] = value;
            return;
        }

        switch (mapping.TargetAttribute.ToLowerInvariant())
        {
            case "username":
            case "userprincipalname":
                resource.UserName = value;
                break;
            case "displayname":
                resource.DisplayName = value;
                break;
            case "givenname":
            case "firstname":
                (resource.Name ??= new()).GivenName = value;
                break;
            case "familyname":
            case "lastname":
                (resource.Name ??= new()).FamilyName = value;
                break;
            case "middlename":
                (resource.Name ??= new()).MiddleName = value;
                break;
            case "honorificsuffix":
                (resource.Name ??= new()).HonorificSuffix = value;
                break;
            case "title":
            case "jobtitle":
                resource.Title = value;
                break;
            case "department":
                resource.Department = value;
                break;
            case "mail":
            case "email":
            case "workemail":
                if (!string.IsNullOrEmpty(value))
                {
                    resource.Emails ??= [];
                    resource.Emails.Add(new ScimTypedValue { Value = value, Type = "work", Primary = true });
                }
                break;
            case "mobilephone":
                if (!string.IsNullOrEmpty(value))
                {
                    resource.PhoneNumbers ??= [];
                    resource.PhoneNumbers.Add(new ScimTypedValue { Value = value, Type = "mobile" });
                }
                break;
            case "workphone":
                if (!string.IsNullOrEmpty(value))
                {
                    resource.PhoneNumbers ??= [];
                    resource.PhoneNumbers.Add(new ScimTypedValue { Value = value, Type = "work" });
                }
                break;
            case "manager":
            case "manageremployeecode":
                if (!string.IsNullOrEmpty(value))
                {
                    resource.Manager = new ScimManager { Value = value };
                }
                break;
            case "streetaddress":
                (resource.Addresses ??= [new ScimAddress()])[0].StreetAddress = value;
                break;
            case "city":
                (resource.Addresses ??= [new ScimAddress()])[0].Locality = value;
                break;
            case "state":
                (resource.Addresses ??= [new ScimAddress()])[0].Region = value;
                break;
            case "postalcode":
                (resource.Addresses ??= [new ScimAddress()])[0].PostalCode = value;
                break;
            case "country":
                (resource.Addresses ??= [new ScimAddress()])[0].Country = value;
                break;
            default:
                // Arbitrary flat attribute path not covered above, e.g. a
                // directory schema extension property name.
                resource.AdditionalAttributes[mapping.TargetAttribute] = value;
                break;
        }
    }
}
