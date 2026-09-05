using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaycomEntraProvisioner.Core.Abstractions;
using PaycomEntraProvisioner.Core.Domain;

namespace PaycomEntraProvisioner.Paycom;

public sealed class PaycomHttpClient(
    HttpClient httpClient,
    IOptionsMonitor<PaycomClientOptions> optionsMonitor,
    ILogger<PaycomHttpClient> logger) : IPaycomClient
{
    private OAuthToken? _cachedToken;

    public async Task<IReadOnlyList<EmployeeRecord>> GetAllEmployeesAsync(CancellationToken cancellationToken = default)
    {
        var options = optionsMonitor.CurrentValue;
        await ApplyAuthenticationAsync(options, cancellationToken);

        using var response = await httpClient.GetAsync(options.EmployeeReportPath, cancellationToken);
        response.EnsureSuccessStatusCode();

        var document = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        var array = ExtractEmployeeArray(document, options.ResponseArrayProperty);

        var records = new List<EmployeeRecord>(array.GetArrayLength());
        foreach (var item in array.EnumerateArray())
        {
            try
            {
                records.Add(ParseEmployee(item, options));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skipped an employee record that could not be parsed from the Paycom response.");
            }
        }

        logger.LogInformation("Retrieved {Count} employee records from Paycom.", records.Count);
        return records;
    }

    private static JsonElement ExtractEmployeeArray(JsonElement document, string? arrayProperty)
    {
        if (document.ValueKind == JsonValueKind.Array)
        {
            return document;
        }

        if (!string.IsNullOrEmpty(arrayProperty) && document.TryGetProperty(arrayProperty, out var nested))
        {
            return nested;
        }

        // Fall back to the first array-valued property in the payload,
        // since customers' Paycom report wrappers vary ("data", "results",
        // "Employees", ...).
        foreach (var property in document.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                return property.Value;
            }
        }

        throw new InvalidOperationException(
            "Could not locate an employee array in the Paycom response. Set PaycomClientOptions.ResponseArrayProperty " +
            "to the property name that wraps the employee list for your tenant's report.");
    }

    private static EmployeeRecord ParseEmployee(JsonElement item, PaycomClientOptions options)
    {
        var raw = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in item.EnumerateObject())
        {
            raw[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Null => null,
                JsonValueKind.True or JsonValueKind.False => property.Value.GetBoolean().ToString(),
                _ => property.Value.ToString()
            };
        }

        string? Alias(string propertyName) =>
            options.CoreFieldAliases.TryGetValue(propertyName, out var sourceField)
            && raw.TryGetValue(sourceField, out var value)
                ? value
                : null;

        var employeeCode = Alias("EmployeeCode")
            ?? throw new InvalidOperationException("Employee record is missing its employee code field.");

        var statusRaw = Alias("Status");
        var status = statusRaw is not null
            && options.StatusValueMap.TryGetValue(statusRaw, out var mappedStatus)
            && Enum.TryParse<EmploymentStatus>(mappedStatus, out var parsedStatus)
                ? parsedStatus
                : EmploymentStatus.Unknown;

        return new EmployeeRecord
        {
            EmployeeCode = employeeCode,
            FirstName = Alias("FirstName"),
            MiddleName = Alias("MiddleName"),
            LastName = Alias("LastName"),
            PreferredFirstName = Alias("PreferredFirstName"),
            Suffix = Alias("Suffix"),
            WorkEmail = Alias("WorkEmail"),
            PersonalEmail = Alias("PersonalEmail"),
            WorkPhone = Alias("WorkPhone"),
            MobilePhone = Alias("MobilePhone"),
            JobTitle = Alias("JobTitle"),
            Department = Alias("Department"),
            DepartmentCode = Alias("DepartmentCode"),
            Division = Alias("Division"),
            CostCenter = Alias("CostCenter"),
            WorkLocationCode = Alias("WorkLocationCode"),
            WorkLocationName = Alias("WorkLocationName"),
            EmployeeType = Alias("EmployeeType"),
            ManagerEmployeeCode = Alias("ManagerEmployeeCode"),
            StreetAddress = Alias("StreetAddress"),
            City = Alias("City"),
            State = Alias("State"),
            PostalCode = Alias("PostalCode"),
            Country = Alias("Country"),
            Status = status,
            HireDate = ParseDate(Alias("HireDate"), options.DateFormat),
            RehireDate = ParseDate(Alias("RehireDate"), options.DateFormat),
            TerminationDate = ParseDate(Alias("TerminationDate"), options.DateFormat),
            RawFields = raw
        };
    }

    private static DateOnly? ParseDate(string? value, string format)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParseExact(value, format, out var exact))
        {
            return exact;
        }

        return DateOnly.TryParse(value, out var parsed) ? parsed : null;
    }

    private async Task ApplyAuthenticationAsync(PaycomClientOptions options, CancellationToken cancellationToken)
    {
        if (options.AuthMode == PaycomAuthMode.ApiKey)
        {
            httpClient.DefaultRequestHeaders.Remove("APISID");
            httpClient.DefaultRequestHeaders.Remove("APIToken");
            httpClient.DefaultRequestHeaders.Add("APISID", options.Sid);
            httpClient.DefaultRequestHeaders.Add("APIToken", options.ApiToken);
            return;
        }

        var token = await GetOrRefreshOAuthTokenAsync(options, cancellationToken);
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task<string> GetOrRefreshOAuthTokenAsync(PaycomClientOptions options, CancellationToken cancellationToken)
    {
        if (_cachedToken is { } cached && cached.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return cached.AccessToken;
        }

        ArgumentException.ThrowIfNullOrEmpty(options.TokenEndpoint);
        using var request = new HttpRequestMessage(HttpMethod.Post, options.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = options.ClientId ?? "",
                ["client_secret"] = options.ClientSecret ?? "",
                ["scope"] = options.Scope ?? ""
            })
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<OAuthTokenResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Paycom token endpoint returned an empty response.");

        _cachedToken = new OAuthToken(payload.access_token, DateTimeOffset.UtcNow.AddSeconds(payload.expires_in));
        return _cachedToken.AccessToken;
    }

    private sealed record OAuthToken(string AccessToken, DateTimeOffset ExpiresAt);

    private sealed record OAuthTokenResponse(string access_token, int expires_in);
}
