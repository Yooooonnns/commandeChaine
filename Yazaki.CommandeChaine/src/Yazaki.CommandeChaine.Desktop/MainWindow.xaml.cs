using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Yazaki.CommandeChaine.Desktop.Services;
using Yazaki.CommandeChaine.Desktop.Views;

namespace Yazaki.CommandeChaine.Desktop;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly ApiConfig _apiConfig;
    private readonly CommandeChaineApiClient _apiClient;
    private readonly RealtimeClient _realtime;
    private CancellationTokenSource? _refreshCancellation;

    public MainWindow()
    {
        InitializeComponent();

        _apiConfig = App.Services.GetRequiredService<ApiConfig>();
        _apiClient = App.Services.GetRequiredService<CommandeChaineApiClient>();
        _realtime = App.Services.GetRequiredService<RealtimeClient>();

        MenuList.SelectedIndex = 0;

        Loaded += async (_, _) =>
        {
            await UpdateApiStatusAsync();
            await UpdateRaspberryPiStatusAsync();

            try
            {
                await _realtime.ConnectAsync(_apiConfig.BaseUrl);
            }
            catch
            {
                // Keep UI usable even if API/SignalR is not reachable yet.
                ApiStatusText.Text = $"API: OFFLINE ({_apiConfig.BaseUrl})";
            }

            // Start periodic Raspberry Pi status refresh
            _refreshCancellation = new CancellationTokenSource();
            _ = RefreshRaspberryPiStatusPeriodicAsync(_refreshCancellation.Token);
        };

        Closed += (_, _) =>
        {
            _refreshCancellation?.Cancel();
            _refreshCancellation?.Dispose();
        };
    }

    private async Task UpdateApiStatusAsync()
    {
        try
        {
            var ok = await _apiClient.IsHealthyAsync();
            ApiStatusText.Text = ok ? $"API: OK ({_apiConfig.BaseUrl})" : $"API: OFFLINE ({_apiConfig.BaseUrl})";
        }
        catch
        {
            ApiStatusText.Text = $"API: OFFLINE ({_apiConfig.BaseUrl})";
        }
    }

    private async Task UpdateRaspberryPiStatusAsync()
    {
        try
        {
            var health = await _apiClient.GetRaspberryPiHealthAsync();
            if (health is null)
            {
                // API call failed
                RaspberryPiStatusIndicator.Fill = new SolidColorBrush(Colors.Gray);
                RaspberryPiStatusText.Text = "Pi: Disconnected";
                RaspberryPiVoltageText.Text = "Voltage: N/A";
                return;
            }

            if (health.IsHealthy)
            {
                RaspberryPiStatusIndicator.Fill = new SolidColorBrush(Colors.LimeGreen);
                var chainLabel = health.ChainRunning switch
                {
                    true => "Running",
                    false => "Stopped",
                    null => "Unknown"
                };
                RaspberryPiStatusText.Text = $"Pi: Connected ({chainLabel})";
            }
            else
            {
                RaspberryPiStatusIndicator.Fill = new SolidColorBrush(Colors.Red);
                RaspberryPiStatusText.Text = "Pi: Offline";
            }

            if (health.LastVoltage.HasValue)
            {
                RaspberryPiVoltageText.Text = $"Voltage: {health.LastVoltage:F2}V";
            }
            else
            {
                RaspberryPiVoltageText.Text = "Voltage: N/A";
            }
        }
        catch
        {
            RaspberryPiStatusIndicator.Fill = new SolidColorBrush(Colors.Gray);
            RaspberryPiStatusText.Text = "Pi: Unknown";
            RaspberryPiVoltageText.Text = "Voltage: N/A";
        }
    }

    private async Task RefreshRaspberryPiStatusPeriodicAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(2000, cancellationToken); // Refresh every 2 seconds
                await UpdateRaspberryPiStatusAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Ignore errors in the refresh thread
            }
        }
    }

    private void MenuList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MenuList.SelectedItem is not ListBoxItem item || item.Tag is not string tag)
        {
            return;
        }

        switch (tag)
        {
            case "chains":
                MainFrame.Navigate(App.Services.GetRequiredService<ChainsPage>());
                break;
            case "fo":
                MainFrame.Navigate(App.Services.GetRequiredService<FOInputPage>());
                break;
            case "dashboard":
                var dashboard = App.Services.GetRequiredService<DashboardWindow>();
                dashboard.Owner = this;
                dashboard.Show();
                break;
        }
    }
}
