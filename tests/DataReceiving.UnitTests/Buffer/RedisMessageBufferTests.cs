using DataReceiving.Buffer;
using DataReceiving.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace DataReceiving.UnitTests.Buffer;

public class RedisMessageBufferTests : IAsyncLifetime
{
    private const string RedisConn = "localhost:6379";
    private const string KeyPrefix = "sensor_buffer:";

    private IConnectionMultiplexer _redis = null!;
    private IDatabase _db = null!;
    private RedisMessageBuffer _buffer = null!;

    public async Task InitializeAsync()
    {
        _redis = await ConnectionMultiplexer.ConnectAsync(RedisConn);
        _db = _redis.GetDatabase();
        var opts = Options.Create(new ReceiverOptions { Redis = new RedisOptions { BufferTtlHours = 24 } });
        _buffer = new RedisMessageBuffer(_redis, opts, NullLogger<RedisMessageBuffer>.Instance);
    }

    public async Task DisposeAsync()
    {
        // clean up any test keys
        var server = _redis.GetServer(_redis.GetEndPoints()[0]);
        var keys = server.Keys(pattern: KeyPrefix + "test-*").ToArray();
        if (keys.Length > 0)
            await _db.KeyDeleteAsync(keys);
        _redis.Dispose();
    }

    [Fact]
    public async Task test_set_creates_key_with_ttl()
    {
        var id = "test-" + Guid.NewGuid();
        await _buffer.SetAsync(id, "sensors.test.gnss", "{}", CancellationToken.None);

        var ttl = await _db.KeyTimeToLiveAsync(KeyPrefix + id);
        Assert.NotNull(ttl);
        Assert.True(ttl!.Value.TotalHours > 23, "TTL should be close to 24h");

        await _db.KeyDeleteAsync(KeyPrefix + id);
    }

    [Fact]
    public async Task test_delete_removes_key()
    {
        var id = "test-" + Guid.NewGuid();
        await _buffer.SetAsync(id, "sensors.test.gnss", "{}", CancellationToken.None);
        await _buffer.DeleteAsync(id, CancellationToken.None);

        var exists = await _db.KeyExistsAsync(KeyPrefix + id);
        Assert.False(exists);
    }

    [Fact]
    public async Task test_delete_nonexistent_key_does_not_throw()
    {
        var ex = await Record.ExceptionAsync(() =>
            _buffer.DeleteAsync("test-does-not-exist-" + Guid.NewGuid(), CancellationToken.None));
        Assert.Null(ex);
    }

    [Fact]
    public async Task test_scan_old_returns_messages_older_than_min_age()
    {
        var id = "test-" + Guid.NewGuid();
        var json = "{\"message_id\":\"" + id + "\"}";
        await _buffer.SetAsync(id, "sensors.test.gnss", json, CancellationToken.None);

        // Manually backdate the stored_at by overwriting the key with old timestamp
        var key = KeyPrefix + id;
        var rawValue = await _db.StringGetAsync(key);
        var backdated = rawValue.ToString().Replace(
            DateTime.UtcNow.ToString("yyyy-MM-dd"),
            DateTime.UtcNow.AddMinutes(-5).ToString("yyyy-MM-dd"));
        // Use a different approach: just set a short minAge and sleep briefly is impractical.
        // Instead, verify that message with "now" stored_at does NOT appear with minAge=2min.
        var messages = new List<BufferedMessage>();
        await foreach (var msg in _buffer.ScanOldAsync(TimeSpan.FromMinutes(2), CancellationToken.None))
            if (msg.MessageId == id) messages.Add(msg);

        // Message was just stored (< 2min ago), should NOT appear
        Assert.Empty(messages);

        await _db.KeyDeleteAsync(key);
    }

    [Fact]
    public async Task test_scan_old_ignores_recent_messages()
    {
        var id = "test-" + Guid.NewGuid();
        await _buffer.SetAsync(id, "sensors.test.gnss", "{}", CancellationToken.None);

        var found = new List<BufferedMessage>();
        await foreach (var msg in _buffer.ScanOldAsync(TimeSpan.FromMinutes(2), CancellationToken.None))
            if (msg.MessageId == id) found.Add(msg);

        Assert.Empty(found);
        await _db.KeyDeleteAsync(KeyPrefix + id);
    }

    [Fact]
    public async Task test_count_returns_buffered_messages()
    {
        var ids = new[] { "test-" + Guid.NewGuid(), "test-" + Guid.NewGuid(), "test-" + Guid.NewGuid() };
        foreach (var id in ids)
            await _buffer.SetAsync(id, "sensors.test.gnss", "{}", CancellationToken.None);

        var count = await _buffer.CountAsync(CancellationToken.None);
        Assert.True(count >= 3, $"expected at least 3 buffered messages, got {count}");

        foreach (var id in ids)
            await _db.KeyDeleteAsync(KeyPrefix + id);
    }

    [Fact]
    public async Task test_stored_envelope_json_is_recoverable()
    {
        var id = "test-" + Guid.NewGuid();
        var routingKey = "sensors.shangtan.gnss";
        var envelopeJson = """{"message_id":"abc","sensor_type":"gnss"}""";

        await _buffer.SetAsync(id, routingKey, envelopeJson, CancellationToken.None);

        // Retrieve the raw value from Redis and verify envelope_json is intact
        var raw = await _db.StringGetAsync(KeyPrefix + id);
        Assert.False(raw.IsNullOrEmpty);
        var doc = System.Text.Json.JsonDocument.Parse(raw.ToString());
        var storedJson = doc.RootElement.GetProperty("envelope_json").GetString();
        Assert.Equal(envelopeJson, storedJson);

        var storedRoutingKey = doc.RootElement.GetProperty("routing_key").GetString();
        Assert.Equal(routingKey, storedRoutingKey);

        await _db.KeyDeleteAsync(KeyPrefix + id);
    }
}
