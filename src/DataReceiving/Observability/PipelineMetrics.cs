using System.Diagnostics;
using Prometheus;

namespace DataReceiving.Observability;

/// <summary>
/// Colectores Prometheus del pipeline de ingesta. Espejo 1:1 del layer
/// observability del rewrite en Go (internal/observability/metrics.go): mismos
/// nombres, labels y buckets, para que la comparación de rendimiento Go-vs-C#
/// mida lo mismo. Usa el registro por defecto de prometheus-net, que ya expone
/// process_resident_memory_bytes y process_cpu_seconds_total (equivalente al
/// ProcessCollector de Go).
/// </summary>
public sealed class PipelineMetrics
{
    private readonly CollectorRegistry _registry;
    private readonly Counter _framesTotal;
    private readonly Histogram _frameLatency;
    private readonly Counter _published;
    private readonly Counter _dropped;
    private readonly Counter _connectionsTotal;
    private readonly Gauge _connectionsActive;

    public PipelineMetrics()
        : this(Metrics.DefaultRegistry)
    {
    }

    public PipelineMetrics(CollectorRegistry registry)
    {
        _registry = registry;
        var factory = Metrics.WithCustomRegistry(registry);

        _framesTotal = factory.CreateCounter(
            "data_receiving_frames_total",
            "Total DTU frames processed, by result (ok|rejected) and sensor_type.",
            new CounterConfiguration { LabelNames = new[] { "result", "sensor_type" } });

        _frameLatency = factory.CreateHistogram(
            "data_receiving_frame_processing_seconds",
            "Wall-clock time to parse and sink a frame.",
            new HistogramConfiguration
            {
                // Sub-milisegundo a ~1s; suficiente para derivar p50/p90/p95/p99.
                Buckets = new[]
                {
                    0.0001, 0.00025, 0.0005, 0.001, 0.0025, 0.005, 0.01,
                    0.025, 0.05, 0.1, 0.25, 0.5, 1,
                },
            });

        _published = factory.CreateCounter(
            "data_receiving_messages_published_total",
            "Envelopes confirmed published to RabbitMQ.");

        _dropped = factory.CreateCounter(
            "data_receiving_messages_dropped_total",
            "Frames rejected and written to the dead-letter directory.");

        _connectionsTotal = factory.CreateCounter(
            "data_receiving_tcp_connections_total",
            "Total TCP connections accepted by the DTU listener.");

        _connectionsActive = factory.CreateGauge(
            "data_receiving_tcp_connections_active",
            "TCP connections (streams) currently open on the DTU listener.");

        // prometheus-net expone process_working_set_bytes pero NO
        // process_resident_memory_bytes (el nombre que usa el ProcessCollector de
        // Go). Publicamos un alias con el nombre de Go, con la misma fuente que
        // usa .NET para el working set (WorkingSet64 == RSS en Linux, /proc), para
        // que el benchmark Go-vs-C# consulte una sola métrica en ambos servicios.
        var residentMemory = factory.CreateGauge(
            "process_resident_memory_bytes",
            "Resident memory size in bytes (alias de process_working_set_bytes, nombre del ProcessCollector de Go).");
        registry.AddBeforeCollectCallback(() =>
            residentMemory.Set(Process.GetCurrentProcess().WorkingSet64));
    }

    /// <summary>Registra el resultado y la latencia de un frame.</summary>
    public void ObserveFrame(string result, string sensorType, double seconds)
    {
        _framesTotal.WithLabels(result, sensorType).Inc();
        _frameLatency.Observe(seconds);
    }

    /// <summary>Incrementa el contador de publicaciones confirmadas.</summary>
    public void IncPublished() => _published.Inc();

    /// <summary>Incrementa el contador de frames descartados (dead-letter).</summary>
    public void IncDropped() => _dropped.Inc();

    /// <summary>Marca una conexión TCP abierta: +1 total aceptadas, +1 activas.</summary>
    public void ConnectionOpened()
    {
        _connectionsTotal.Inc();
        _connectionsActive.Inc();
    }

    /// <summary>Marca una conexión TCP cerrada: -1 activas.</summary>
    public void ConnectionClosed() => _connectionsActive.Dec();

    /// <summary>Serializa el registro en formato de texto Prometheus 0.0.4.</summary>
    public Task WriteTo(Stream target, CancellationToken ct) =>
        _registry.CollectAndExportAsTextAsync(target, ct);
}
