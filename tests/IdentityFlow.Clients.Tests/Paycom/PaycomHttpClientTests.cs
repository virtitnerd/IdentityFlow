using System.Net;
using System.Text;
using IdentityFlow.Clients.Paycom;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace IdentityFlow.Clients.Tests.Paycom;

public class PaycomHttpClientTests
{
    private static PaycomClientOptions BaseOptions() => new()
    {
        BaseUrl = "https://api.paycomonline.net/v4/rest/index.php/",
        AuthMode = PaycomAuthMode.ApiKey,
        Sid = "mysid",
        ApiToken = "mytoken",
        EmployeeReportPath = "api/v1/employeedirectory",
        PageSize = 500
    };

    private static (PaycomHttpClient Client, FakeHttpMessageHandler Handler) CreateClient(
        PaycomClientOptions options,
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new FakeHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(options.BaseUrl) };
        var client = new PaycomHttpClient(httpClient, new StaticOptionsMonitor<PaycomClientOptions>(options), NullLogger<PaycomHttpClient>.Instance);
        return (client, handler);
    }

    [Fact]
    public async Task GetAllEmployeesAsync_SendsHttpBasicAuthWithSidAndToken()
    {
        var options = BaseOptions();
        var (client, handler) = CreateClient(options, _ => JsonResponse("""{"result":true,"data":[{"eecode":"E1","eename":"DOE, JOHN"}]}"""));

        await client.GetAllEmployeesAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
        var expectedCredentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("mysid:mytoken"));
        Assert.Equal(expectedCredentials, request.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task GetAllEmployeesAsync_PreservesBasePathWhenComposingRequestUri()
    {
        var options = BaseOptions();
        var (client, handler) = CreateClient(options, _ => JsonResponse("""{"result":true,"data":[]}"""));

        await client.GetAllEmployeesAsync();

        var request = Assert.Single(handler.Requests);
        // Regression guard: a leading "/" on EmployeeReportPath combined with
        // HttpClient.BaseAddress replaces the whole base path instead of
        // appending, silently dropping "/v4/rest/index.php/".
        Assert.StartsWith("https://api.paycomonline.net/v4/rest/index.php/api/v1/employeedirectory", request.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetAllEmployeesAsync_IncludesPagingAndCustomQueryParameters()
    {
        var options = BaseOptions();
        options.QueryParameters["eestatus"] = "A";
        var (client, handler) = CreateClient(options, _ => JsonResponse("""{"result":true,"data":[]}"""));

        await client.GetAllEmployeesAsync();

        var query = handler.Requests[0].RequestUri!.Query;
        Assert.Contains("pagesize=500", query);
        Assert.Contains("page=1", query);
        Assert.Contains("eestatus=A", query);
    }

    [Fact]
    public async Task GetAllEmployeesAsync_PaginatesUntilAPartialPageIsReturned()
    {
        var options = BaseOptions();
        options.PageSize = 2;

        var (client, handler) = CreateClient(options, request =>
        {
            var page = request.RequestUri!.Query.Contains("page=1") ? 1 : 2;
            return page == 1
                ? JsonResponse("""{"result":true,"data":[{"eecode":"E1"},{"eecode":"E2"}]}""", HttpStatusCode.PartialContent)
                : JsonResponse("""{"result":true,"data":[{"eecode":"E3"}]}""");
        });

        var employees = await client.GetAllEmployeesAsync();

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(3, employees.Count);
        Assert.Equal(["E1", "E2", "E3"], employees.Select(e => e.EmployeeCode));
    }

    [Fact]
    public async Task GetAllEmployeesAsync_MapsConfirmedEecodeAlias()
    {
        var options = BaseOptions();
        var (client, _) = CreateClient(options, _ => JsonResponse("""{"result":true,"data":[{"eecode":"A002","eename":"BLACK, JACK A"}]}"""));

        var employees = await client.GetAllEmployeesAsync();

        Assert.Equal("A002", Assert.Single(employees).EmployeeCode);
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responder(request));
        }
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;
        public T Get(string? name) => CurrentValue;
        public IDisposable OnChange(Action<T, string?> listener) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }
}
