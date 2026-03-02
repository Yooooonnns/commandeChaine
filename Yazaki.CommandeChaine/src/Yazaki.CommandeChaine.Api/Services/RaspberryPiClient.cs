using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Yazaki.CommandeChaine.Api.Services;

public sealed class RaspberryPiClient
{
    private readonly HttpClient _http;
    private readonly ILogger<RaspberryPiClient> _logger;
    private readonly string? _baseUrl;
    private double? _lastVoltage;
    private DateTime? _lastCommandAtUtc;
    private bool? _lastChainRunning;
    private double? _lastEncoderDelta;

    public RaspberryPiClient(HttpClient http, IConfiguration config, ILogger<RaspberryPiClient> logger)
    {
        _http = http;
        _logger = logger;
        _baseUrl = config["RaspberryPi:ApiUrl"];

        if (!string.IsNullOrWhiteSpace(_baseUrl))
        {
            _http.BaseAddress = new Uri(_baseUrl.EndsWith('/') ? _baseUrl : _baseUrl + "/");
        }
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_baseUrl);
    public double? LastVoltage => _lastVoltage;
    public DateTime? LastCommandAtUtc => _lastCommandAtUtc;
    public bool? LastChainRunning => _lastChainRunning;
    public double? LastEncoderDelta => _lastEncoderDelta;

    /// <summary>
    /// Send a speed command to the Raspberry Pi.
    /// Returns the applied voltage or null if failed/not configured.
    /// </summary>
    public async Task<double?> SendSpeedCommandAsync(
        string lineId,
        double speed,
        string mode,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            _logger.LogDebug("RaspberryPi not configured. Skipping speed command.");
            return null;
        }

        try
        {
            var payload = new
            {
                line_id = lineId,
                speed = Math.Round(speed, 2),
                mode = mode,
                timestamp = DateTimeOffset.UtcNow.ToString("O")
            };

            _logger.LogDebug("Sending speed command to RaspberryPi: {LineId} {Speed} RPM", lineId, speed);

            var response = await _http.PostAsJsonAsync("api/v1/command", payload, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("RaspberryPi responded with {StatusCode}: {Content}", 
                    response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<RaspberryPiCommandResponse>(cancellationToken: cancellationToken);
            if (result is null)
            {
                _logger.LogWarning("RaspberryPi response deserialization failed.");
                return null;
            }

            // Store the last voltage and command time
            _lastVoltage = result.Voltage;
            _lastCommandAtUtc = result.AppliedAt;

            _logger.LogInformation("RaspberryPi speed command applied: {Status} -> {Voltage}V", result.Status, result.Voltage);
            return result.Voltage;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "RaspberryPi HTTP error. Is it running at {BaseUrl}?", _baseUrl);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "RaspberryPi request timeout.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending speed to RaspberryPi.");
            return null;
        }
    }

    /// <summary>
    /// Check if the Raspberry Pi is healthy and fetch current voltage state.
    /// </summary>
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return false;
        }

        try
        {
            var response = await _http.GetAsync("api/v1/health", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            // Also fetch the current state to get the voltage
            try
            {
                var stateResponse = await _http.GetAsync("api/v1/state", cancellationToken);
                if (stateResponse.IsSuccessStatusCode)
                {
                    var state = await stateResponse.Content.ReadFromJsonAsync<RaspberryPiState>(cancellationToken: cancellationToken);
                    if (state?.LastVoltage.HasValue == true)
                    {
                        _lastVoltage = state.LastVoltage;
                    }

                    if (state?.ChainState is not null)
                    {
                        _lastChainRunning = state.ChainState.IsRunning;
                        _lastEncoderDelta = state.ChainState.EncoderDelta;
                    }
                }
            }
            catch
            {
                // If state fetch fails, that's ok - we still report as healthy if health check passed
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}

public sealed record RaspberryPiCommandResponse(
    string Status,
    double SpeedUsed,
    double Voltage,
    string Reason,
    DateTime AppliedAt);
public sealed record RaspberryPiState(
    double? LastValidSpeed,
    double? LastVoltage,
    [property: JsonPropertyName("chain_state")]
    RaspberryPiChainState? ChainState);

public sealed record RaspberryPiChainState(
    [property: JsonPropertyName("is_running")]
    bool? IsRunning,
    [property: JsonPropertyName("encoder_delta")]
    double? EncoderDelta,
    [property: JsonPropertyName("updated_at")]
    DateTime? UpdatedAt);