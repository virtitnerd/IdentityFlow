namespace PaycomEntraProvisioner.Paycom;

/// <summary>
/// Configuration for talking to Paycom's API. Paycom does not publish a
/// single public API contract - access, base URL, the exact report
/// endpoint, and the field names it returns are all negotiated per
/// customer with a Paycom representative. Populate
/// <see cref="CoreFieldAliases"/> and <see cref="StatusValueMap"/> to match
/// whatever your tenant's configured export actually contains; everything
/// returned by Paycom still lands in <c>EmployeeRecord.RawFields</c>
/// verbatim so it stays available to field mappings even if a customer's
/// export includes attributes this client doesn't know about ahead of time.
/// </summary>
public sealed class PaycomClientOptions
{
    public const string SectionName = "Paycom";

    public required string BaseUrl { get; set; }

    public PaycomAuthMode AuthMode { get; set; } = PaycomAuthMode.ApiKey;

    /// <summary>SID for API-key auth (APISID header).</summary>
    public string? Sid { get; set; }

    /// <summary>API token for API-key auth (APIToken header).</summary>
    public string? ApiToken { get; set; }

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? TokenEndpoint { get; set; }
    public string? Scope { get; set; }

    /// <summary>
    /// Relative path to the employee master/report endpoint your Paycom
    /// representative provisions for this integration.
    /// </summary>
    public string EmployeeReportPath { get; set; } = "/api/v1/reports/employee-master";

    /// <summary>
    /// Optional JSON pointer-style property name under which the response
    /// array is nested (e.g. "data", "Employees"). Leave null if the
    /// response is a bare JSON array.
    /// </summary>
    public string? ResponseArrayProperty { get; set; }

    /// <summary>
    /// Maps each <c>EmployeeRecord</c> property name to the field name
    /// Paycom actually returns for this tenant's report layout.
    /// </summary>
    public Dictionary<string, string> CoreFieldAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EmployeeCode"] = "Employee_Code",
        ["FirstName"] = "First_Name",
        ["MiddleName"] = "Middle_Name",
        ["LastName"] = "Last_Name",
        ["PreferredFirstName"] = "Preferred_Name",
        ["Suffix"] = "Suffix",
        ["WorkEmail"] = "Email_Work",
        ["PersonalEmail"] = "Email_Personal",
        ["WorkPhone"] = "Phone_Work",
        ["MobilePhone"] = "Phone_Mobile",
        ["JobTitle"] = "Position_Title",
        ["Department"] = "Department_Description",
        ["DepartmentCode"] = "Department_Code",
        ["Division"] = "Division_Description",
        ["CostCenter"] = "Cost_Center",
        ["WorkLocationCode"] = "Location_Code",
        ["WorkLocationName"] = "Location_Description",
        ["EmployeeType"] = "Employee_Type",
        ["ManagerEmployeeCode"] = "Supervisor_Employee_Code",
        ["StreetAddress"] = "Address_Line_1",
        ["City"] = "City",
        ["State"] = "State",
        ["PostalCode"] = "Zip_Code",
        ["Country"] = "Country",
        ["Status"] = "Employee_Status",
        ["HireDate"] = "Hire_Date",
        ["RehireDate"] = "Rehire_Date",
        ["TerminationDate"] = "Termination_Date"
    };

    /// <summary>
    /// Maps Paycom's raw status code/text (left) to our normalized
    /// <see cref="Core.Domain.EmploymentStatus"/> name (right). Adjust to
    /// match what your Paycom report actually emits (e.g. "A"/"T"/"L" or
    /// "Active"/"Terminated"/"Leave of Absence").
    /// </summary>
    public Dictionary<string, string> StatusValueMap { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = "Active",
        ["Active"] = "Active",
        ["L"] = "OnLeave",
        ["Leave"] = "OnLeave",
        ["Leave of Absence"] = "OnLeave",
        ["T"] = "Terminated",
        ["Terminated"] = "Terminated",
        ["P"] = "PreHire",
        ["Pending"] = "PreHire",
        ["Pre-Hire"] = "PreHire"
    };

    public string DateFormat { get; set; } = "yyyy-MM-dd";
}
