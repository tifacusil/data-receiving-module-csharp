using DataReceiving.Configuration;
using DataReceiving.Networking;
using DataReceiving.ServiceDiscovery;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DataReceiving.UnitTests.ServiceDiscovery;

public class HealthEndpointTests
{
    private static HealthEndpoint MakeEndpoint(bool isListening)
    {
        var status = new TcpServerStatus { IsListening = isListening };
        var opts = Options.Create(new ReceiverOptions());
        return new HealthEndpoint(status, opts, NullLogger<HealthEndpoint>.Instance);
    }

    [Fact]
    public void CheckHealth_WhenListening_Returns200()
    {
        var endpoint = MakeEndpoint(isListening: true);
        var (statusCode, _) = endpoint.CheckHealth();
        Assert.Equal(200, statusCode);
    }

    [Fact]
    public void CheckHealth_WhenListening_ReturnsOkBody()
    {
        var endpoint = MakeEndpoint(isListening: true);
        var (_, body) = endpoint.CheckHealth();
        Assert.Contains("\"ok\"", body);
    }

    [Fact]
    public void CheckHealth_WhenNotListening_Returns503()
    {
        var endpoint = MakeEndpoint(isListening: false);
        var (statusCode, _) = endpoint.CheckHealth();
        Assert.Equal(503, statusCode);
    }

    [Fact]
    public void CheckHealth_WhenNotListening_ReturnsStartingBody()
    {
        var endpoint = MakeEndpoint(isListening: false);
        var (_, body) = endpoint.CheckHealth();
        Assert.Contains("\"starting\"", body);
    }

    [Fact]
    public void CheckHealth_StatusTransitions_From503To200()
    {
        var status = new TcpServerStatus { IsListening = false };
        var opts = Options.Create(new ReceiverOptions());
        var endpoint = new HealthEndpoint(status, opts, NullLogger<HealthEndpoint>.Instance);

        var (before, _) = endpoint.CheckHealth();
        status.IsListening = true;
        var (after, _) = endpoint.CheckHealth();

        Assert.Equal(503, before);
        Assert.Equal(200, after);
    }

    [Fact]
    public void CheckHealth_BodyIsValidJson()
    {
        var endpoint = MakeEndpoint(isListening: true);
        var (_, body) = endpoint.CheckHealth();
        // basic JSON shape check — no exception thrown on parse
        var doc = System.Text.Json.JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("status", out _));
    }
}
