using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MQTTnet;
using MQTTnet.Client;
using Yazaki.CommandeChaine.Api.Hubs;
using Yazaki.CommandeChaine.Infrastructure.Persistence;

namespace Yazaki.CommandeChaine.Api.Services;

/// <summary>
/// Background service that subscribes to speed updates from Raspberry Pi via MQTT.
/// </summary>
public sealed class MqttSpeedSubscriber : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<MqttSpeedSubscriber> _logger;
    private readonly IHubContext<RealtimeHub> _hubContext;
    private readonly IServiceScopeFactory _scopeFactory;
    private IMqttClient? _mqttClient;

    public MqttSpeedSubscriber(
        IConfiguration configuration,
        ILogger<MqttSpeedSubscriber> logger,
        IHubContext<RealtimeHub> hubContext,
        IServiceScopeFactory scopeFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _hubContext = hubContext;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _configuration.GetValue<bool>("Mqtt:Enabled");
        if (!enabled)
        {
            _logger.LogInformation("MQTT speed subscriber disabled");
            return;
        }

        var host = _configuration["Mqtt:Host"] ?? "localhost";
        var port = _configuration.GetValue<int?>("Mqtt:Port") ?? 1883;
        var topic = _configuration["Mqtt:SpeedResponseTopic"] ?? "yazaki/line/+/speed";

        var factory = new MqttFactory();
        _mqttClient = factory.CreateMqttClient();

        _mqttClient.ApplicationMessageReceivedAsync += HandleSpeedMessageAsync;

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(host, port)
            .WithCleanSession()
            .Build();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_mqttClient.IsConnected)
                {
                    _logger.LogInformation("Connecting MQTT speed subscriber to {Host}:{Port}", host, port);
                    await _mqttClient.ConnectAsync(options, stoppingToken);

                    var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                        .WithTopicFilter(topic)
                        .Build();

                    await _mqttClient.SubscribeAsync(subscribeOptions, stoppingToken);
                    _logger.LogInformation("MQTT speed subscriber connected and subscribed to {Topic}", topic);
                }

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MQTT speed subscriber error, retrying in 5s");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task HandleSpeedMessageAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        try
        {
            var payload = Encoding.UTF8.GetString(args.ApplicationMessage.PayloadSegment);
            var message = JsonSerializer.Deserialize<SpeedResponseMessage>(payload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (message is null)
            {
                return;
            }

            _logger.LogInformation(
                "Received speed from Raspberry: line={LineId} speed_rpm={SpeedRpm} voltage={Voltage}",
                message.LineId,
                message.SpeedRpm,
                message.Voltage);

            // Update the batch with speed from Raspberry
            await UpdateBatchSpeedAsync(message);

            // Broadcast to connected clients via SignalR
            await BroadcastSpeedUpdateAsync(message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process speed message");
        }
    }

    private async Task UpdateBatchSpeedAsync(SpeedResponseMessage message)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommandeChaineDbContext>();

        // Find chain by name (line_id)
        var chain = await db.Chains
            .FirstOrDefaultAsync(x => x.Name == message.LineId);

        if (chain is null)
        {
            return;
        }

        var batch = await db.FoBatches
            .FirstOrDefaultAsync(x => x.ChainId == chain.Id);

        if (batch is not null)
        {
            batch.RecommendedSpeedRpm = message.SpeedRpm;
            await db.SaveChangesAsync();
        }
    }

    private async Task BroadcastSpeedUpdateAsync(SpeedResponseMessage message)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommandeChaineDbContext>();

        var chain = await db.Chains.FirstOrDefaultAsync(x => x.Name == message.LineId);
        if (chain is null)
        {
            return;
        }

        await _hubContext.Clients.Group(RealtimeHub.ChainGroup(chain.Id)).SendAsync("SpeedUpdate", new
        {
            chainId = chain.Id,
            lineId = message.LineId,
            speedRpm = message.SpeedRpm,
            voltage = message.Voltage,
            ctSeconds = message.CtSeconds,
            timestamp = message.Timestamp
        });
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_mqttClient?.IsConnected == true)
        {
            await _mqttClient.DisconnectAsync(cancellationToken: cancellationToken);
        }
        _mqttClient?.Dispose();
        await base.StopAsync(cancellationToken);
    }
}

public sealed record SpeedResponseMessage(
    string LineId,
    double SpeedRpm,
    double Voltage,
    double CtSeconds,
    string Timestamp
)
{
    public SpeedResponseMessage() : this("", 0, 0, 0, "") { }
}
