using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaycomEntraProvisioner.Core.Abstractions;
using PaycomEntraProvisioner.Core.Scim;

namespace PaycomEntraProvisioner.Graph;

/// <summary>
/// Submits SCIM bulk operations to the Entra ID API-driven inbound
/// provisioning job's bulkUpload endpoint and reads back provisioning
/// activity from the audit log. Uses a plain HttpClient (rather than the
/// Graph SDK's generated models) because bulkUpload's SCIM-shaped payload
/// is most reliably controlled with the hand-written models in
/// PaycomEntraProvisioner.Core.Scim - verify the exact endpoint/response
/// shape against Microsoft Learn for your tenant's API version before
/// go-live.
/// </summary>
public sealed class EntraProvisioningClient(
    HttpClient httpClient,
    GraphCredentialFactory credentialFactory,
    IOptionsMonitor<EntraOptions> optionsMonitor,
    ILogger<EntraProvisioningClient> logger) : IEntraProvisioningClient
{
    private static readonly string[] GraphScope = ["https://graph.microsoft.com/.default"];

    private readonly TokenCredential _credential = credentialFactory.Create();
    private readonly SemaphoreSlim _throttleGate = new(1, 1);
    private readonly Queue<DateTimeOffset> _recentCallTimestamps = new();

    public async Task<BulkUploadResult> SubmitBulkUploadAsync(
        IReadOnlyList<ScimBulkOperation> operations,
        CancellationToken cancellationToken = default)
    {
        var options = optionsMonitor.CurrentValue;
        var results = new List<ScimBulkOperationResult>();
        var batchErrors = new List<string>();
        var batches = 0;

        foreach (var chunk in Chunk(operations, options.BulkUploadBatchSize))
        {
            await ThrottleAsync(options, cancellationToken);
            batches++;

            var request = new ScimBulkRequest { Operations = [.. chunk] };
            var url = $"{options.GraphBaseUrl}/servicePrincipals/{options.ProvisioningServicePrincipalId}" +
                      $"/synchronization/jobs/{options.ProvisioningJobId}/bulkUpload";

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(request)
            };
            await AttachTokenAsync(httpRequest, cancellationToken);

            using var response = await httpClient.SendAsync(httpRequest, cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(options.ThrottleWindowSeconds);
                logger.LogWarning("Entra bulkUpload throttled; waiting {Delay}.", retryAfter);
                await Task.Delay(retryAfter, cancellationToken);
                await AttachTokenAsync(httpRequest, cancellationToken);
                using var retryResponse = await httpClient.SendAsync(httpRequest, cancellationToken);
                await ProcessResponseAsync(retryResponse, chunk, results, batchErrors, cancellationToken);
                continue;
            }

            await ProcessResponseAsync(response, chunk, results, batchErrors, cancellationToken);
        }

        return new BulkUploadResult(batches, results, batchErrors);
    }

    private async Task ProcessResponseAsync(
        HttpResponseMessage response,
        IReadOnlyList<ScimBulkOperation> chunk,
        List<ScimBulkOperationResult> results,
        List<string> batchErrors,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            batchErrors.Add($"bulkUpload batch of {chunk.Count} failed with {(int)response.StatusCode}: {Truncate(body)}");
            logger.LogError("Entra bulkUpload batch failed: {Status} {Body}", response.StatusCode, Truncate(body));
            return;
        }

        // 202 Accepted with no body is a documented success response; a
        // 200 with a bulk response body is treated the same way.
        if (response.Content.Headers.ContentLength is > 0)
        {
            var parsed = await response.Content.ReadFromJsonAsync<ScimBulkResponse>(cancellationToken: cancellationToken);
            if (parsed is not null)
            {
                results.AddRange(parsed.Operations);
                return;
            }
        }

        results.AddRange(chunk.Select(op => new ScimBulkOperationResult
        {
            BulkId = op.BulkId,
            Status = new ScimStatus { Code = "202" }
        }));
    }

    public async Task<IReadOnlyList<ProvisioningLogEntry>> GetRecentProvisioningLogAsync(
        int top = 100,
        CancellationToken cancellationToken = default)
    {
        var options = optionsMonitor.CurrentValue;
        var url = $"{options.GraphBaseUrl}/auditLogs/provisioning" +
                  $"?$top={top}&$orderby=activityDateTime desc" +
                  $"&$filter=serviceType eq 'API-driven'";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        await AttachTokenAsync(request, cancellationToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Could not read the provisioning audit log ({Status}); returning an empty log for this run.",
                response.StatusCode);
            return [];
        }

        var payload = await response.Content.ReadFromJsonAsync<ProvisioningLogResponse>(cancellationToken: cancellationToken);
        return payload?.Value.Select(v => new ProvisioningLogEntry(
            v.SourceIdentity?.Id,
            v.ProvisioningAction ?? "unknown",
            v.ProvisioningStatus?.Status ?? "unknown",
            v.ActivityDateTime,
            v.ProvisioningStatus?.ErrorInformation?.Message)).ToList() ?? [];
    }

    private async Task AttachTokenAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _credential.GetTokenAsync(new TokenRequestContext(GraphScope), cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
    }

    private async Task ThrottleAsync(EntraOptions options, CancellationToken cancellationToken)
    {
        await _throttleGate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var windowStart = now.AddSeconds(-options.ThrottleWindowSeconds);

            while (_recentCallTimestamps.Count > 0 && _recentCallTimestamps.Peek() < windowStart)
            {
                _recentCallTimestamps.Dequeue();
            }

            if (_recentCallTimestamps.Count >= options.MaxCallsPerWindow)
            {
                var waitFor = _recentCallTimestamps.Peek().AddSeconds(options.ThrottleWindowSeconds) - now;
                if (waitFor > TimeSpan.Zero)
                {
                    await Task.Delay(waitFor, cancellationToken);
                }
            }

            _recentCallTimestamps.Enqueue(DateTimeOffset.UtcNow);
        }
        finally
        {
            _throttleGate.Release();
        }
    }

    private static IEnumerable<List<T>> Chunk<T>(IReadOnlyList<T> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
        {
            yield return [.. source.Skip(i).Take(size)];
        }
    }

    private static string Truncate(string value, int maxLength = 500) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";

    private sealed record ProvisioningLogResponse(
        [property: JsonPropertyName("value")] List<ProvisioningLogValue> Value);

    private sealed record ProvisioningLogValue(
        [property: JsonPropertyName("sourceIdentity")] ProvisioningLogIdentity? SourceIdentity,
        [property: JsonPropertyName("activityDateTime")] DateTimeOffset ActivityDateTime,
        [property: JsonPropertyName("provisioningAction")] string? ProvisioningAction,
        [property: JsonPropertyName("provisioningStatus")] ProvisioningLogStatus? ProvisioningStatus);

    private sealed record ProvisioningLogIdentity(
        [property: JsonPropertyName("id")] string? Id);

    private sealed record ProvisioningLogStatus(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("errorInformation")] ProvisioningLogError? ErrorInformation);

    private sealed record ProvisioningLogError(
        [property: JsonPropertyName("message")] string? Message);
}
