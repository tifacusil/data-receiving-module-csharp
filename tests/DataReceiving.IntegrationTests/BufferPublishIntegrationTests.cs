using System.Text;
using System.Text.Json;
using DataReceiving.Buffer;
using DataReceiving.Configuration;
using DataReceiving.Messaging;
using DataReceiving.Parsing;
using DataReceiving.Sinks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using StackExchange.Redis;

namespace DataReceiving.IntegrationTests;

public class BufferPublishIntegrationTests : IAsyncLifetime
{
    private const string RedisConn = "localhost:6379";
    private const string RmqHost = "localhost";
    private const string RmqUser = "data_receiving";
    private const string RmqPass = "testpassword";
    private const string Exchange = "sensors.raw";
    private const string KeyPrefix = "sensor_buffer:";

    private IConnectionMultiplexer _redis = null!;
    private IDatabase _db = null!;
    private IConnection _rmqConn = null!;
    private IModel _rmqChannel = null!;
    private string _testQueue = null!;
    private IOptions<ReceiverOptions> _opts = null!;

    public async Task InitializeAsync()
    {
        _redis = await ConnectionMultiplexer.ConnectAsync(RedisConn);
        _db = _redis.GetDatabase();

        var factory = new ConnectionFactory
        {
            HostName = RmqHost,
            UserName = RmqUser,
            Password = RmqPass,
        };
        _rmqConn = factory.CreateConnection("integration-tests");
        _rmqChannel = _rmqConn.CreateModel();
        _rmqChannel.ExchangeDeclare(Exchange, ExchangeType.Topic, durable: true, autoDelete: false);

        var queueResult = _rmqChannel.QueueDeclare("", exclusive: false, durable: false, autoDelete: true);
        _testQueue = queueResult.QueueName;
        _rmqChannel.QueueBind(_testQueue, Exchange, "sensors.#");
        _rmqChannel.ConfirmSelect();

        _opts = Options.Create(new ReceiverOptions
        {
            Redis = new RedisOptions { ConnectionString = RedisConn, BufferTtlHours = 24, RetryMinAgeMinutes = 2 },
            RabbitMq = new RabbitMqOptions { Exchange = Exchange, ConfirmTimeoutSeconds = 5 },
        });

        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        try { _rmqChannel.QueueDelete(_testQueue, ifEmpty: false, ifUnused: false); } catch { }
        _rmqChannel?.Close(); _rmqChannel?.Dispose();
        _rmqConn?.Close(); _rmqConn?.Dispose();
        _redis?.Dispose();
        await Task.CompletedTask;
    }

    private (RabbitMqSink sink, RedisMessageBuffer buffer, RabbitMqPublisher publisher) CreateSink()
    {
        var buffer = new RedisMessageBuffer(_redis, _opts, NullLogger<RedisMessageBuffer>.Instance);
        var publisher = new RabbitMqPublisher(_rmqChannel, _opts, NullLogger<RabbitMqPublisher>.Instance);
        var sink = new RabbitMqSink(buffer, publisher, NullLogger<RabbitMqSink>.Instance);
        return (sink, buffer, publisher);
    }

    private static SensorEnvelope MakeGnssEnvelope(string project = "shangtan") => new()
    {
        MessageId = Guid.NewGuid().ToString(),
        ProjectId = project,
        SensorId = "GXZM-DB03",
        SensorType = "gnss",
        Timestamp = "2026-05-02T14:30:00Z",
        ReceivedAt = "2026-05-02T14:30:01Z",
        SamplingIntervalSeconds = 3600,
        Payload = new GnssPayload(12.34f, -5.67f, 2.10f, 13.57f, 1247.5f, "rtk_fixed"),
    };

    private static SensorEnvelope MakeRainfallEnvelope() => new()
    {
        MessageId = Guid.NewGuid().ToString(),
        ProjectId = "shangtan",
        SensorId = "GXZM-RG01",
        SensorType = "rainfall",
        Timestamp = "2026-05-02T14:00:00Z",
        ReceivedAt = "2026-05-02T14:00:01Z",
        SamplingIntervalSeconds = 3600,
        Payload = new RainfallPayload(2.4f, 1547.2f, 60),
    };

