using Consul;
using DataReceiving.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataReceiving.ServiceDiscovery;

public sealed class ConsulRegistration : IHostedService
{
    private readonly IConsulClient _consul;
    private readonly ConsulOptions _opts;
    private readonly ILogger<ConsulRegistration> _logger;
    private string? _serviceId;

    public ConsulRegistration(IConsulClient consul, IOptions<ReceiverOptions> opts, ILogger<ConsulRegistration> logger)
    {
        _consul = consul;
        _opts = opts.Value.Consul;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _serviceId = $"data-receiving-{_opts.InstanceId}";

        var registration = new AgentServiceRegistration
        {
            ID = _serviceId,
            Name = "data-receiving",
            Tags = new[] { _opts.Environment, $"version:{_opts.ServiceVersion}", $"instance:{_opts.InstanceId}" },
            Port = _opts.HealthPort,
            Check = new AgentServiceCheck
            {
                HTTP = $"http://localhost:{_opts.HealthPort}/health/",
                Interval = TimeSpan.FromSeconds(10),
                Timeout = TimeSpan.FromSeconds(5),
                DeregisterCriticalServiceAfter = TimeSpan.FromMinutes(1),
            }
        };

        try
        {
            await _consul.Agent.ServiceRegister(registration, ct);
            _logger.LogInformation("Registered service {ServiceId} with Consul", _serviceId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to register {ServiceId} with Consul; continuing without service discovery", _serviceId);
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_serviceId is null) return;
        try
        {
            await _consul.Agent.ServiceDeregister(_serviceId, ct);
            _logger.LogInformation("Deregistered service {ServiceId} from Consul", _serviceId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to deregister {ServiceId} from Consul", _serviceId);
        }
    }
}
