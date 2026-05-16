using Consul;
using DataReceiving.Configuration;
using DataReceiving.ServiceDiscovery;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DataReceiving.IntegrationTests;

[Trait("Category", "Integration")]
public class ConsulIntegrationTests : IAsyncLifetime
{
    private const string ConsulHost = "http://localhost:8500";
    private ConsulClient _consul = null!;
    private string _serviceId = null!;

    public async Task InitializeAsync()
    {
        _consul = new ConsulClient(cfg => cfg.Address = new Uri(ConsulHost));
        _serviceId = $"data-receiving-test-{Guid.NewGuid():N}";

        // skip if Consul unreachable
        try { await _consul.Agent.Self(); }
        catch { throw new SkipException("Consul not available"); }
    }

    public async Task DisposeAsync()
    {
        try { await _consul.Agent.ServiceDeregister(_serviceId); }
        catch { /* best-effort cleanup */ }
        _consul.Dispose();
    }

    private ConsulRegistration MakeRegistration(string instanceId)
    {
        var opts = Options.Create(new ReceiverOptions
        {
            Consul = new ConsulOptions
            {
                Address = ConsulHost,
                InstanceId = instanceId,
                HealthPort = 8080,
                Environment = "test",
                ServiceVersion = "0.0.0",
            }
        });
        return new ConsulRegistration(_consul, opts, NullLogger<ConsulRegistration>.Instance);
    }

    [Fact]
    public async Task StartAsync_ServiceAppearsInCatalog()
    {
        var reg = MakeRegistration(_serviceId);
        await reg.StartAsync(CancellationToken.None);

        var services = await _consul.Agent.Services();
        Assert.Contains(services.Response.Keys, k => k.Contains(_serviceId));

        await reg.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_ServiceRemovedFromCatalog()
    {
        var reg = MakeRegistration(_serviceId);
        await reg.StartAsync(CancellationToken.None);
        await reg.StopAsync(CancellationToken.None);

        var services = await _consul.Agent.Services();
        Assert.DoesNotContain(services.Response.Keys, k => k.Contains(_serviceId));
    }

    [Fact]
    public async Task StartAsync_ServiceHasCorrectTags()
    {
        var reg = MakeRegistration(_serviceId);
        await reg.StartAsync(CancellationToken.None);

        var services = await _consul.Agent.Services();
        var entry = services.Response.Values.FirstOrDefault(s => s.ID.Contains(_serviceId));
        Assert.NotNull(entry);
        Assert.Contains("test", entry!.Tags);
        Assert.Contains("version:0.0.0", entry.Tags);

        await reg.StopAsync(CancellationToken.None);
    }
}

internal class SkipException : Exception
{
    public SkipException(string message) : base(message) { }
}
