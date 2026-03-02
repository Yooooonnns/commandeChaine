using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using Yazaki.CommandeChaine.Desktop.Services;

namespace Yazaki.CommandeChaine.Desktop.Views;

public partial class BoardDetailWindow : Window
{
    private const int WorkBandSegments = 20;
    private readonly CommandeChaineApiClient _apiClient;
    private ChainTableDto _table;
    private readonly Guid _chainId;
    private readonly ObservableCollection<string> _stopHistory = new();
    private bool _isStopped;
    private DateTimeOffset? _stopStart;
    private readonly List<Rectangle> _workBandBlocks = new();
    private bool _delayReported;
    private string _currentHarnessRef = "-";
    private double? _lastCreditRatio;

    // Target tracking
    private int _targetProductionMinutes;
    private int _maxProductionMinutes;
    private bool _hasWorkTarget;
    private double _targetRatio; // lit portion of the bar

    public event Action<Guid, string>? TableRenamed;

    public BoardDetailWindow(ChainTableDto table, Guid chainId, CommandeChaineApiClient apiClient)
    {
        InitializeComponent();
        _table = table;
        _chainId = chainId;
        _apiClient = apiClient;
        StopsList.ItemsSource = _stopHistory;
        InitializeWorkBand();
        StopCauseSelect.ItemsSource = new[] { "Retard", "Panne", "Qualite", "Autre" };
        StopCauseSelect.SelectedIndex = 0;

        TitleText.Text = $"Poste {table.Index}";
        BoardNameText.Text = table.Name;
        HarnessRefText.Text = "-";
        ProgressText.Text = "0%";
        StopStateText.Text = "Etat: Marche";
        WorkBandText.Text = "Cible: -";
        WorkBandLateText.Text = string.Empty;
        CreditText.Text = "Credit temps: -";
        CreditTrendText.Text = "Tendance: -";
    }

    public void SetHarnessReference(string reference)
    {
        var next = string.IsNullOrWhiteSpace(reference) ? "-" : reference;
        if (!string.Equals(_currentHarnessRef, next, StringComparison.Ordinal))
        {
            _currentHarnessRef = next;
            _delayReported = false;
            _lastCreditRatio = null;
        }

        HarnessRefText.Text = next;
    }


    public void UpdateProgress(double progress01)
    {
        var pct = Math.Clamp(progress01, 0, 1) * 100.0;
        HarnessProgress.Value = pct;
        ProgressText.Text = $"{pct:0}%";

        if (!_hasWorkTarget || _maxProductionMinutes <= 0)
        {
            return;
        }

        // Move arrow according to progression bar position
        var arrowRatio = Math.Clamp(progress01, 0, 1);
        var canvasWidth = Math.Max(1, ArrowCanvas.ActualWidth);
        var arrowX = arrowRatio * (canvasWidth - 12);
        System.Windows.Controls.Canvas.SetLeft(ArrowIndicator, arrowX);

        if (_targetRatio <= 0)
        {
            WorkBandLateText.Text = string.Empty;
            return;
        }

   
        var isLate = arrowRatio > _targetRatio;
        ArrowIndicator.Foreground = new SolidColorBrush(
            isLate ? Color.FromRgb(176, 43, 38) : Color.FromRgb(35, 95, 62));

        if (!isLate)
        {
            var remainPct = (_targetRatio - arrowRatio) * 100;
            WorkBandLateText.Text = $"Reste: {remainPct:0}%";
            WorkBandLateText.Foreground = new SolidColorBrush(Color.FromRgb(35, 95, 62));
        }
        else
        {
            var latePct = (arrowRatio - _targetRatio) / _targetRatio * 100;
            WorkBandLateText.Text = $"Retard: +{latePct:0}%";
            WorkBandLateText.Foreground = new SolidColorBrush(Color.FromRgb(176, 43, 38));

            if (!_delayReported)
            {
                _delayReported = true;
                _ = ReportDelayAsync(latePct);
            }
        }
    }

    public void UpdateTimeCredit(TableCreditDto credit)
    {
        if (credit.TableId != _table.Id)
        {
            return;
        }

        var minutes = credit.CreditMinutes;
        var pct = credit.CreditRatio * 100.0;
        var signMin = minutes >= 0 ? "+" : "-";
        var signPct = pct >= 0 ? "+" : "-";
        var absMin = Math.Abs(minutes);
        var absPct = Math.Abs(pct);

        CreditText.Text = $"Credit temps: {signMin}{absMin:0.0} min ({signPct}{absPct:0}%)";
        CreditText.Foreground = new SolidColorBrush(
            minutes >= 0 ? Color.FromRgb(35, 95, 62) : Color.FromRgb(176, 43, 38));

        string trendLabel;
        var trendColor = Color.FromRgb(107, 114, 128);
        if (_lastCreditRatio.HasValue)
        {
            var delta = credit.CreditRatio - _lastCreditRatio.Value;
            if (delta > 0.01)
            {
                trendLabel = "Tendance: +";
                trendColor = Color.FromRgb(35, 95, 62);
            }
            else if (delta < -0.01)
            {
                trendLabel = "Tendance: -";
                trendColor = Color.FromRgb(176, 43, 38);
            }
            else
            {
                trendLabel = "Tendance: Stable";
            }
        }
        else
        {
            trendLabel = "Tendance: -";
        }

        CreditTrendText.Text = trendLabel;
        CreditTrendText.Foreground = new SolidColorBrush(trendColor);
        _lastCreditRatio = credit.CreditRatio;
    }

