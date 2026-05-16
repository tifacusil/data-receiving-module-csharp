using System.Text;
using DataReceiving.Configuration;
using DataReceiving.Parsing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace DataReceiving.Messaging;

public sealed class RabbitMqPublisher : IMessagePublisher
{
    private readonly IModel _channel;
    private readonly RabbitMqOptions _opts;
    private readonly ILogger<RabbitMqPublisher> _logger;
    private readonly object _lock = new();

    public RabbitMqPublisher(IModel channel, IOptions<ReceiverOptions> options, ILogger<RabbitMqPublisher> logger)
    {
        _channel = channel;
        _opts = options.Value.RabbitMq;
        _logger = logger;
    }

    public Task PublishAsync(SensorEnvelope envelope, CancellationToken ct)
    {
        var routingKey = $"sensors.{envelope.ProjectId}.{envelope.SensorType}";
        var json = EnvelopeSerializer.Serialize(envelope);
        return PublishJsonAsync(routingKey, json, envelope.MessageId, ct);
    }

    public Task PublishJsonAsync(string routingKey, string envelopeJson, string messageId, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            lock (_lock)
            {
                var body = Encoding.UTF8.GetBytes(envelopeJson);
                var props = _channel.CreateBasicProperties();
                props.DeliveryMode = 2;
                props.ContentType = "application/json";
                props.MessageId = messageId;

                _channel.BasicPublish(
                    exchange: _opts.Exchange,
                    routingKey: routingKey,
                    basicProperties: props,
                    body: body);

                _channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(_opts.ConfirmTimeoutSeconds));

                _logger.LogDebug("published msg={MessageId} routingKey={RoutingKey}", messageId, routingKey);
            }
        }, ct);
    }
}
