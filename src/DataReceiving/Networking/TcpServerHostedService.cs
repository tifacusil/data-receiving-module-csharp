using Microsoft.Extensions.Hosting;

namespace DataReceiving.Networking;

public sealed class TcpServerHostedService : BackgroundService
{
    private readonly TcpServer _server;
    private readonly TcpServerStatus _status;

    public TcpServerHostedService(TcpServer server, TcpServerStatus status)
    {
        _server = server;
        _status = status;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _status.IsListening = true;
        try
        {
            await _server.RunAsync(ct);
        }
        finally
        {
            _status.IsListening = false;
        }
    }
}
