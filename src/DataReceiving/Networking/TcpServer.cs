using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using DataReceiving.DeadLetter;
using DataReceiving.Observability;
using DataReceiving.Parsing;
using DataReceiving.Sinks;
using Microsoft.Extensions.Logging;

namespace DataReceiving.Networking;

public class TcpServer
{
    private readonly int _port;
    private readonly IDtuPacketParser _parser;
    private readonly ISink _sink;
    private readonly LocalDlxWriter _dlx;
    private readonly ILogger<TcpServer> _logger;
    private readonly PipelineMetrics? _metrics;

    private long _totalFrames;
    private long _gnssCount, _rainfallCount, _strainCount, _soilCount, _deepCount, _dlxCount;

    public TcpServer(int port, IDtuPacketParser parser, ISink sink, LocalDlxWriter dlx, ILogger<TcpServer> logger, PipelineMetrics? metrics = null)
    {
        _port = port;
        _parser = parser;
        _sink = sink;
        _dlx = dlx;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, _port);
        listener.Start();
        _logger.LogInformation("TCP server listening on port {Port}", _port);

        _ = Task.Run(() => SummaryLoop(ct), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => HandleClientAsync(client, ct), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting connection");
            }
        }
        listener.Stop();
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            _metrics?.ConnectionOpened();
            try
            {
            var stream = client.GetStream();
            while (!ct.IsCancellationRequested)
            {
                byte[]? rawFrame;
                try
                {
                    rawFrame = await FrameReader.ReadFrameAsync(stream, ct);
                    if (rawFrame is null) break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error reading frame from client");
                    break;
                }

                var startTs = Stopwatch.GetTimestamp();
                try
                {
                    var result = _parser.Parse(rawFrame);
                    Interlocked.Increment(ref _totalFrames);

                    if (result.Success && result.Envelope is not null)
                    {
                        CountSensorType(result.Envelope.SensorType);
                        _logger.LogDebug("frame parsed: {Type}/{SensorId} ({ProjectId}) msg={MsgId} size={Size}B",
                            result.Envelope.SensorType, result.Envelope.SensorId,
                            result.Envelope.ProjectId, result.Envelope.MessageId[..8], rawFrame.Length);

                        await _sink.WriteAsync(result.Envelope, ct);
                        _metrics?.IncPublished();
                        _metrics?.ObserveFrame("ok", result.Envelope.SensorType, Stopwatch.GetElapsedTime(startTs).TotalSeconds);
                        await SendAckAsync(stream, 0x00, 0x00, ct);
                    }
                    else if (result.Error is not null)
                    {
                        Interlocked.Increment(ref _dlxCount);
                        byte ackStatus = AckStatus(result.Error.Stage);
                        string hexPreview = Convert.ToHexString(rawFrame, 0, Math.Min(16, rawFrame.Length));
                        _logger.LogWarning("frame rejected: stage={Stage} error={Error} raw={Hex}",
                            result.Error.Stage, result.Error.Message, hexPreview);

                        await _dlx.WriteAsync(rawFrame, result.Error, ct);
                        _metrics?.IncDropped();
                        _metrics?.ObserveFrame("rejected", "unknown", Stopwatch.GetElapsedTime(startTs).TotalSeconds);
                        await SendAckAsync(stream, ackStatus, (byte)result.Error.Stage, ct);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error processing frame from client; closing connection");
                    break;
                }
            }
            }
            finally
            {
                _metrics?.ConnectionClosed();
            }
        }
    }

    private static async Task SendAckAsync(NetworkStream stream, byte status, byte code, CancellationToken ct)
    {
        byte[] ack = [0xAA, 0x55, status, code, 0xAA];
        await stream.WriteAsync(ack, ct);
    }

    private static byte AckStatus(ParseStage stage) => stage switch
    {
        ParseStage.Crc => 0x01,
        ParseStage.SensorType => 0x04,
        ParseStage.SchemaValidation => 0x03,
        _ => 0x02,
    };

    private void CountSensorType(string sensorType)
    {
        switch (sensorType)
        {
            case "gnss": Interlocked.Increment(ref _gnssCount); break;
            case "rainfall": Interlocked.Increment(ref _rainfallCount); break;
            case "strain": Interlocked.Increment(ref _strainCount); break;
            case "soil_pressure": Interlocked.Increment(ref _soilCount); break;
            case "deep_displacement": Interlocked.Increment(ref _deepCount); break;
        }
    }

    private async Task SummaryLoop(CancellationToken ct)
    {
        long lastTotal = 0;
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromMinutes(1), ct); }
            catch (OperationCanceledException) { break; }

            long total = Interlocked.Read(ref _totalFrames);
            if (total == lastTotal) continue;
            lastTotal = total;

            _logger.LogInformation(
                "summary: {Total} frames | gnss={Gnss} rainfall={Rain} strain={Strain} soil_pressure={Soil} deep_disp={Deep} | dlx={Dlx}",
                total, _gnssCount, _rainfallCount, _strainCount, _soilCount, _deepCount, _dlxCount);
        }
    }
}
