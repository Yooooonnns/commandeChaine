using System.Net.Http;
using System.Net.Http.Json;

namespace Yazaki.CommandeChaine.Desktop.Services;

public sealed class CommandeChaineApiClient
{
    private readonly HttpClient _http;

    public CommandeChaineApiClient(string baseUrl)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/")
        };
    }

    public async Task<bool> IsHealthyAsync()
    {
        var response = await _http.GetAsync("health");
        return response.IsSuccessStatusCode;
    }

    public async Task<RaspberryPiHealthDto?> GetRaspberryPiHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<RaspberryPiHealthDto>("api/raspberrypi/health", cancellationToken);
            return result;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<List<PendingCableDto>> GetPendingCablesAsync(Guid chainTableId, CancellationToken cancellationToken = default)
    {
        var result = await _http.GetFromJsonAsync<List<PendingCableDto>>($"api/fo/pending-cables/{chainTableId}", cancellationToken);
        return result ?? new List<PendingCableDto>();
    }

    public async Task<CableValidationResultDto> ValidateCableAsync(Guid foHarnessId, Guid chainTableId, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new ValidateCableRequest(foHarnessId, chainTableId);
            var response = await _http.PostAsJsonAsync("api/fo/validate-cable", payload, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"API returned {response.StatusCode}: {errorContent}");
            }

            var result = await response.Content.ReadFromJsonAsync<CableValidationResultDto>(cancellationToken: cancellationToken);
            return result ?? throw new InvalidOperationException("API returned empty response for ValidateCable.");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to call validate-cable endpoint: {ex.Message}", ex);
        }
    }

    public async Task<List<ChainDto>> GetChainsAsync(CancellationToken cancellationToken = default)
    {
        var result = await _http.GetFromJsonAsync<List<ChainDto>>("api/chains", cancellationToken);
        return result ?? new List<ChainDto>();
    }

    public async Task<ChainDto> CreateChainAsync(string name, int tableCount = 0, CancellationToken cancellationToken = default)
    {
        var payload = new CreateChainRequest(
            name,
            tableCount,
            WorkerCount: 1,
            ProductivityFactor: 1.0,
            PitchDistanceMeters: 1.0,
            BalancingTuningK: 0.7);
        var response = await _http.PostAsJsonAsync("api/chains", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<ChainDto>(cancellationToken: cancellationToken);
        return dto ?? throw new InvalidOperationException("API returned empty response for CreateChain.");
    }

    public async Task<ChainDto> RenameChainAsync(Guid chainId, string name, CancellationToken cancellationToken = default)
    {
        var payload = new RenameChainRequest(name);
        var response = await _http.PutAsJsonAsync($"api/chains/{chainId}", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<ChainDto>(cancellationToken: cancellationToken);
        return dto ?? throw new InvalidOperationException("API returned empty response for RenameChain.");
    }

    public async Task DeleteChainAsync(Guid chainId, CancellationToken cancellationToken = default)
    {
        var response = await _http.DeleteAsync($"api/chains/{chainId}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<ChainDto> UpdateTableCountAsync(Guid chainId, int tableCount, CancellationToken cancellationToken = default)
    {
        var payload = new UpdateChainTablesRequest(tableCount);
        var response = await _http.PutAsJsonAsync($"api/chains/{chainId}/tables", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<ChainDto>(cancellationToken: cancellationToken);
        return dto ?? throw new InvalidOperationException("API returned empty response for UpdateTableCount.");
    }

    public async Task<ChainTableDto> RenameChainTableAsync(Guid tableId, string name, CancellationToken cancellationToken = default)
    {
        var payload = new RenameChainTableRequest(name);
        var response = await _http.PutAsJsonAsync($"api/chains/tables/{tableId}", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<ChainTableDto>(cancellationToken: cancellationToken);
        return dto ?? throw new InvalidOperationException("API returned empty response for RenameChainTable.");
    }

    public async Task IngestScanAsync(HarnessItem harness, string? scannerId, Guid? chainId, Guid? tableId, CancellationToken cancellationToken = default)
    {
        var payload = new ScanIngestRequest(
            harness.Barcode,
            scannerId,
            chainId,
            tableId,
            null,
            harness.Fo,
            harness.Type.ToString(),
            harness.Quantity,
            harness.PlannedDate,
            harness.ProductionTimeInMinutes,
            harness.IsUrgent);

        var response = await _http.PostAsJsonAsync("api/scans", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<FoAssignResponse> AssignFoToChainAsync(Guid chainId, string foName, List<FOHarnessRow> rows, CancellationToken cancellationToken = default)
    {
        var payload = new FoAssignRequest(
            chainId,
            foName,
            rows.Select(x => new FoHarnessDto(x.Reference, x.ProductionTimeMinutes, x.IsUrgent, x.IsLate, x.OrderIndex)).ToList());

        var response = await _http.PostAsJsonAsync("api/fo/assign", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<FoAssignResponse>(cancellationToken: cancellationToken);
        return dto ?? throw new InvalidOperationException("API returned empty response for FO assign.");
    }

    public async Task<FoStatusResponse?> GetFoStatusAsync(Guid chainId, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"api/fo/status/{chainId}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<FoStatusResponse>(cancellationToken: cancellationToken);
    }

    public async Task<FoCurrentResponse?> GetFoCurrentForBoardAsync(Guid chainId, int tableIndex, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"api/fo/current/{chainId}/{tableIndex}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<FoCurrentResponse>(cancellationToken: cancellationToken);
    }

    public async Task<FoBoardMetricsResponse?> GetFoBoardMetricsAsync(Guid chainId, int tableIndex, CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync($"api/fo/board-metrics/{chainId}/{tableIndex}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<FoBoardMetricsResponse>(cancellationToken: cancellationToken);
    }

    public async Task<FoStatusResponse> CompleteNextFoAsync(Guid chainId, CancellationToken cancellationToken = default)
    {
        var payload = new FoCompleteNextRequest(chainId);
        var response = await _http.PostAsJsonAsync("api/fo/complete-next", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<FoStatusResponse>(cancellationToken: cancellationToken);
        return dto ?? throw new InvalidOperationException("API returned empty response for FO complete-next.");
    }

    public async Task CancelFoAsync(Guid chainId, CancellationToken cancellationToken = default)
    {
        var payload = new FoCancelRequest(chainId);
        var response = await _http.PostAsJsonAsync("api/fo/cancel", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task IngestQualityEventAsync(QualityIngestRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync("api/quality", request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<TableCreditDto>> UpdateTimeCreditAsync(Guid chainId, List<TableCreditUpdateDto> tables, CancellationToken cancellationToken = default)
    {
        var payload = new CreditUpdateRequest(chainId, tables, DateTimeOffset.UtcNow);
        var response = await _http.PostAsJsonAsync("api/credit/update", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<List<TableCreditDto>>(cancellationToken: cancellationToken);
        return result ?? new List<TableCreditDto>();
    }

    public async Task<List<TableCreditDto>> GetTimeCreditAsync(Guid chainId, CancellationToken cancellationToken = default)
    {
        var url = $"api/credit/chain/{chainId}";
        var result = await _http.GetFromJsonAsync<List<TableCreditDto>>(url, cancellationToken);
        return result ?? new List<TableCreditDto>();
    }

    public async Task<List<DelayPointDto>> GetDelaySeriesAsync(DateTime from, DateTime to, Guid? chainId, Guid? tableId, string? cause, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string>
        {
            ["from"] = FormatDate(from),
            ["to"] = FormatDate(to)
        };

        if (chainId.HasValue)
        {
            query["chainId"] = chainId.Value.ToString();
        }

        if (tableId.HasValue)
        {
            query["tableId"] = tableId.Value.ToString();
        }

        if (!string.IsNullOrWhiteSpace(cause))
        {
            query["cause"] = cause;
        }

        var url = "api/dashboard/delay" + BuildQuery(query);
        var result = await _http.GetFromJsonAsync<List<DelayPointDto>>(url, cancellationToken);
        return result ?? new List<DelayPointDto>();
    }

    public async Task<List<StopSummaryDto>> GetStopSummaryAsync(DateTime from, DateTime to, Guid? chainId, Guid? tableId, string? cause, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string>
        {
            ["from"] = FormatDate(from),
            ["to"] = FormatDate(to)
        };

        if (chainId.HasValue)
        {
            query["chainId"] = chainId.Value.ToString();
        }

        if (tableId.HasValue)
        {
            query["tableId"] = tableId.Value.ToString();
        }

        if (!string.IsNullOrWhiteSpace(cause))
        {
            query["cause"] = cause;
        }

        var url = "api/dashboard/stops" + BuildQuery(query);
        var result = await _http.GetFromJsonAsync<List<StopSummaryDto>>(url, cancellationToken);
        return result ?? new List<StopSummaryDto>();
    }

    public async Task<List<CreditPointDto>> GetCreditSeriesAsync(DateTime from, DateTime to, Guid? chainId, Guid? tableId, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string>
        {
            ["from"] = FormatDate(from),
            ["to"] = FormatDate(to)
        };

        if (chainId.HasValue)
        {
            query["chainId"] = chainId.Value.ToString();
        }

        if (tableId.HasValue)
        {
            query["tableId"] = tableId.Value.ToString();
        }

        var url = "api/dashboard/credit" + BuildQuery(query);
        var result = await _http.GetFromJsonAsync<List<CreditPointDto>>(url, cancellationToken);
        return result ?? new List<CreditPointDto>();
    }

    private static string BuildQuery(Dictionary<string, string> query)
    {
        if (query.Count == 0)
        {
            return string.Empty;
        }

        var items = query.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}");
        return "?" + string.Join("&", items);
    }

    private static string FormatDate(DateTime value)
    {
        var local = value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Local)
            : value;
        return new DateTimeOffset(local).ToString("O");
    }
}

public sealed record ChainDto(
    Guid Id,
    string Name,
    int WorkerCount,
    double ProductivityFactor,
    double PitchDistanceMeters,
    double BalancingTuningK,
    List<ChainTableDto> Tables);
public sealed record ChainTableDto(Guid Id, int Index, string Name);
public sealed record CreateChainRequest(
    string Name,
    int TableCount,
    int WorkerCount,
    double ProductivityFactor,
    double PitchDistanceMeters,
    double BalancingTuningK);
public sealed record RenameChainRequest(string Name);
public sealed record RenameChainTableRequest(string Name);
public sealed record ScanIngestRequest(string Barcode, string? ScannerId, Guid? ChainId, Guid? TableId, DateTimeOffset? ScannedAtUtc, string? Fo, string? HarnessType, int? Quantity, DateOnly? PlannedDate, int? ProductionTimeInMinutes, bool? IsUrgent);
public sealed record UpdateChainTablesRequest(int TableCount);
public sealed record FoHarnessDto(string Reference, int ProductionTimeMinutes, bool IsUrgent, bool IsLate, int OrderIndex);
public sealed record FoAssignRequest(Guid ChainId, string FoName, List<FoHarnessDto> Harnesses);
public sealed record FoAssignResponse(Guid FoId, Guid ChainId, string FoName, int HarnessCount, double RecommendedSpeedRpm);
public sealed record FoStatusResponse(Guid FoId, Guid ChainId, string FoName, int HarnessCount, int CompletedCount, double RecommendedSpeedRpm);
public sealed record FoCompleteNextRequest(Guid ChainId);
public sealed record FoCancelRequest(Guid ChainId);
public sealed record FoCurrentResponse(string Reference, int OrderIndex);
public sealed record FoBoardMetricsResponse(int MaxProductionTimeMinutes, int CurrentProductionTimeMinutes, string Reference);
public sealed record QualityIngestRequest(Guid ChainId, Guid? TableId, QualityEventKind Kind, string? Cause, double? DelayPercent, double? DurationMinutes, string? Note, DateTimeOffset? OccurredAtUtc);
public sealed record CreditUpdateRequest(Guid ChainId, List<TableCreditUpdateDto> Tables, DateTimeOffset? OccurredAtUtc);
public sealed record TableCreditUpdateDto(Guid TableId, double ProgressRatio);
public sealed record TableCreditDto(Guid TableId, int TableIndex, double? ProgressRatio, double TargetRatio, double CreditRatio, double CreditMinutes, DateTimeOffset? UpdatedAtUtc);
public sealed record DelayPointDto(DateTime TimestampUtc, double Value, int Count);
public sealed record StopSummaryDto(string Cause, int Count, double TotalDurationMinutes);
public sealed record CreditPointDto(DateTime TimestampUtc, double Value, int Count);
public sealed record RaspberryPiHealthDto(
    bool IsHealthy,
    string? Status,
    double? LastVoltage,
    DateTime? LastCommandAtUtc,
    bool? ChainRunning,
    double? EncoderDelta);
public sealed record PendingCableDto(Guid Id, string Reference, int ProductionTimeMinutes, bool IsUrgent, bool IsLate, int OrderIndex, int ValidationStatus);
public sealed record ValidateCableRequest(Guid FoHarnessId, Guid ChainTableId);
public sealed record CableValidationResultDto(string CableReference, int Status, DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc);

public enum BoardCableValidationStatus
{
    Pending = 0,
    Started = 1,
    Completed = 2
}

public enum QualityEventKind
{
    Ok = 0,
    Defect = 1,
    Rework = 2,
    Stop = 3
}
