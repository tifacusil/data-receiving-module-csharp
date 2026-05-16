using System.Net;
using DataReceiving.Configuration;
using DataReceiving.Networking;
using DataReceiving.ServiceDiscovery;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DataReceiving.IntegrationTests;

// Starts the HttpListener once; all three tests share it.
public sealed class HealthEndpointFixture : IAsyncLifetime
{
    internal const int Port = 18080;
    internal TcpServerStatus Status { get; } = new();
    private readonly HealthEndpoint _endpoint;
    private readonly CancellationTokenSource _cts = new();

    public HealthEndpointFixture()
    {
        var opts = Options.Create(new ReceiverOptions
        {
            Consul = new ConsulOptions { HealthPort = Port }
        });
        _endpoint = new HealthEndpoint(Status, opts, NullLogger<HealthEndpoint>.Instance);
    }

    public async Task InitializeAsync()
    {
        await _endpoint.StartAsync(_cts.Token);

        using var http = new HttpClient();
        for (int i = 0; i < 30; i++)
        {
            try { await http.GetAsync($"http://localhost:{Port}/health/"); return; }
            catch { await Task.Delay(100); }
        }
        throw new Exception($"HealthEndpoint did not respond on port {Port} after 3s");
    }

    public async Task DisposeAsync()
    {
        _cts.Cancel();
        await _endpoint.StopAsync(CancellationToken.None);
        _cts.Dispose();
    }
}

public class HealthEndpointHttpTests : IClassFixture<HealthEndpointFixture>
{
    private readonly HealthEndpointFixture _fx;
    private readonly HttpClient _http = new();

    public HealthEndpointHttpTests(HealthEndpointFixture fx) => _fx = fx;

    [Fact]
    public async Task Returns503_WhenTcpServerNotYetListening()
    {
        _fx.Status.IsListening = false;

        var response = await _http.GetAsync($"http://localhost:{HealthEndpointFixture.Port}/health/");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("starting", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Returns200_WhenTcpServerIsListening()
    {
        _fx.Status.IsListening = true;

        var response = await _http.GetAsync($"http://localhost:{HealthEndpointFixture.Port}/health/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task StatusTransitions_From503To200_LiveOverHttp()
    {
        _fx.Status.IsListening = false;
        var before = await _http.GetAsync($"http://localhost:{HealthEndpointFixture.Port}/health/");

        _fx.Status.IsListening = true;
        var after = await _http.GetAsync($"http://localhost:{HealthEndpointFixture.Port}/health/");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, before.StatusCode);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }
}
