using PaycomEntraProvisioner.Core.Configuration;
using PaycomEntraProvisioner.Core.Domain;
using PaycomEntraProvisioner.Core.Mapping;
using Xunit;

namespace PaycomEntraProvisioner.Core.Tests;

public class MappingEngineTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

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
            ["First_Name"] = "Jane",
            ["Last_Name"] = "Doe",
            ["Department_Description"] = "Sales",
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
            new() { SourceField = "First_Name", TargetAttribute = "givenName" },
            new() { SourceField = "Last_Name", TargetAttribute = "familyName" },
        };

        var resource = engine.BuildScimResource(SampleEmployee(), mappings, Today);

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

        var resource = engine.BuildScimResource(SampleEmployee(), mappings, Today);
        resource.FinalizeExtensionSchema();

        Assert.Contains(Scim.ScimUserResource.EntraExtensionSchema, resource.Schemas);
        Assert.Equal("CC-42", resource.ExtensionAttributes["extensionAttribute1"]);
    }

    [Fact]
    public void BuildScimResource_WritesUnrecognizedTargetAttributeAsFlatAttribute()
    {
        // The engine must not require every possible Entra target attribute
        // to have dedicated handling: anything not in AttributeHandlers -
        // e.g. employeeHireDate, or any other standard/custom attribute
        // this engine has no special SCIM sub-object shape for - still has
        // to be settable, just as a flat top-level attribute rather than
        // one assembled into a typed sub-object like name/emails/manager.
        var engine = new MappingEngine();
        var employee = new EmployeeRecord
        {
            EmployeeCode = "E100",
            WorkEmail = "jane.doe@contoso.com",
            Status = EmploymentStatus.Active,
            RawFields = new Dictionary<string, string?> { ["Hire_Date"] = "2026-01-15" }
        };
        var mappings = new List<FieldMapping>
        {
            new() { SourceField = "Hire_Date", TargetAttribute = "employeeHireDate" }
        };

        var resource = engine.BuildScimResource(employee, mappings, Today);

        Assert.Equal("2026-01-15", resource.AdditionalAttributes["employeeHireDate"]);
    }

    [Fact]
    public void BuildScimResource_AppliesTransformExpression()
    {
        var engine = new MappingEngine();
        var mappings = new List<FieldMapping>
        {
            new() { SourceField = "Department_Description", TargetAttribute = "department", TransformExpression = "value.ToUpper()" }
        };

        var resource = engine.BuildScimResource(SampleEmployee(), mappings, Today);

        Assert.Equal("SALES", resource.Department);
    }

    [Fact]
    public void BuildScimResource_DoesNotFallBackToClrPropertyNamesForSourceField()
    {
        // Regression guard: SourceField must resolve against RawFields (the
        // Paycom-native field names) only. A reflection fallback onto
        // EmployeeRecord's typed CLR property names used to exist here and
        // was removed - it was a footgun where a mapping conventionally
        // named after a property (e.g. "FirstName" instead of the real
        // Paycom field "First_Name") would silently break the moment that
        // property was ever renamed, with nothing to catch it.
        var engine = new MappingEngine();
        var mappings = new List<FieldMapping>
        {
            new() { SourceField = "FirstName", TargetAttribute = "givenName" }
        };

        var resource = engine.BuildScimResource(SampleEmployee(), mappings, Today);

        Assert.Null(resource.Name?.GivenName);
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

        var resource = engine.BuildScimResource(record, [], Today);

        Assert.False(resource.Active);
    }

    [Fact]
    public void BuildScimResource_PreHireStaysInactiveUntilHireDatePlusOffset()
    {
        var engine = new MappingEngine();
        var record = new EmployeeRecord
        {
            EmployeeCode = "E101",
            WorkEmail = "new.hire@contoso.com",
            Status = EmploymentStatus.PreHire,
            HireDate = Today
        };

        var beforeOffsetElapsed = engine.BuildScimResource(record, [], Today, enableAccountDayOffset: 3);
        var afterOffsetElapsed = engine.BuildScimResource(record, [], Today.AddDays(3), enableAccountDayOffset: 3);

        Assert.False(beforeOffsetElapsed.Active);
        Assert.True(afterOffsetElapsed.Active);
    }

    [Fact]
    public void BuildScimResource_TerminatedEmployeeStaysActiveUntilTerminationDatePlusOffset()
    {
        var engine = new MappingEngine();
        var record = new EmployeeRecord
        {
            EmployeeCode = "E102",
            WorkEmail = "leaving.soon@contoso.com",
            Status = EmploymentStatus.Terminated,
            TerminationDate = Today
        };

        var withinNoticePeriod = engine.BuildScimResource(record, [], Today, disableAccountDayOffset: 5);
        var afterNoticePeriod = engine.BuildScimResource(record, [], Today.AddDays(5), disableAccountDayOffset: 5);

        Assert.True(withinNoticePeriod.Active);
        Assert.False(afterNoticePeriod.Active);
    }
}
