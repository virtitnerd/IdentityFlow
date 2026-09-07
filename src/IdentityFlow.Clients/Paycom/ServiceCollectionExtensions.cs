using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using IdentityFlow.Core.Abstractions;

namespace IdentityFlow.Clients.Paycom;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Paycom as this deployment's configured <see cref="IHrClient"/>.
    /// Paycom is the only HR system this solution supports today - adding a
    /// second one means adding a parallel <c>Add&lt;X&gt;Integration</c>
    /// method under <c>IdentityFlow.Clients.&lt;X&gt;</c> and calling that
    /// instead (or as well, with an <see cref="IHrClient"/> per configured
    /// tenant) from Program.cs - Core and everything downstream of
    /// <see cref="IHrClient"/> stays unchanged either way.
    /// </summary>
    public static IServiceCollection AddPaycomIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PaycomClientOptions>()
            .Bind(configuration.GetSection(PaycomClientOptions.SectionName));

        services.AddHttpClient<IHrClient, PaycomHttpClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<PaycomClientOptions>>().CurrentValue;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        });

        return services;
    }
}
