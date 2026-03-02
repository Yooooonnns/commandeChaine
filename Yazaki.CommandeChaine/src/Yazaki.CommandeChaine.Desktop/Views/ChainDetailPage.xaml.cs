using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Yazaki.CommandeChaine.Desktop.Services;
using Yazaki.CommandeChaine.Desktop.Rendering;

namespace Yazaki.CommandeChaine.Desktop.Views;

public partial class ChainDetailPage : Page
{
    private readonly RealtimeClient _realtime;
    private readonly DispatcherTimer _timer;
    private readonly ApiConfig _apiConfig;
    private readonly ActiveChainState _activeChain;
    private readonly HarnessRegistry _harnessRegistry;
    private readonly HarnessStandardsService _standards;
    private readonly CommandeChaineApiClient _apiClient;

    private ChainDto? _chain;
    private readonly Dictionary<Guid, string> _tableToBarcode = new();
    private readonly Dictionary<Guid, BoardDetailWindow> _boardWindows = new();
    private List<ChainTableDto> _tables = new();
    private double _progress;
    private double _lastProgress;
    private bool _finDeCourseInFlight;
    private bool _chainRunning;
    private DateTimeOffset _lastCreditPushUtc = DateTimeOffset.MinValue;
    private bool _creditUpdateInFlight;
    private readonly Dictionary<Guid, double> _tableCreditRatio = new();

    private double _currentSpeedRpm = 35.0;
    private double _targetSpeedRpm = 35.0;

    public ChainDetailPage()
    {
        InitializeComponent();
        _realtime = App.Services.GetRequiredService<RealtimeClient>();
        _apiConfig = App.Services.GetRequiredService<ApiConfig>();
        _activeChain = App.Services.GetRequiredService<ActiveChainState>();
        _harnessRegistry = App.Services.GetRequiredService<HarnessRegistry>();
        _standards = App.Services.GetRequiredService<HarnessStandardsService>();
        _apiClient = App.Services.GetRequiredService<CommandeChaineApiClient>();

        _realtime.ScanReceived += OnScanReceived;
        _realtime.SpeedRecommended += OnSpeedRecommended;
        _realtime.SpeedUpdated += OnSpeedUpdated;
        _realtime.SpeedFromRaspberry += OnSpeedFromRaspberry;
        _realtime.HarnessCompleted += OnHarnessCompleted;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += (_, _) =>
        {
            // Scale animation speed with RPM (simulation only).
            _currentSpeedRpm += (_targetSpeedRpm - _currentSpeedRpm) * 0.12;
            var rpm = Math.Clamp(_currentSpeedRpm, 5.0, 120.0);
            var chainScale = GetChainScaleFactor();
            var incrementPerTick = (rpm * 0.0001) / chainScale; // scale speed by chain size
            _progress = (_progress + incrementPerTick) % 1.0;
            if (_progress < _lastProgress)
            {
                _ = TriggerFinDeCourseAsync();
            }

            _lastProgress = _progress;
            UpdateBoardProgress();
            _ = MaybeUpdateTimeCreditAsync();
            RedrawTwin();
        };

        Loaded += (_, _) => SetChainRunning(true);
        Unloaded += (_, _) => SetChainRunning(false);
        SizeChanged += (_, _) => RedrawTwin();
    }

    public async void SetChain(ChainDto chain)
    {
        _chain = chain;
        _activeChain.Set(chain.Id);
        TitleText.Text = $"{chain.Name} - Digital Twin";
        _tableToBarcode.Clear();
        CloseBoardWindows();
        _tables = chain.Tables.OrderBy(t => t.Index).ToList();
        TablesList.ItemsSource = _tables;
        ScansList.Items.Clear();
        RecommendedSpeedText.Text = "-";
        StandardsWarning.Visibility = Visibility.Collapsed;
        _progress = 0;
        _lastProgress = 0;
        _currentSpeedRpm = 35.0;
        _targetSpeedRpm = 35.0;
        _tableCreditRatio.Clear();
        _lastCreditPushUtc = DateTimeOffset.MinValue;
        _creditUpdateInFlight = false;
        RedrawTwin();

        SetChainRunning(true);

        await LoadFoStatusAsync();

        try
        {
            await _realtime.EnsureConnectedAsync(_apiConfig.BaseUrl);
            await _realtime.JoinChainAsync(chain.Id);
        }
        catch
        {
            // Keep UI responsive even if realtime is down.
        }
    }

