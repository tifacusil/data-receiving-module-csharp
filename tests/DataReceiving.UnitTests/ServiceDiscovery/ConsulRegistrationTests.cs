using Consul;
using DataReceiving.Configuration;
using DataReceiving.ServiceDiscovery;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DataReceiving.UnitTests.ServiceDiscovery;

public class ConsulRegistrationTests
{
    private static (ConsulRegistration Service, IConsulClient Client, IAgentEndpoint Agent) Make(
        string instanceId = "data-receiving-01",
        int healthPort = 8080,
        string environment = "prod",
        string version = "1.0.0")
    {
        var client = Substitute.For<IConsulClient>();
        var agent = Substitute.For<IAgentEndpoint>();
        client.Agent.Returns(agent);
        agent.ServiceRegister(Arg.Any<AgentServiceRegistration>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(new WriteResult()));
        agent.ServiceDeregister(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult(new WriteResult()));

        var opts = Options.Create(new ReceiverOptions
        {
            Consul = new ConsulOptions
            {
                InstanceId = instanceId,
                HealthPort = healthPort,
                Environment = environment,
                ServiceVersion = version,
            }
        });

        var svc = new ConsulRegistration(client, opts, NullLogger<ConsulRegistration>.Instance);
        return (svc, client, agent);
    }

    [Fact]
    public async Task StartAsync_CallsServiceRegister()
    {
        var (svc, _, agent) = Make();
        await svc.StartAsync(CancellationToken.None);
        await agent.Received(1).ServiceRegister(Arg.Any<AgentServiceRegistration>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_RegistersWithCorrectServiceName()
    {
        var (svc, _, agent) = Make();
        await svc.StartAsync(CancellationToken.None);
        await agent.Received(1).ServiceRegister(
            Arg.Is<AgentServiceRegistration>(r => r.Name == "data-receiving"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_ServiceIdContainsInstanceId()
    {
        var (svc, _, agent) = Make(instanceId: "node-02");
        await svc.StartAsync(CancellationToken.None);
        await agent.Received(1).ServiceRegister(
            Arg.Is<AgentServiceRegistration>(r => r.ID.Contains("node-02")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_HealthCheckTargetsCorrectPort()
    {
        var (svc, _, agent) = Make(healthPort: 9090);
        await svc.StartAsync(CancellationToken.None);
        await agent.Received(1).ServiceRegister(
            Arg.Is<AgentServiceRegistration>(r => r.Check != null && r.Check.HTTP!.Contains("9090")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_TagsContainEnvironmentAndVersion()
    {
        var (svc, _, agent) = Make(environment: "staging", version: "2.1.0");
        await svc.StartAsync(CancellationToken.None);
        await agent.Received(1).ServiceRegister(
            Arg.Is<AgentServiceRegistration>(r =>
                r.Tags.Contains("staging") && r.Tags.Contains("version:2.1.0")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_CallsServiceDeregister()
    {
        var (svc, _, agent) = Make();
        await svc.StartAsync(CancellationToken.None);
        await svc.StopAsync(CancellationToken.None);
        await agent.Received(1).ServiceDeregister(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_DeregistersCorrectServiceId()
    {
        var (svc, _, agent) = Make(instanceId: "node-03");
        await svc.StartAsync(CancellationToken.None);
        await svc.StopAsync(CancellationToken.None);
        await agent.Received(1).ServiceDeregister(
            Arg.Is<string>(id => id.Contains("node-03")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_WithoutStart_DoesNotCallDeregister()
    {
        var (svc, _, agent) = Make();
        await svc.StopAsync(CancellationToken.None);
        await agent.DidNotReceive().ServiceDeregister(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
