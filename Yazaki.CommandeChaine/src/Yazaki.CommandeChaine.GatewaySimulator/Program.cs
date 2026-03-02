using System.Net.Http.Json;

var apiBaseUrl = Environment.GetEnvironmentVariable("YAZAKI_API_BASE_URL") ?? "http://localhost:5000";
var chainIdText = Environment.GetEnvironmentVariable("YAZAKI_CHAIN_ID");
var tableIdText = Environment.GetEnvironmentVariable("YAZAKI_TABLE_ID");
var scannerId = Environment.GetEnvironmentVariable("YAZAKI_SCANNER_ID") ?? "SIM-GW";

Guid? chainId = Guid.TryParse(chainIdText, out var cid) ? cid : null;
Guid? tableId = Guid.TryParse(tableIdText, out var tid) ? tid : null;

Console.WriteLine("Yazaki WiFi Gateway Simulator");
Console.WriteLine($"API: {apiBaseUrl}");
Console.WriteLine($"ScannerId: {scannerId}");
Console.WriteLine($"ChainId: {chainId?.ToString() ?? "(null)"}");
Console.WriteLine($"TableId: {tableId?.ToString() ?? "(null)"}");
Console.WriteLine("Commands: Enter = send random barcode | type barcode then Enter | q = quit");

using var http = new HttpClient { BaseAddress = new Uri(apiBaseUrl.EndsWith('/') ? apiBaseUrl : apiBaseUrl + "/") };

while (true)
{
	var line = Console.ReadLine();
	if (line is null)
	{
		continue;
	}

	if (string.Equals(line.Trim(), "q", StringComparison.OrdinalIgnoreCase))
	{
		return;
	}

	var barcode = string.IsNullOrWhiteSpace(line)
		? $"CABLE-{Random.Shared.Next(100000, 999999)}"
		: line.Trim();

	var payload = new
	{
		Barcode = barcode,
		ScannerId = scannerId,
		ChainId = chainId,
		TableId = tableId,
		ScannedAtUtc = (DateTimeOffset?)null
	};

	try
	{
		var resp = await http.PostAsJsonAsync("api/scans", payload);
		var body = await resp.Content.ReadAsStringAsync();
		Console.WriteLine(resp.IsSuccessStatusCode
			? $"OK -> {body}"
			: $"ERROR ({(int)resp.StatusCode}) -> {body}");
	}
	catch (Exception ex)
	{
		Console.WriteLine($"ERROR -> {ex.Message}");
	}
}
