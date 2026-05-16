using System.Net;
using System.Text;
using DataReceiving.Configuration;
using DataReceiving.Networking;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataReceiving.ServiceDiscovery;

public sealed class HealthEndpoint : BackgroundService
{
    private readonly TcpServerStatus _tcpStatus;
    private readonly int _port;
    private readonly ILogger<HealthEndpoint> _logger;

    public HealthEndpoint(TcpServerStatus tcpStatus, IOptions<ReceiverOptions> opts, ILogger<HealthEndpoint> logger)
    {
        _tcpStatus = tcpStatus;
        _port = opts.Value.Consul.HealthPort;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://+:{_port}/health/");
        listener.Start();
        _logger.LogInformation("Health endpoint listening on port {Port}", _port);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext? ctx = null;
                try
                {
                    ctx = await listener.GetContextAsync().WaitAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var (status, body) = CheckHealth();
                var bytes = Encoding.UTF8.GetBytes(body);
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes, ct);
                ctx.Response.Close();
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    internal (int StatusCode, string Body) CheckHealth()
    {
        if (_tcpStatus.IsListening)
            return (200, """{"status":"ok"}""");

        return (503, """{"status":"starting"}""");
    }
}
