using Microsoft.Extensions.Hosting;

namespace DataReceiving.Networking;

public sealed class TcpServerHostedService : BackgroundService
{
    private readonly TcpServer _server;

    public TcpServerHostedService(TcpServer server) => _server = server;

    protected override Task ExecuteAsync(CancellationToken ct) => _server.RunAsync(ct);
}