    [Fact]
    public async Task test_happy_path_published_and_removed_from_redis()
    {
        var (sink, _, _) = CreateSink();
        var envelope = MakeGnssEnvelope();

        await sink.WriteAsync(envelope, CancellationToken.None);

        var exists = await _db.KeyExistsAsync(KeyPrefix + envelope.MessageId);
        Assert.False(exists, "buffer key should be deleted after successful publish");

        await Task.Delay(200);
        var msg = _rmqChannel.BasicGet(_testQueue, autoAck: true);
        Assert.NotNull(msg);
        var body = JsonDocument.Parse(Encoding.UTF8.GetString(msg.Body.ToArray()));
        Assert.Equal(envelope.MessageId, body.RootElement.GetProperty("message_id").GetString());
    }

    [Fact]
    public async Task test_rabbitmq_down_message_stays_in_redis()
    {
        var buffer = new RedisMessageBuffer(_redis, _opts, NullLogger<RedisMessageBuffer>.Instance);
        var failingPublisher = new FailingPublisher();
        var sink = new RabbitMqSink(buffer, failingPublisher, NullLogger<RabbitMqSink>.Instance);
        var envelope = MakeRainfallEnvelope();

        var ex = await Record.ExceptionAsync(() => sink.WriteAsync(envelope, CancellationToken.None));
        Assert.Null(ex);

        var exists = await _db.KeyExistsAsync(KeyPrefix + envelope.MessageId);
        Assert.True(exists, "buffer key should remain when rabbitmq publish fails");

        await _db.KeyDeleteAsync(KeyPrefix + envelope.MessageId);
    }

    [Fact]
    public async Task test_retry_worker_recovers_buffered_messages()
    {
        var buffer = new RedisMessageBuffer(_redis, _opts, NullLogger<RedisMessageBuffer>.Instance);
        var failingPublisher = new FailingPublisher();
        var sinkFailing = new RabbitMqSink(buffer, failingPublisher, NullLogger<RabbitMqSink>.Instance);
        var envelope = MakeGnssEnvelope();

        await sinkFailing.WriteAsync(envelope, CancellationToken.None);
        Assert.True(await _db.KeyExistsAsync(KeyPrefix + envelope.MessageId));

        var publisher = new RabbitMqPublisher(_rmqChannel, _opts, NullLogger<RabbitMqPublisher>.Instance);
        var worker = new RetryWorker(buffer, publisher, _opts, NullLogger<RetryWorker>.Instance);

        // run retry pass with minAge=0 so the just-buffered message is retried
        await worker.RunRetryPassAsync(TimeSpan.Zero, CancellationToken.None);

        var exists = await _db.KeyExistsAsync(KeyPrefix + envelope.MessageId);
        Assert.False(exists, "buffer key should be removed after retry succeeds");

        await Task.Delay(200);
        var msg = _rmqChannel.BasicGet(_testQueue, autoAck: true);
        Assert.NotNull(msg);
    }

