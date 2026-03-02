using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Yazaki.CommandeChaine.Desktop.Services;

namespace Yazaki.CommandeChaine.Desktop.Views;

public partial class DashboardWindow : Window
{
    private readonly CommandeChaineApiClient _apiClient;
    private readonly RealtimeClient _realtime;
    private readonly DispatcherTimer _refreshTimer;
    private List<ChainDto> _chains = new();
    private bool _isInitialized;

    public DashboardWindow(CommandeChaineApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _realtime = App.Services.GetRequiredService<RealtimeClient>();

        FromDate.SelectedDate = DateTime.Today.AddDays(-7);
        ToDate.SelectedDate = DateTime.Today;
        CauseSelect.ItemsSource = new[] { "(Toutes)", "Retard", "Panne", "Qualite", "Autre" };
        CauseSelect.SelectedIndex = 0;

        // Auto-refresh dashboard when a QualityEvent is received in real-time
        _realtime.QualityEventReceived += OnQualityEventReceived;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refreshTimer.Tick += async (_, _) =>
        {
            if (!_isInitialized) return;
            try { await RefreshAsync(); } catch { /* swallow */ }
        };

        Loaded += async (_, _) => await InitializeAsync();
        Closed += (_, _) =>
        {
            _realtime.QualityEventReceived -= OnQualityEventReceived;
            _refreshTimer.Stop();
        };
    }

    private void OnQualityEventReceived(QualityEventReceivedDto dto)
    {
        if (!_isInitialized) return;
        Dispatcher.InvokeAsync(async () =>
        {
            try { await RefreshAsync(); } catch { /* swallow */ }
        });
    }

    private async Task InitializeAsync()
    {
        try
        {
            _chains = await _apiClient.GetChainsAsync();

            // First item = "(Toutes)" placeholder for no filter
            var lineItems = new List<object> { "(Toutes les lignes)" };
            lineItems.AddRange(_chains);
            LineSelect.ItemsSource = lineItems;
            LineSelect.SelectedIndex = 0;

            PostSelect.ItemsSource = new[] { "(Tous les postes)" };
            PostSelect.SelectedIndex = 0;

            _isInitialized = true;
            await RefreshAsync();
            _refreshTimer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Dashboard init failed: {ex.Message}");
        }
    }

    private async void Refresh_OnClick(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private void LineSelect_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;

        if (LineSelect.SelectedItem is ChainDto chain)
        {
            var postItems = new List<object> { "(Tous les postes)" };
            postItems.AddRange(chain.Tables);
            PostSelect.ItemsSource = postItems;
            PostSelect.SelectedIndex = 0;
        }
        else
        {
            PostSelect.ItemsSource = new[] { "(Tous les postes)" };
            PostSelect.SelectedIndex = 0;
        }

        CauseSelect.SelectedIndex = 0;
    }

    private async Task RefreshAsync()
    {
        try
        {
            var from = FromDate.SelectedDate ?? DateTime.Today.AddDays(-7);
            var to = ToDate.SelectedDate ?? DateTime.Today;

            // Each filter is independent and optional
            Guid? chainId = LineSelect.SelectedItem is ChainDto chain ? chain.Id : null;
            Guid? tableId = PostSelect.SelectedItem is ChainTableDto table ? table.Id : null;
            string? cause = CauseSelect.SelectedItem is string c && c != "(Toutes)" ? c : null;

            var delaySeries = await _apiClient.GetDelaySeriesAsync(from, to, chainId, tableId, cause);
            var stops = await _apiClient.GetStopSummaryAsync(from, to, chainId, tableId, cause);
            var creditSeries = await _apiClient.GetCreditSeriesAsync(from, to, chainId, tableId);

            RenderDelayChart(delaySeries);
            RenderStopsChart(stops);
            RenderCreditChart(creditSeries);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Dashboard refresh failed: {ex.Message}");
        }
    }

