using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph;
using IdentityFlow.Core.Abstractions;

namespace IdentityFlow.Graph;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEntraIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EntraOptions>()
            .Bind(configuration.GetSection(EntraOptions.SectionName));

        services.AddSingleton<GraphCredentialFactory>();

        services.AddSingleton(sp =>
        {
            var credential = sp.GetRequiredService<GraphCredentialFactory>().Create();
            return new GraphServiceClient(credential, ["https://graph.microsoft.com/.default"]);
        });

        services.AddHttpClient<IEntraProvisioningClient, EntraProvisioningClient>();
        services.AddScoped<IEntraDirectoryClient, EntraDirectoryClient>();

        return services;
    }
}
