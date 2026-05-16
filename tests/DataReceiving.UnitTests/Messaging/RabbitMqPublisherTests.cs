using DataReceiving.Configuration;
using DataReceiving.Messaging;
using DataReceiving.Parsing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RabbitMQ.Client;

namespace DataReceiving.UnitTests.Messaging;

public class RabbitMqPublisherTests
{
    private static (RabbitMqPublisher publisher, IModel channel, IBasicProperties props) CreatePublisher()
    {
        var mockProps = Substitute.For<IBasicProperties>();
        var mockChannel = Substitute.For<IModel>();
        mockChannel.CreateBasicProperties().Returns(mockProps);

        var opts = Options.Create(new ReceiverOptions
        {
            RabbitMq = new RabbitMqOptions
            {
                Exchange = "sensors.raw",
                ConfirmTimeoutSeconds = 5,
            }
        });
        var publisher = new RabbitMqPublisher(mockChannel, opts, NullLogger<RabbitMqPublisher>.Instance);
        return (publisher, mockChannel, mockProps);
    }

    private static SensorEnvelope MakeEnvelope(string projectId, string sensorType) =>
        new()
        {
            MessageId = Guid.NewGuid().ToString(),
            ProjectId = projectId,
            SensorId = "TEST-01",
            SensorType = sensorType,
            Timestamp = "2026-05-02T14:30:00Z",
            ReceivedAt = "2026-05-02T14:30:01Z",
            SamplingIntervalSeconds = 3600,
            Payload = new GnssPayload(0, 0, 0, 0, 0, "no_fix"),
        };

    [Fact]
    public async Task test_routing_key_gnss_shangtan()
    {
        var (pub, ch, _) = CreatePublisher();
        await pub.PublishAsync(MakeEnvelope("shangtan", "gnss"), CancellationToken.None);

        ch.Received().BasicPublish(
            exchange: "sensors.raw",
            routingKey: "sensors.shangtan.gnss",
            mandatory: Arg.Any<bool>(),
            basicProperties: Arg.Any<IBasicProperties>(),
            body: Arg.Any<ReadOnlyMemory<byte>>());
    }

    [Fact]
    public async Task test_routing_key_deep_displacement_lingzhan()
    {
        var (pub, ch, _) = CreatePublisher();
        await pub.PublishAsync(MakeEnvelope("lingzhan", "deep_displacement"), CancellationToken.None);

        ch.Received().BasicPublish(
            exchange: "sensors.raw",
            routingKey: "sensors.lingzhan.deep_displacement",
            mandatory: Arg.Any<bool>(),
            basicProperties: Arg.Any<IBasicProperties>(),
            body: Arg.Any<ReadOnlyMemory<byte>>());
    }

    [Theory]
    [InlineData("shangtan", "gnss",               "sensors.shangtan.gnss")]
    [InlineData("shangtan", "rainfall",           "sensors.shangtan.rainfall")]
    [InlineData("shangtan", "strain",             "sensors.shangtan.strain")]
    [InlineData("shangtan", "soil_pressure",      "sensors.shangtan.soil_pressure")]
    [InlineData("lingzhan", "deep_displacement",  "sensors.lingzhan.deep_displacement")]
    public async Task test_routing_key_for_all_five_sensor_types(string project, string sensorType, string expectedKey)
    {
        var (pub, ch, _) = CreatePublisher();
        await pub.PublishAsync(MakeEnvelope(project, sensorType), CancellationToken.None);

        ch.Received().BasicPublish(
            exchange: "sensors.raw",
            routingKey: expectedKey,
            mandatory: Arg.Any<bool>(),
            basicProperties: Arg.Any<IBasicProperties>(),
            body: Arg.Any<ReadOnlyMemory<byte>>());
    }

    [Fact]
    public async Task test_delivery_mode_is_persistent()
    {
        var (pub, _, props) = CreatePublisher();
        await pub.PublishAsync(MakeEnvelope("shangtan", "gnss"), CancellationToken.None);

        Assert.Equal(2, props.DeliveryMode);
    }

    [Fact]
    public async Task test_body_is_valid_json()
    {
        var (pub, ch, _) = CreatePublisher();
        var envelope = MakeEnvelope("shangtan", "gnss");

        ReadOnlyMemory<byte> capturedBody = default;
        ch.When(x => x.BasicPublish(
            exchange: Arg.Any<string>(),
            routingKey: Arg.Any<string>(),
            mandatory: Arg.Any<bool>(),
            basicProperties: Arg.Any<IBasicProperties>(),
            body: Arg.Any<ReadOnlyMemory<byte>>()))
            .Do(call => capturedBody = call.ArgAt<ReadOnlyMemory<byte>>(4));

        await pub.PublishAsync(envelope, CancellationToken.None);

        Assert.False(capturedBody.IsEmpty);
        var json = System.Text.Encoding.UTF8.GetString(capturedBody.Span);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(envelope.MessageId, doc.RootElement.GetProperty("message_id").GetString());
    }

    [Fact]
    public async Task test_message_id_in_properties()
    {
        var (pub, _, props) = CreatePublisher();
        var envelope = MakeEnvelope("shangtan", "gnss");

        await pub.PublishAsync(envelope, CancellationToken.None);

        Assert.Equal(envelope.MessageId, props.MessageId);
    }
}
