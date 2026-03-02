using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Client;

namespace Yazaki.CommandeChaine.Api.Services;

public sealed class MqttCycleTimePublisher(IConfiguration configuration, ILogger<MqttCycleTimePublisher> logger)
{
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<MqttCycleTimePublisher> _logger = logger;

    public async Task PublishCycleTimeAsync(
        string lineId,
        double calculatedCtSeconds,
        bool isRunning,
        double encoderDelta,
        IReadOnlyList<MqttJigPayload> jigs,
        CancellationToken cancellationToken = default)
    {
        var enabled = _configuration.GetValue<bool>("Mqtt:Enabled");
        if (!enabled)
        {
            return;
        }

        var host = _configuration["Mqtt:Host"] ?? "localhost";
        var port = _configuration.GetValue<int?>("Mqtt:Port") ?? 1883;
        var topicTemplate = _configuration["Mqtt:TopicTemplate"] ?? "yazaki/line/{lineId}/ct";
        var topic = topicTemplate.Replace("{lineId}", lineId, StringComparison.OrdinalIgnoreCase);

        var payload = JsonSerializer.Serialize(new
        {
            line_id = lineId,
            calculated_ct_seconds = calculatedCtSeconds,
            timestamp = DateTime.UtcNow,
            chain_state = new
            {
                is_running = isRunning,
                encoder_delta = encoderDelta
            },
            jigs = jigs
        });

        try
        {
            var factory = new MqttFactory();
            using var client = factory.CreateMqttClient();

            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(host, port)
                .Build();

            await client.ConnectAsync(options, cancellationToken);

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(Encoding.UTF8.GetBytes(payload))
                .WithRetainFlag(false)
                .Build();

            await client.PublishAsync(message, cancellationToken);
            await client.DisconnectAsync(cancellationToken: cancellationToken);

            _logger.LogInformation("Published CT to MQTT: {Topic} -> {CtSeconds}", topic, calculatedCtSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish CT to MQTT broker {Host}:{Port}", host, port);
        }
    }
}

public sealed record MqttJigPayload(
    string jig_id,
    string status,
    double relative_pos);
