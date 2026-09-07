using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using IdentityFlow.Core.Abstractions;

namespace IdentityFlow.Paycom;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPaycomIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PaycomClientOptions>()
            .Bind(configuration.GetSection(PaycomClientOptions.SectionName));

        services.AddHttpClient<IPaycomClient, PaycomHttpClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<PaycomClientOptions>>().CurrentValue;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        });

        return services;
    }
}
