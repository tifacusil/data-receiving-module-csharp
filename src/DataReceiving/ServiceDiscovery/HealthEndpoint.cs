using System.Net;
using System.Text;
using DataReceiving.Configuration;
using DataReceiving.Networking;
using DataReceiving.Observability;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataReceiving.ServiceDiscovery;

public sealed class HealthEndpoint : BackgroundService
{
    private readonly TcpServerStatus _tcpStatus;
    private readonly int _port;
    private readonly ILogger<HealthEndpoint> _logger;
    private readonly PipelineMetrics? _metrics;

    public HealthEndpoint(TcpServerStatus tcpStatus, IOptions<ReceiverOptions> opts, ILogger<HealthEndpoint> logger, PipelineMetrics? metrics = null)
    {
        _tcpStatus = tcpStatus;
        _port = opts.Value.Consul.HealthPort;
        _logger = logger;
        _metrics = metrics;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var listener = new HttpListener();
        // Prefijo raíz: enrutamos por path (/health, /metrics) como hace el mux de Go.
        listener.Prefixes.Add($"http://+:{_port}/");
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

                var path = ctx.Request.Url?.AbsolutePath ?? "/";
                if (path.StartsWith("/metrics", StringComparison.Ordinal))
                {
                    await HandleMetricsAsync(ctx, ct);
                }
                else
                {
                    var (status, body) = CheckHealth();
                    var bytes = Encoding.UTF8.GetBytes(body);
                    ctx.Response.StatusCode = status;
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.ContentLength64 = bytes.Length;
                    await ctx.Response.OutputStream.WriteAsync(bytes, ct);
                    ctx.Response.Close();
                }
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleMetricsAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        if (_metrics is null)
        {
            ctx.Response.StatusCode = 503;
            ctx.Response.Close();
            return;
        }

        // Formato de texto Prometheus 0.0.4.
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = "text/plain; version=0.0.4; charset=utf-8";
        await _metrics.WriteTo(ctx.Response.OutputStream, ct);
        ctx.Response.Close();
    }

    internal (int StatusCode, string Body) CheckHealth()
    {
        if (_tcpStatus.IsListening)
            return (200, """{"status":"ok"}""");

        return (503, """{"status":"starting"}""");
    }
}
