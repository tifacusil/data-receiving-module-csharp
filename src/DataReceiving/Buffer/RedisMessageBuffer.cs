using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using DataReceiving.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace DataReceiving.Buffer;

public sealed class RedisMessageBuffer : IMessageBuffer
{
    private const string KeyPrefix = "sensor_buffer:";

    private readonly IDatabase _db;
    private readonly IServer _server;
    private readonly TimeSpan _ttl;
    private readonly ILogger<RedisMessageBuffer> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public RedisMessageBuffer(IConnectionMultiplexer redis, IOptions<ReceiverOptions> options, ILogger<RedisMessageBuffer> logger)
    {
        _db = redis.GetDatabase();
        _server = redis.GetServer(redis.GetEndPoints()[0]);
        _ttl = TimeSpan.FromHours(options.Value.Redis.BufferTtlHours);
        _logger = logger;
    }

    public async Task SetAsync(string messageId, string routingKey, string envelopeJson, CancellationToken ct)
    {
        var key = KeyPrefix + messageId;
        var wrapper = new BufferWrapper
        {
            StoredAt = DateTime.UtcNow,
            RoutingKey = routingKey,
            MessageId = messageId,
            EnvelopeJson = envelopeJson,
        };
        var value = JsonSerializer.Serialize(wrapper, JsonOpts);
        await _db.StringSetAsync(key, value, _ttl);
        _logger.LogDebug("buffered msg={MessageId} key={Key}", messageId, key);
    }

    public async Task DeleteAsync(string messageId, CancellationToken ct)
    {
        var key = KeyPrefix + messageId;
        await _db.KeyDeleteAsync(key);
    }

    public async IAsyncEnumerable<BufferedMessage> ScanOldAsync(TimeSpan minAge, [EnumeratorCancellation] CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - minAge;

        foreach (var key in _server.Keys(pattern: KeyPrefix + "*", pageSize: 100))
        {
            ct.ThrowIfCancellationRequested();

            var value = await _db.StringGetAsync(key);
            if (value.IsNullOrEmpty) continue;

            BufferWrapper? wrapper;
            try { wrapper = JsonSerializer.Deserialize<BufferWrapper>(value.ToString(), JsonOpts); }
            catch { continue; }

            if (wrapper is null || wrapper.StoredAt > cutoff) continue;

            yield return new BufferedMessage(wrapper.MessageId, wrapper.RoutingKey, wrapper.EnvelopeJson, wrapper.StoredAt);
        }
    }

    public Task<long> CountAsync(CancellationToken ct)
    {
        long count = 0;
        foreach (var _ in _server.Keys(pattern: KeyPrefix + "*", pageSize: 100))
            count++;
        return Task.FromResult(count);
    }

    private sealed class BufferWrapper
    {
        [JsonPropertyName("stored_at")]
        public DateTime StoredAt { get; set; }

        [JsonPropertyName("routing_key")]
        public string RoutingKey { get; set; } = "";

        [JsonPropertyName("message_id")]
        public string MessageId { get; set; } = "";

        [JsonPropertyName("envelope_json")]
        public string EnvelopeJson { get; set; } = "";
    }
}