    [Fact]
    public async Task test_all_five_sensor_types_produce_correct_routing_keys()
    {
        var (sink, _, _) = CreateSink();
        var envelopes = new[]
        {
            new SensorEnvelope { MessageId = Guid.NewGuid().ToString(), ProjectId = "shangtan", SensorId = "S1", SensorType = "gnss",              Timestamp = "2026-05-02T14:30:00Z", ReceivedAt = "2026-05-02T14:30:01Z", SamplingIntervalSeconds = 3600, Payload = new GnssPayload(0, 0, 0, 0, 0, "no_fix") },
            new SensorEnvelope { MessageId = Guid.NewGuid().ToString(), ProjectId = "shangtan", SensorId = "S2", SensorType = "rainfall",           Timestamp = "2026-05-02T14:30:00Z", ReceivedAt = "2026-05-02T14:30:01Z", SamplingIntervalSeconds = 3600, Payload = new RainfallPayload(1, 100, 60) },
            new SensorEnvelope { MessageId = Guid.NewGuid().ToString(), ProjectId = "shangtan", SensorId = "S3", SensorType = "strain",             Timestamp = "2026-05-02T14:30:00Z", ReceivedAt = "2026-05-02T14:30:01Z", SamplingIntervalSeconds = 3600, Payload = new StrainPayload(100, null) },
            new SensorEnvelope { MessageId = Guid.NewGuid().ToString(), ProjectId = "shangtan", SensorId = "S4", SensorType = "soil_pressure",      Timestamp = "2026-05-02T14:30:00Z", ReceivedAt = "2026-05-02T14:30:01Z", SamplingIntervalSeconds = 3600, Payload = new SoilPressurePayload(30, null) },
            new SensorEnvelope { MessageId = Guid.NewGuid().ToString(), ProjectId = "lingzhan", SensorId = "S5", SensorType = "deep_displacement",  Timestamp = "2026-05-02T14:30:00Z", ReceivedAt = "2026-05-02T14:30:01Z", SamplingIntervalSeconds = 3600, Payload = new DeepDisplacementPayload(5, 1, -0.5f, 10) },
        };
        var expectedKeys = new[]
        {
            "sensors.shangtan.gnss",
            "sensors.shangtan.rainfall",
            "sensors.shangtan.strain",
            "sensors.shangtan.soil_pressure",
            "sensors.lingzhan.deep_displacement",
        };

        foreach (var env in envelopes)
            await sink.WriteAsync(env, CancellationToken.None);

        await Task.Delay(300);

        var receivedKeys = new List<string>();
        BasicGetResult? msg;
        while ((msg = _rmqChannel.BasicGet(_testQueue, autoAck: true)) is not null)
            receivedKeys.Add(msg.RoutingKey);

        foreach (var key in expectedKeys)
            Assert.Contains(key, receivedKeys);
    }

    [Fact]
    public async Task test_published_json_passes_schema_validation()
    {
        var (sink, _, _) = CreateSink();
        var envelope = MakeGnssEnvelope();

        await sink.WriteAsync(envelope, CancellationToken.None);

        await Task.Delay(200);
        var msg = _rmqChannel.BasicGet(_testQueue, autoAck: true);
        Assert.NotNull(msg);

        var json = Encoding.UTF8.GetString(msg.Body.ToArray());
        var validator = new Validation.SchemaValidator();
        var (valid, error) = validator.Validate(json, "gnss");
        Assert.True(valid, $"JSON in queue failed schema validation: {error}");
    }

    [Fact]
    public async Task test_buffer_count_visible_during_retry_pass()
    {
        var buffer = new RedisMessageBuffer(_redis, _opts, NullLogger<RedisMessageBuffer>.Instance);
        var failingPublisher = new FailingPublisher();
        var sink = new RabbitMqSink(buffer, failingPublisher, NullLogger<RabbitMqSink>.Instance);

        var ids = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var env = MakeGnssEnvelope();
            ids.Add(env.MessageId);
            await sink.WriteAsync(env, CancellationToken.None);
        }

        var count = await buffer.CountAsync(CancellationToken.None);
        Assert.True(count >= 3, $"expected at least 3, got {count}");

        foreach (var id in ids)
            await _db.KeyDeleteAsync(KeyPrefix + id);
    }

    private sealed class FailingPublisher : IMessagePublisher
    {
        public Task PublishAsync(SensorEnvelope envelope, CancellationToken ct) =>
            Task.FromException(new Exception("RabbitMQ unavailable (test)"));

        public Task PublishJsonAsync(string routingKey, string envelopeJson, string messageId, CancellationToken ct) =>
            Task.FromException(new Exception("RabbitMQ unavailable (test)"));
    }
}