    private void RenderDelayChart(IReadOnlyList<DelayPointDto> points)
    {
        DelayChart.Children.Clear();
        if (points.Count == 0)
        {
            var hint = new TextBlock
            {
                Text = "Aucun retard enregistre.\nLancez un OF et attendez qu'un poste depasse son temps cible.",
                FontSize = 13,
                Opacity = 0.5,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Width = Math.Max(200, DelayChart.ActualWidth - 40)
            };
            Canvas.SetLeft(hint, 20);
            Canvas.SetTop(hint, 80);
            DelayChart.Children.Add(hint);
            DelayLegend.Text = "Aucune donnee";
            return;
        }

        var width = Math.Max(1, DelayChart.ActualWidth);
        var height = Math.Max(1, DelayChart.Height);
        var max = Math.Max(1, points.Max(p => p.Value));

        var poly = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromRgb(198, 86, 61)),
            StrokeThickness = 2,
            Points = new PointCollection()
        };

        for (var i = 0; i < points.Count; i++)
        {
            var x = (points.Count == 1) ? width / 2 : (i * (width - 10) / (points.Count - 1)) + 5;
            var y = height - (points[i].Value / max * (height - 12)) - 6;
            poly.Points.Add(new Point(x, y));
        }

        DelayChart.Children.Add(poly);
        DelayLegend.Text = $"Max retard: {max:0.0}%";
    }

    private void RenderStopsChart(IReadOnlyList<StopSummaryDto> rows)
    {
        StopsChart.Children.Clear();
        if (rows.Count == 0)
        {
            var hint = new TextBlock
            {
                Text = "Aucun arret enregistre.\nOuvrez un poste et cliquez Stop pour enregistrer un arret.",
                FontSize = 13,
                Opacity = 0.5,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Width = Math.Max(200, StopsChart.ActualWidth - 40)
            };
            Canvas.SetLeft(hint, 20);
            Canvas.SetTop(hint, 80);
            StopsChart.Children.Add(hint);
            StopsLegend.Text = "Aucune donnee";
            return;
        }

        var width = Math.Max(1, StopsChart.ActualWidth);
        var height = Math.Max(1, StopsChart.Height);
        var max = Math.Max(1, rows.Max(r => r.Count));
        var barWidth = Math.Max(16, (width - 20) / rows.Count);

        for (var i = 0; i < rows.Count; i++)
        {
            var x = 10 + (i * barWidth);
            var h = (rows[i].Count / max) * (height - 20);
            var rect = new Rectangle
            {
                Width = barWidth - 8,
                Height = h,
                Fill = new SolidColorBrush(Color.FromRgb(78, 115, 157)),
                RadiusX = 2,
                RadiusY = 2
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, height - h - 6);
            StopsChart.Children.Add(rect);

            var label = new TextBlock
            {
                Text = rows[i].Cause,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(75, 67, 58))
            };
            Canvas.SetLeft(label, x);
            Canvas.SetTop(label, height - 16);
            StopsChart.Children.Add(label);
        }

        StopsLegend.Text = $"Total arrets: {rows.Sum(r => r.Count)}";
    }

    private void RenderCreditChart(IReadOnlyList<CreditPointDto> points)
    {
        CreditChart.Children.Clear();
        if (points.Count == 0)
        {
            var hint = new TextBlock
            {
                Text = "Aucun credit temps enregistre.\nLancez un OF pour initialiser les credits.",
                FontSize = 13,
                Opacity = 0.5,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Width = Math.Max(200, CreditChart.ActualWidth - 40)
            };
            Canvas.SetLeft(hint, 20);
            Canvas.SetTop(hint, 70);
            CreditChart.Children.Add(hint);
            CreditLegend.Text = "Aucune donnee";
            return;
        }

        var width = Math.Max(1, CreditChart.ActualWidth);
        var height = Math.Max(1, CreditChart.Height);
        var min = points.Min(p => p.Value);
        var max = points.Max(p => p.Value);
        if (Math.Abs(max - min) < 0.01)
        {
            max = min + 1;
        }

        var poly = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromRgb(46, 125, 50)),
            StrokeThickness = 2,
            Points = new PointCollection()
        };

        for (var i = 0; i < points.Count; i++)
        {
            var x = (points.Count == 1) ? width / 2 : (i * (width - 10) / (points.Count - 1)) + 5;
            var y = height - ((points[i].Value - min) / (max - min) * (height - 12)) - 6;
            poly.Points.Add(new Point(x, y));
        }

        CreditChart.Children.Add(poly);

        if (min < 0 && max > 0)
        {
            var zeroY = height - ((0 - min) / (max - min) * (height - 12)) - 6;
            var zeroLine = new Line
            {
                X1 = 4,
                X2 = width - 4,
                Y1 = zeroY,
                Y2 = zeroY,
                Stroke = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                StrokeThickness = 1
            };
            CreditChart.Children.Add(zeroLine);
        }

        CreditLegend.Text = $"Min: {min:0.0}%  Max: {max:0.0}%";
    }
}