    private async Task LoadFoStatusAsync()
    {
        if (_chain is null)
        {
            return;
        }

        var status = await _apiClient.GetFoStatusAsync(_chain.Id);
        ApplyFoStatus(status);
    }

    private void ApplyFoStatus(FoStatusResponse? status)
    {
        if (status is null)
        {
            CurrentFoText.Text = "OF: -";
            FoProgressText.Text = "Progress: -";
            _ = RefreshOpenBoardHarnessesAsync(clear: true);
            return;
        }

        CurrentFoText.Text = $"OF: {status.FoName}";
        FoProgressText.Text = $"Progress: {status.CompletedCount}/{status.HarnessCount}";
        // Convert cycles/min to meaningful display - show CT in minutes instead
        var ctMinutes = status.RecommendedSpeedRpm > 0 ? 1.0 / status.RecommendedSpeedRpm : 0;
        RecommendedSpeedText.Text = ctMinutes > 0 ? $"CT: {ctMinutes:0.0} min" : "CT: -";
        _targetSpeedRpm = status.RecommendedSpeedRpm;
        _ = RefreshOpenBoardHarnessesAsync();
    }

    private void OnScanReceived(ScanReceivedDto dto)
    {
        if (_chain is null || dto.ChainId != _chain.Id)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            if (dto.TableId is Guid tableId)
            {
                _tableToBarcode[tableId] = dto.Barcode;
            }

            var scanLine = BuildScanLine(dto);
            ScansList.Items.Insert(0, scanLine);
            while (ScansList.Items.Count > 30)
            {
                ScansList.Items.RemoveAt(ScansList.Items.Count - 1);
            }

            RedrawTwin();

            // Refresh open board windows so delay tracking uses current harness data
            _ = RefreshOpenBoardHarnessesAsync();
        });
    }

    private void OnSpeedRecommended(SpeedRecommendedDto dto)
    {
        if (_chain is null || dto.ChainId != _chain.Id)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            RecommendedSpeedText.Text = $"{dto.RecommendedSpeedRpm:0.0} RPM (conf {dto.Confidence:P0})";

            // Auto-apply in simulation so speed visibly changes.
            _targetSpeedRpm = dto.RecommendedSpeedRpm;
        });
    }

    private void OnSpeedUpdated(SpeedUpdatedDto dto)
    {
        if (_chain is null || dto.ChainId != _chain.Id)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            var ctDisplay = dto.CycleTimeMinutes.HasValue ? $"CT: {dto.CycleTimeMinutes:0.0} min ({dto.HarnessesOnChain} sur chaine)" : "CT: -";
            RecommendedSpeedText.Text = ctDisplay;
            _targetSpeedRpm = dto.RecommendedSpeedRpm;
        });
    }

    private void OnSpeedFromRaspberry(SpeedFromRaspberryDto dto)
    {
        if (_chain is null || dto.ChainId != _chain.Id)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            // Display speed from Raspberry (or simulated) with CT info
            var ctMin = dto.CtSeconds / 60.0;
            RecommendedSpeedText.Text = $"Vitesse: {dto.SpeedRpm:0.0000} RPM | CT: {ctMin:0.0} min ({dto.HarnessesOnChain} sur chaine)";
            _targetSpeedRpm = dto.SpeedRpm;
        });
    }

    private void OnHarnessCompleted(HarnessCompletedDto dto)
    {
        if (_chain is null || dto.ChainId != _chain.Id)
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            FoProgressText.Text = $"Progress: {dto.CompletedCount}/{dto.TotalCount}";
            _ = RefreshOpenBoardHarnessesAsync();
        });
    }

    private void RedrawTwin()
    {
        if (_chain is null)
        {
            return;
        }

        RacetrackTwinRenderer.Render(TwinCanvas, _chain, _tableToBarcode, _progress, OpenBoardWindow, _tableCreditRatio);
    }

    private void OpenBoard_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ChainTableDto table })
        {
            return;
        }

        OpenBoardWindow(table);
    }

    private void ValidateCables_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ChainTableDto table })
        {
            return;
        }

        OpenCableValidationWindow(table);
    }

    private void OpenBoardWindow(ChainTableDto table)
    {
        if (_boardWindows.TryGetValue(table.Id, out var existing))
        {
            existing.Activate();
            return;
        }

        if (_chain is null)
        {
            return;
        }

        var window = new BoardDetailWindow(table, _chain.Id, _apiClient);
        window.Owner = Window.GetWindow(this);
        window.TableRenamed += OnTableRenamed;
        window.Closed += (_, _) => _boardWindows.Remove(table.Id);
        _boardWindows[table.Id] = window;
        window.Show();
        window.UpdateProgress(_progress);
        _ = UpdateBoardHarnessAsync(window, table);
    }

    private void OpenCableValidationWindow(ChainTableDto table)
    {
        if (_chain is null)
        {
            return;
        }

        var window = App.Services.GetRequiredService<CableValidationWindow>();
        
        // We need to set the properties through the constructor
        // Create a new instance with the required parameters
        var actualWindow = new CableValidationWindow(_apiClient, table.Id, table.Name);
        actualWindow.Owner = Window.GetWindow(this);
        actualWindow.Show();
    }

    private void OnTableRenamed(Guid tableId, string name)
    {
        if (_chain is null)
        {
            return;
        }

        var updated = _tables.Select(t => t.Id == tableId ? t with { Name = name } : t).ToList();
        _tables = updated;
        _chain = _chain with { Tables = updated };
        TablesList.ItemsSource = null;
        TablesList.ItemsSource = _tables;
    }

    private async Task UpdateBoardHarnessAsync(BoardDetailWindow window, ChainTableDto table)
    {
        if (_chain is null)
        {
            return;
        }

        var current = await _apiClient.GetFoCurrentForBoardAsync(_chain.Id, table.Index);
        window.SetHarnessReference(current?.Reference ?? "-");
        await window.RefreshWorkMetricsAsync();
    }

    private async Task RefreshOpenBoardHarnessesAsync(bool clear = false)
    {
        if (_chain is null || _boardWindows.Count == 0)
        {
            return;
        }

        if (clear)
        {
            foreach (var window in _boardWindows.Values)
            {
                window.SetHarnessReference("-");
            }

            return;
        }

        var tasks = _boardWindows.Select(async pair =>
        {
            var table = _chain.Tables.FirstOrDefault(t => t.Id == pair.Key);
            if (table is null)
            {
                return;
            }

            var current = await _apiClient.GetFoCurrentForBoardAsync(_chain.Id, table.Index);
            pair.Value.SetHarnessReference(current?.Reference ?? "-");
            await pair.Value.RefreshWorkMetricsAsync();
        });

        await Task.WhenAll(tasks);
    }

    private void UpdateBoardProgress()
    {
        if (_boardWindows.Count == 0)
        {
            return;
        }

        foreach (var window in _boardWindows.Values)
        {
            window.UpdateProgress(_progress);
        }
    }

    private async Task MaybeUpdateTimeCreditAsync()
    {
        if (_chain is null || _tables.Count == 0 || _creditUpdateInFlight || !_chainRunning)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now - _lastCreditPushUtc < TimeSpan.FromSeconds(1))
        {
            return;
        }

        _lastCreditPushUtc = now;
        _creditUpdateInFlight = true;

        try
        {
            var updates = BuildCreditUpdates();
            if (updates.Count == 0)
            {
                return;
            }

            var credits = await _apiClient.UpdateTimeCreditAsync(_chain.Id, updates);
            foreach (var credit in credits)
            {
                _tableCreditRatio[credit.TableId] = credit.CreditRatio;
                if (_boardWindows.TryGetValue(credit.TableId, out var window))
                {
                    window.UpdateTimeCredit(credit);
                }
            }
        }
        catch
        {
            // Keep UI responsive if API is down.
        }
        finally
        {
            _creditUpdateInFlight = false;
        }
    }

    private List<TableCreditUpdateDto> BuildCreditUpdates()
    {
        var ordered = _tables.OrderBy(t => t.Index).ToList();
        var n = ordered.Count;
        if (n == 0)
        {
            return new List<TableCreditUpdateDto>();
        }

        var updates = new List<TableCreditUpdateDto>(n);
        for (var i = 0; i < n; i++)
        {
            var table = ordered[i];
            var progress = (_progress + (i / (double)n)) % 1.0;
            updates.Add(new TableCreditUpdateDto(table.Id, progress));
        }

        return updates;
    }

    private void StartChain_OnClick(object sender, RoutedEventArgs e)
    {
        SetChainRunning(true);
    }

    private void StopChain_OnClick(object sender, RoutedEventArgs e)
    {
        SetChainRunning(false);
    }

    private async void CancelFo_OnClick(object sender, RoutedEventArgs e)
    {
        if (_chain is null)
        {
            return;
        }

        try
        {
            await _apiClient.CancelFoAsync(_chain.Id);
            ApplyFoStatus(null);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cancel failed: {ex.Message}");
        }
    }

    private void SetChainRunning(bool running)
    {
        _chainRunning = running;
        if (_chainRunning)
        {
            _timer.Start();
            ChainStateText.Text = "Etat: Marche";
        }
        else
        {
            _timer.Stop();
            ChainStateText.Text = "Etat: Arret";
        }
    }

    private void CloseBoardWindows()
    {
        foreach (var window in _boardWindows.Values.ToList())
        {
            window.Close();
        }

        _boardWindows.Clear();
    }

    private async Task TriggerFinDeCourseAsync(bool showError = false)
    {
        if (_finDeCourseInFlight || _chain is null)
        {
            return;
        }

        _finDeCourseInFlight = true;
        try
        {
            var status = await _apiClient.CompleteNextFoAsync(_chain.Id);
            ApplyFoStatus(status);
        }
        catch (Exception ex)
        {
            if (showError)
            {
                MessageBox.Show($"Fin de course failed: {ex.Message}");
            }
        }
        finally
        {
            _finDeCourseInFlight = false;
        }
    }

    private double GetChainScaleFactor()
    {
        var tableCount = Math.Max(1, _chain?.Tables.Count ?? 1);
        return Math.Clamp(tableCount / 6.0, 1.5, 12.0);
    }

    private string BuildScanLine(ScanReceivedDto dto)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (_harnessRegistry.TryGet(dto.Barcode, out var item) && item is not null)
        {
            var mismatch = _standards.IsMismatch(item.PlannedDate, today);
            UpdateStandardsWarning(item, today, mismatch);

            var standardLabel = _standards.GetRangeLabel(item.PlannedDate, item.Type);
            var urgentLabel = item.IsUrgent ? "URGENT" : "NORMAL";
            var mismatchLabel = mismatch ? $"STD {item.PlannedDate:yyyy-MM-dd} != {today:yyyy-MM-dd}" : "STD OK";

            return $"[{dto.ScannedAtUtc:HH:mm:ss}] {item.Fo} | {item.Barcode} | {item.Type} | Qte {item.Quantity} | {standardLabel} | {urgentLabel} | {mismatchLabel}";
        }

        StandardsWarning.Visibility = Visibility.Collapsed;
        return $"[{dto.ScannedAtUtc:HH:mm:ss}] {dto.Barcode} ({dto.ScannerId})";
    }

    private void UpdateStandardsWarning(HarnessItem item, DateOnly today, bool mismatch)
    {
        if (!mismatch)
        {
            StandardsWarning.Visibility = Visibility.Collapsed;
            return;
        }

        var label = _standards.GetRangeLabel(item.PlannedDate, item.Type);
        StandardsWarningText.Text = $"Attention: standards du {item.PlannedDate:yyyy-MM-dd} (ex: {label}) au lieu de {today:yyyy-MM-dd}.";
        StandardsWarning.Visibility = Visibility.Visible;
    }
}
