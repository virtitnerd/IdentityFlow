using PaycomEntraProvisioner.Core.Configuration;
using PaycomEntraProvisioner.Core.Domain;
using PaycomEntraProvisioner.Core.Mapping;
using Xunit;

namespace PaycomEntraProvisioner.Core.Tests;

public class MappingEngineTests
{
    private static EmployeeRecord SampleEmployee() => new()
    {
        EmployeeCode = "E100",
        FirstName = "Jane",
        LastName = "Doe",
        WorkEmail = "jane.doe@contoso.com",
        Department = "Sales",
        Status = EmploymentStatus.Active,
        RawFields = new Dictionary<string, string?>
        {
            ["Email_Work"] = "jane.doe@contoso.com",
            ["Employee_Code"] = "E100",
            ["Cost_Center"] = "CC-42"
        }
    };

    [Fact]
    public void BuildScimResource_MapsCoreAttributes()
    {
        var engine = new MappingEngine();
        var mappings = new List<FieldMapping>
        {
            new() { SourceField = "Email_Work", TargetAttribute = "userPrincipalName", IsMatchingAttribute = true },
            new() { SourceField = "FirstName", TargetAttribute = "givenName" },
            new() { SourceField = "LastName", TargetAttribute = "familyName" },
        };

        var resource = engine.BuildScimResource(SampleEmployee(), mappings);

        Assert.Equal("jane.doe@contoso.com", resource.UserName);
        Assert.Equal("Jane", resource.Name?.GivenName);
        Assert.Equal("Doe", resource.Name?.FamilyName);
        Assert.True(resource.Active);
        Assert.Equal("E100", resource.ExternalId);
    }

    [Fact]
    public void BuildScimResource_WritesExtensionAttributes()
    {
        var engine = new MappingEngine();
        var mappings = new List<FieldMapping>
        {
            new() { SourceField = "Cost_Center", TargetAttribute = "extensionAttribute1", IsExtensionAttribute = true }
        };

        var resource = engine.BuildScimResource(SampleEmployee(), mappings);
        resource.FinalizeExtensionSchema();

        Assert.Contains(Scim.ScimUserResource.EntraExtensionSchema, resource.Schemas);
        Assert.Equal("CC-42", resource.ExtensionAttributes["extensionAttribute1"]);
    }

    [Fact]
    public void BuildScimResource_AppliesTransformExpression()
    {
        var engine = new MappingEngine();
        var mappings = new List<FieldMapping>
        {
            new() { SourceField = "Department", TargetAttribute = "department", TransformExpression = "value.ToUpper()" }
        };

        var resource = engine.BuildScimResource(SampleEmployee(), mappings);

        Assert.Equal("SALES", resource.Department);
    }

    [Fact]
    public void BuildScimResource_InactiveForTerminatedEmployee()
    {
        var engine = new MappingEngine();
        var record = new EmployeeRecord
        {
            EmployeeCode = "E100",
            WorkEmail = "jane.doe@contoso.com",
            Status = EmploymentStatus.Terminated
        };

        var resource = engine.BuildScimResource(record, []);

        Assert.False(resource.Active);
    }
}
