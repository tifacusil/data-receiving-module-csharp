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
    private const int ScanPageSize = 100;

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

    public Task DeleteAsync(string messageId, CancellationToken ct)
    {
        // Fire-and-forget: the delete is off the hot path's latency budget. If it
        // is lost, the retry worker republishes and downstream INSERT IGNORE dedups.
        var key = KeyPrefix + messageId;
        return _db.KeyDeleteAsync(key, CommandFlags.FireAndForget);
    }

    public async IAsyncEnumerable<BufferedMessage> ScanOldAsync(TimeSpan minAge, [EnumeratorCancellation] CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - minAge;
        var page = new List<RedisKey>(ScanPageSize);

        await foreach (var key in _server.KeysAsync(pattern: KeyPrefix + "*", pageSize: ScanPageSize).WithCancellation(ct))
        {
            page.Add(key);
            if (page.Count < ScanPageSize) continue;

            foreach (var msg in await ReadBatchAsync(page, cutoff))
                yield return msg;
            page.Clear();
        }

        if (page.Count > 0)
        {
            foreach (var msg in await ReadBatchAsync(page, cutoff))
                yield return msg;
        }
    }

    // One MGET per page instead of one GET round-trip per key.
    private async Task<List<BufferedMessage>> ReadBatchAsync(List<RedisKey> keys, DateTime cutoff)
    {
        var result = new List<BufferedMessage>();
        var values = await _db.StringGetAsync(keys.ToArray());

        foreach (var value in values)
        {
            if (value.IsNullOrEmpty) continue;

            BufferWrapper? wrapper;
            try { wrapper = JsonSerializer.Deserialize<BufferWrapper>(value.ToString(), JsonOpts); }
            catch { continue; }

            if (wrapper is null || wrapper.StoredAt > cutoff) continue;

            result.Add(new BufferedMessage(wrapper.MessageId, wrapper.RoutingKey, wrapper.EnvelopeJson, wrapper.StoredAt));
        }
        return result;
    }

    public async Task<long> CountAsync(CancellationToken ct)
    {
        long count = 0;
        await foreach (var _ in _server.KeysAsync(pattern: KeyPrefix + "*", pageSize: ScanPageSize).WithCancellation(ct))
            count++;
        return count;
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
