using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Yazaki.CommandeChaine.Desktop.Services;

namespace Yazaki.CommandeChaine.Desktop.Views;

public partial class FOInputPage : Page
{
    private readonly FOImportService _importService;
    private readonly CommandeChaineApiClient _apiClient;
    private readonly ObservableCollection<FOHarnessRow> _rows = new();
    private ICollectionView? _view;
    private List<ChainDto> _chains = new();

    public FOInputPage()
    {
        InitializeComponent();
        _importService = App.Services.GetRequiredService<FOImportService>();
        _apiClient = App.Services.GetRequiredService<CommandeChaineApiClient>();

        FoGrid.ItemsSource = _rows;
        _view = CollectionViewSource.GetDefaultView(_rows);
        ApplySort();

        Loaded += async (_, _) => await LoadChainsAsync();
    }

    private async Task LoadChainsAsync()
    {
        _chains = await _apiClient.GetChainsAsync();
        ChainSelect.ItemsSource = _chains;
        if (_chains.Count > 0)
        {
            ChainSelect.SelectedIndex = 0;
        }
    }

    private void LoadExcel_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Excel files (*.xlsx)|*.xlsx",
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var items = _importService.LoadFromExcel(dialog.FileName);
            LoadRows(items);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Import failed: {ex.Message}");
        }
    }

    private void GenerateSample_OnClick(object sender, RoutedEventArgs e)
    {
        var items = new List<FOHarnessRow>();
        for (var i = 1; i <= 300; i++)
        {
            items.Add(new FOHarnessRow
            {
                OrderIndex = i,
                Reference = $"HR-{i:000}",
                ProductionTimeMinutes = Random.Shared.Next(35, 120),
                IsUrgent = i % 40 == 0,
                IsLate = i % 25 == 0
            });
        }

        LoadRows(items);
    }

    private void Clear_OnClick(object sender, RoutedEventArgs e)
    {
        _rows.Clear();
    }

    private void MarkUrgent_OnClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in SelectedRows())
        {
            row.IsUrgent = true;
        }

        _view?.Refresh();
    }

    private void UnmarkUrgent_OnClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in SelectedRows())
        {
            row.IsUrgent = false;
        }

        _view?.Refresh();
    }

    private void MarkLate_OnClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in SelectedRows())
        {
            row.IsLate = true;
        }

        _view?.Refresh();
    }

    private void UnmarkLate_OnClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in SelectedRows())
        {
            row.IsLate = false;
        }

        _view?.Refresh();
    }

    private IEnumerable<FOHarnessRow> SelectedRows()
    {
        return FoGrid.SelectedItems.Cast<FOHarnessRow>();
    }

    private void LoadRows(IEnumerable<FOHarnessRow> items)
    {
        _rows.Clear();
        var index = 1;
        foreach (var item in items)
        {
            item.OrderIndex = index++;
            _rows.Add(item);
        }

        _view?.Refresh();
    }

    private void ApplySort()
    {
        if (_view is null)
        {
            return;
        }

        if (_view is ListCollectionView listView)
        {
            listView.CustomSort = new FoPriorityComparer();
        }
    }

    private async void AssignToChain_OnClick(object sender, RoutedEventArgs e)
    {
        var foName = FoNameText.Text?.Trim();
        if (string.IsNullOrWhiteSpace(foName))
        {
            MessageBox.Show("FO name is required.");
            return;
        }

        if (_rows.Count == 0)
        {
            MessageBox.Show("FO list is empty.");
            return;
        }

        if (ChainSelect.SelectedItem is not ChainDto chain)
        {
            MessageBox.Show("Select a chain.");
            return;
        }

        try
        {
            var required = chain.Tables.Count + 2;
            if (_rows.Count < required)
            {
                MessageBox.Show($"Chain {chain.Name} requires at least {required} harnesses for {chain.Tables.Count} boards.");
                return;
            }

            var ordered = GetOrderedHarnesses().ToList();
            var response = await _apiClient.AssignFoToChainAsync(chain.Id, foName, ordered);
            RecommendationText.Text = $"Assigned {response.HarnessCount} harnesses to {chain.Name}.";
            MessageBox.Show($"{chain.Name}: {response.RecommendedSpeedRpm:0.0} RPM ({response.HarnessCount})", "FO assigned");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Assign failed: {ex.Message}");
        }
    }

    private IEnumerable<FOHarnessRow> GetOrderedHarnesses()
    {
        var list = _rows.ToList();
        list.Sort(new FoPriorityComparer());
        return list;
    }

}

internal sealed class FoPriorityComparer : IComparer, IComparer<FOHarnessRow>
{
    public int Compare(object? x, object? y)
    {
        return Compare(x as FOHarnessRow, y as FOHarnessRow);
    }

    public int Compare(FOHarnessRow? left, FOHarnessRow? right)
    {
        if (left is null || right is null)
        {
            return 0;
        }

        var rankLeft = Rank(left);
        var rankRight = Rank(right);
        if (rankLeft != rankRight)
        {
            return rankLeft.CompareTo(rankRight);
        }

        return left.OrderIndex.CompareTo(right.OrderIndex);
    }

    private static int Rank(FOHarnessRow row)
    {
        if (row.IsUrgent && row.IsLate)
        {
            return 0;
        }

        if (row.IsUrgent)
        {
            return 1;
        }

        if (row.IsLate)
        {
            return 3;
        }

        return 2;
    }
}
