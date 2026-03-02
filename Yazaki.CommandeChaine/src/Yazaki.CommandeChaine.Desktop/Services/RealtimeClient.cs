using Microsoft.AspNetCore.SignalR.Client;

namespace Yazaki.CommandeChaine.Desktop.Services;

public sealed class RealtimeClient
{
    private HubConnection? _connection;

    public event Action<ScanReceivedDto>? ScanReceived;
    public event Action<SpeedRecommendedDto>? SpeedRecommended;
    public event Action<SpeedUpdatedDto>? SpeedUpdated;
    public event Action<SpeedFromRaspberryDto>? SpeedFromRaspberry;
    public event Action<HarnessCompletedDto>? HarnessCompleted;
    public event Action<QualityEventReceivedDto>? QualityEventReceived;

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public async Task ConnectAsync(string baseUrl)
    {
        if (_connection is null)
        {
            var hubUrl = new Uri(new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/"), "hubs/realtime");
            _connection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            _connection.On<ScanReceivedDto>("ScanReceived", dto => ScanReceived?.Invoke(dto));
            _connection.On<SpeedRecommendedDto>("SpeedRecommended", dto => SpeedRecommended?.Invoke(dto));
            _connection.On<SpeedUpdatedDto>("SpeedUpdated", dto => SpeedUpdated?.Invoke(dto));
            _connection.On<SpeedFromRaspberryDto>("SpeedFromRaspberry", dto => SpeedFromRaspberry?.Invoke(dto));
            _connection.On<HarnessCompletedDto>("HarnessCompleted", dto => HarnessCompleted?.Invoke(dto));
            _connection.On<QualityEventReceivedDto>("QualityEvent", dto => QualityEventReceived?.Invoke(dto));
        }

        if (_connection.State == HubConnectionState.Connected)
        {
            return;
        }

        await _connection.StartAsync();
    }

    public async Task EnsureConnectedAsync(string baseUrl)
    {
        if (_connection is null || _connection.State == HubConnectionState.Disconnected)
        {
            await ConnectAsync(baseUrl);
        }
    }

    public async Task JoinChainAsync(Guid chainId)
    {
        if (_connection is null)
        {
            return;
        }

        await _connection.InvokeAsync("JoinChain", chainId);
    }
}

public sealed record ScanReceivedDto(Guid Id, string Barcode, string? ScannerId, Guid? ChainId, Guid? TableId, DateTimeOffset ScannedAtUtc);
public sealed record SpeedRecommendedDto(Guid ChainId, double RecommendedSpeedRpm, double Confidence, string Rationale, DateTimeOffset ComputedAtUtc);
public sealed record SpeedUpdatedDto(Guid ChainId, double RecommendedSpeedRpm, double? CycleTimeMinutes, int HarnessesOnChain);
public sealed record SpeedFromRaspberryDto(Guid ChainId, string LineId, double SpeedRpm, double Voltage, double CtSeconds, int HarnessesOnChain, string Timestamp);
public sealed record HarnessCompletedDto(Guid ChainId, string Reference, int CompletedCount, int TotalCount);
public sealed record QualityEventReceivedDto(Guid Id, Guid ChainId, Guid? TableId, int Kind, int? Cause, double? DelayPercent, double? DurationMinutes, string? Note, DateTimeOffset OccurredAtUtc);