    public async Task RefreshWorkMetricsAsync()
    {
        try
        {
            var metrics = await _apiClient.GetFoBoardMetricsAsync(_chainId, _table.Index);
            if (metrics is null || metrics.MaxProductionTimeMinutes <= 0)
            {
                _hasWorkTarget = false;
                _targetProductionMinutes = 0;
                _maxProductionMinutes = 0;
                _targetRatio = 0;
                WorkBandText.Text = "Cible: -";
                WorkBandLateText.Text = string.Empty;
                UpdateWorkBandBlocks(0);
                ArrowIndicator.Visibility = Visibility.Collapsed;
                return;
            }

            _targetProductionMinutes = metrics.CurrentProductionTimeMinutes;
            _maxProductionMinutes = metrics.MaxProductionTimeMinutes;
            _hasWorkTarget = true;

            // The bar ratio = current harness time / max OF time (static target)
            _targetRatio = Math.Clamp((double)_targetProductionMinutes / _maxProductionMinutes, 0, 1);
            _targetRatio = ApplyOvertimeSimulation(_targetRatio);
            WorkBandText.Text = $"Cible: {_targetProductionMinutes} min / {_maxProductionMinutes} min ({_targetRatio * 100:0}%)";
            UpdateWorkBandBlocks(_targetRatio);
            ArrowIndicator.Visibility = Visibility.Visible;
        }
        catch
        {
            // Keep UI responsive
        }
    }

    private void InitializeWorkBand()
    {
        WorkBandGrid.Children.Clear();
        _workBandBlocks.Clear();
        for (var i = 0; i < WorkBandSegments; i++)
        {
            var rect = new Rectangle
            {
                Height = 14,
                RadiusX = 2,
                RadiusY = 2,
                Margin = new Thickness(1, 0, 1, 0),
                Fill = new SolidColorBrush(Color.FromRgb(224, 224, 224))
            };
            _workBandBlocks.Add(rect);
            WorkBandGrid.Children.Add(rect);
        }
    }

    /// <summary>
    /// Lights up segments representing the current harness's target time
    /// as a proportion of the max OF time. This bar is STATIC — it only
    /// changes when the harness changes, not as time passes.
    /// </summary>
    private void UpdateWorkBandBlocks(double targetRatio)
    {
        var lit = (int)Math.Round(targetRatio * WorkBandSegments);
        var color = Color.FromRgb(66, 133, 244); // blue = target zone
        var off = Color.FromRgb(224, 224, 224);

        for (var i = 0; i < _workBandBlocks.Count; i++)
        {
            _workBandBlocks[i].Fill = new SolidColorBrush(i < lit ? color : off);
        }
    }

    private async Task ReportDelayAsync(double delayPercent)
    {
        if (_chainId == Guid.Empty || _table.Id == Guid.Empty)
        {
            return;
        }

        try
        {
            await _apiClient.IngestQualityEventAsync(new QualityIngestRequest(
                _chainId,
                _table.Id,
                QualityEventKind.Stop,
                "Retard",
                delayPercent,
                null,
                null,
                DateTimeOffset.UtcNow));
        }
        catch
        {
            // Ignore failures to keep UI responsive.
        }
    }

    private double ApplyOvertimeSimulation(double targetRatio)
    {
        if (targetRatio < 0.98)
        {
            return targetRatio;
        }

        var seed = HashCode.Combine(_currentHarnessRef, _table.Id);
        var mod = Math.Abs(seed % 4);
        var factor = mod switch
        {
            0 => 1.0,
            1 => 0.95,
            2 => 0.9,
            _ => 0.85
        };

        return Math.Clamp(targetRatio * factor, 0, 1);
    }

    private async void Rename_OnClick(object sender, RoutedEventArgs e)
    {
        var name = BoardNameText.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Nom requis.");
            return;
        }

        try
        {
            var updated = await _apiClient.RenameChainTableAsync(_table.Id, name);
            _table = updated;
            TitleText.Text = $"Poste {_table.Index}";
            TableRenamed?.Invoke(_table.Id, _table.Name);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Renommage echoue: {ex.Message}");
        }
    }

    private void Stop_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isStopped)
        {
            return;
        }

        _isStopped = true;
        _stopStart = DateTimeOffset.Now;
        StopStateText.Text = "Etat: Arret";
        StopButton.IsEnabled = false;
        ResumeButton.IsEnabled = true;
        _stopHistory.Insert(0, $"Stop a {_stopStart:HH:mm:ss}");
        _ = ReportStopAsync(started: true, durationMinutes: null);
    }

    private void Resume_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_isStopped || _stopStart is null)
        {
            return;
        }

        var end = DateTimeOffset.Now;
        var duration = end - _stopStart.Value;
        _stopHistory.Insert(0, $"Reprise a {end:HH:mm:ss} (duree {duration.ToString(@"mm\:ss")})");
        _isStopped = false;
        _stopStart = null;
        StopStateText.Text = "Etat: Marche";
        StopButton.IsEnabled = true;
        ResumeButton.IsEnabled = false;
        _ = ReportStopAsync(started: false, durationMinutes: duration.TotalMinutes);
    }

    private async Task ReportStopAsync(bool started, double? durationMinutes)
    {
        if (_chainId == Guid.Empty || _table.Id == Guid.Empty)
        {
            return;
        }

        var cause = StopCauseSelect.SelectedItem as string ?? "Autre";
        var note = started ? "Stop" : "Reprise";
        try
        {
            await _apiClient.IngestQualityEventAsync(new QualityIngestRequest(
                _chainId,
                _table.Id,
                QualityEventKind.Stop,
                cause,
                null,
                durationMinutes,
                note,
                DateTimeOffset.UtcNow));
        }
        catch
        {
            // Ignore failures to keep UI responsive.
        }
    }
}
