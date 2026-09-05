namespace PaycomEntraProvisioner.Paycom;

/// <summary>
/// Configuration for talking to Paycom's API. Confirmed against Paycom's
/// own "API Companion Guide" (2026): REST, three regional base URLs
/// (OKC/PHX/DFW - use whichever region hosts your organization's data),
/// HTTP Basic Authentication with the SID as username and the API token as
/// password, and pagination via pagesize/page (max pagesize 500, partial
/// results come back as HTTP 206). The exact field names an endpoint
/// returns still aren't published outside the tenant-specific "Paycom
/// Endpoint Guide" your representative provides, so
/// <see cref="CoreFieldAliases"/> and <see cref="StatusValueMap"/> stay
/// configurable rather than hard-coded. Everything Paycom returns still
/// lands in <c>EmployeeRecord.RawFields</c> verbatim regardless of these
/// aliases, so a field mapping can reference any Paycom field by its real
/// name even if it isn't aliased here.
/// </summary>
public sealed class PaycomClientOptions
{
    public const string SectionName = "Paycom";

    /// <summary>
    /// One of Paycom's three regional base URLs, e.g.
    /// "https://api.paycomonline.net/v4/rest/index.php/" (OKC),
    /// "https://api.phx.us-west.paycomonline.net/v4/rest/index.php/" (PHX), or
    /// "https://api.paycomdfw.net/v4/rest/index.php/" (DFW) - use whichever
    /// region your Paycom representative confirms hosts your data. Must end
    /// with a trailing slash so relative endpoint paths compose correctly.
    /// </summary>
    public required string BaseUrl { get; set; }

    public PaycomAuthMode AuthMode { get; set; } = PaycomAuthMode.ApiKey;

    /// <summary>SID used as the HTTP Basic Authentication username.</summary>
    public string? Sid { get; set; }

    /// <summary>API token used as the HTTP Basic Authentication password.</summary>
    public string? ApiToken { get; set; }

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? TokenEndpoint { get; set; }
    public string? Scope { get; set; }

    /// <summary>
    /// Relative path (no leading slash - it's appended to <see cref="BaseUrl"/>)
    /// to the endpoint that returns worker demographic data. Defaults to the
    /// Employee Directory endpoint ("api/v1/employeedirectory"), which Paycom's
    /// own how-to-connect example uses for "a list of all employees and their
    /// basic demographics". Swap for a custom report path if your tenant uses
    /// one instead.
    /// </summary>
    public string EmployeeReportPath { get; set; } = "api/v1/employeedirectory";

    /// <summary>
    /// Extra query string parameters appended to every request, e.g.
    /// { "eestatus", "A" } to request only active employees. Left empty by
    /// default so Terminated workers still come back with an explicit
    /// status (letting the sync disable them immediately) instead of
    /// simply disappearing from the feed and relying on the
    /// vanished-from-feed safety net a cycle late.
    /// </summary>
    public Dictionary<string, string> QueryParameters { get; set; } = [];

    /// <summary>
    /// Records requested per page. Paycom's documented maximum is 500;
    /// a response with more remaining records comes back as HTTP 206
    /// Partial Content, which this client pages through automatically.
    /// </summary>
    public int PageSize { get; set; } = 500;

    /// <summary>
    /// Property name under which the response array is nested. Every
    /// sample response in Paycom's API guide (employee change log, punch
    /// import, labor allocation) wraps its payload as
    /// <c>{ "result": true, "data": [...] }</c>, so "data" is the confirmed
    /// default - override if a specific endpoint differs.
    /// </summary>
    public string? ResponseArrayProperty { get; set; } = "data";

    /// <summary>
    /// Maps each <c>EmployeeRecord</c> property name to the field name
    /// Paycom actually returns for this tenant's report layout. Only
    /// "EmployeeCode" -> "eecode" is confirmed from Paycom's own API guide
    /// (it appears as the identifier in every sample response, including
    /// the employee change/audit log). Every other value below is an
    /// unconfirmed placeholder - replace with the real field names from
    /// your tenant's Paycom Endpoint Guide or a sample API response before
    /// going live. Note: that same guide's audit-log sample shows employee
    /// name coming back as a single combined "Last, First MI" string
    /// (<c>"eename": "BLACK, JACK A"</c>) rather than separate first/last
    /// fields - confirm whether the Employee Directory endpoint splits
    /// them before assuming FirstName/LastName map directly; if not,
    /// alias both to the same source field and use a
    /// <see cref="Configuration.FieldMapping.TransformExpression"/> to
    /// split it (e.g. <c>value.Split(", ")[0]</c>).
    /// </summary>
    public Dictionary<string, string> CoreFieldAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EmployeeCode"] = "eecode",
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
