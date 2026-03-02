namespace Yazaki.CommandeChaine.Desktop.Services;

public sealed class ApiConfig
{
    public string BaseUrl { get; }

    public ApiConfig()
    {
        BaseUrl = Environment.GetEnvironmentVariable("YAZAKI_API_BASE_URL")
            ?? "http://localhost:5016";
    }
}
