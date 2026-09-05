using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace PaycomEntraProvisioner.Graph;

/// <summary>Builds the single <see cref="TokenCredential"/> shared by the Graph SDK client and the raw bulkUpload HTTP client.</summary>
public sealed class GraphCredentialFactory(IOptionsMonitor<EntraOptions> optionsMonitor)
{
    public TokenCredential Create()
    {
        var options = optionsMonitor.CurrentValue;

        if (options.UseManagedIdentity)
        {
            return new DefaultAzureCredential();
        }

        ArgumentException.ThrowIfNullOrEmpty(options.ClientSecret);
        return new ClientSecretCredential(options.TenantId, options.ClientId, options.ClientSecret);
    }
}
