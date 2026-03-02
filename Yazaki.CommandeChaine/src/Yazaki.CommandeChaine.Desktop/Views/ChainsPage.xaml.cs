using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Yazaki.CommandeChaine.Desktop.Services;

namespace Yazaki.CommandeChaine.Desktop.Views;

public partial class ChainsPage : Page
{
    private readonly CommandeChaineApiClient _api;

    public ChainsPage()
    {
        InitializeComponent();
        _api = App.Services.GetRequiredService<CommandeChaineApiClient>();
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var chains = await _api.GetChainsAsync();
        ChainsItems.ItemsSource = chains.Select(x => new ChainAdminRow(x.Id, x.Name, x.Tables.Count, x)).ToList();
    }

    private async void Refresh_OnClick(object sender, RoutedEventArgs e)
    {
        await LoadAsync();
    }

    private static int ParseTableCount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return int.TryParse(text.Trim(), out var value) ? Math.Max(0, value) : 0;
    }

    private async Task<ChainDto?> CreateAsync(bool openAfter)
    {
        var name = NewChainName.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Nom de chaîne requis.");
            return null;
        }

        var tableCount = ParseTableCount(NewTableCount.Text);

        try
        {
            var created = await _api.CreateChainAsync(name, tableCount);
            NewChainName.Text = string.Empty;
            NewTableCount.Text = string.Empty;
            await LoadAsync();

            if (openAfter)
            {
                OpenChain(created);
            }

            return created;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Création échouée: {ex.Message}");
            return null;
        }
    }

    private async void CreateChain_OnClick(object sender, RoutedEventArgs e)
    {
        await CreateAsync(openAfter: false);
    }

    private async void CreateAndOpen_OnClick(object sender, RoutedEventArgs e)
    {
        await CreateAsync(openAfter: true);
    }

    private void OpenChain(ChainDto chain)
    {
        var page = App.Services.GetRequiredService<ChainDetailPage>();
        page.SetChain(chain);
        NavigationService?.Navigate(page);
    }

    private void OpenChain_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ChainDto chain })
        {
            return;
        }

        OpenChain(chain);
    }

    private async void DeleteChain_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid chainId })
        {
            return;
        }

        var result = MessageBox.Show("Supprimer cette chaîne ?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _api.DeleteChainAsync(chainId);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Suppression échouée: {ex.Message}");
        }
    }

    private async void Rename_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        if (sender is not TextBox { Tag: Guid chainId } tb)
        {
            return;
        }

        var name = tb.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            await _api.RenameChainAsync(chainId, name);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Renommage échoué: {ex.Message}");
        }
    }

    private async void TableCount_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        if (sender is not TextBox { Tag: Guid chainId } tb)
        {
            return;
        }

        var count = ParseTableCount(tb.Text);
        try
        {
            await _api.UpdateTableCountAsync(chainId, count);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Mise à jour échouée: {ex.Message}");
        }
    }
}

public sealed record ChainAdminRow(Guid Id, string Name, int TableCount, ChainDto Chain);
