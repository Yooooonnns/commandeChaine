using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Yazaki.CommandeChaine.Desktop.Services;

namespace Yazaki.CommandeChaine.Desktop.Views;

public partial class CableValidationWindow : Window
{
    private readonly CommandeChaineApiClient _apiClient;
    private readonly Guid _chainTableId;
    private List<PendingCableDto> _cables = new();
    private PendingCableDto? _selectedCable;

    public CableValidationWindow(CommandeChaineApiClient apiClient, Guid chainTableId, string boardName)
    {
        InitializeComponent();
        
        _apiClient = apiClient;
        _chainTableId = chainTableId;
        
        BoardNameText.Text = $"Validation - {boardName}";

        Loaded += async (_, _) => await RefreshCablesAsync();
    }

    private async Task RefreshCablesAsync()
    {
        try
        {
            _cables = await _apiClient.GetPendingCablesAsync(_chainTableId);
            
            if (_cables == null || _cables.Count == 0)
            {
                CablesListBox.ItemsSource = new List<PendingCableDto>();
                CurrentCableText.Text = "Aucun câble en attente";
                CableStatusText.Text = "Tous les câbles ont été validés";
                ConfirmCableButton.IsEnabled = false;
                return;
            }

            CablesListBox.ItemsSource = null;
            CablesListBox.ItemsSource = _cables;

            // Check if any cable is already started
            var started = _cables.FirstOrDefault(c => c.ValidationStatus == 1); // Started
            if (started != null)
            {
                CurrentCableText.Text = started.Reference;
                CableStatusText.Text = "En cours de traitement...";
                ConfirmCableButton.Content = "Valider & Charger suivant (Entrer)";
                ConfirmCableButton.IsEnabled = true;
            }
            else
            {
                CurrentCableText.Text = "Aucun câble en cours";
                CableStatusText.Text = "Cliquez sur un câble pour commencer";
                ConfirmCableButton.IsEnabled = false;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors du chargement: {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            CablesListBox.ItemsSource = new List<PendingCableDto>();
        }
    }

    private void Cable_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CablesListBox.SelectedItem is not PendingCableDto cable)
        {
            return;
        }

        _selectedCable = cable;
    }

    private async void ConfirmCable_OnClick(object sender, RoutedEventArgs e)
    {
        await ValidateSelectedCableAsync();
    }

    private async Task ValidateSelectedCableAsync()
    {
        if (_selectedCable is null)
        {
            MessageBox.Show("Veuillez sélectionner un câble.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ConfirmCableButton.IsEnabled = false;
        CablesListBox.IsEnabled = false;
        
        try
        {
            System.Diagnostics.Debug.WriteLine($"Validating cable: {_selectedCable.Id} on table: {_chainTableId}");
            
            var result = await _apiClient.ValidateCableAsync(_selectedCable.Id, _chainTableId);
            
            System.Diagnostics.Debug.WriteLine($"Validation response: {result?.CableReference} - Status: {result?.Status}");
            
            if (result != null)
            {
                MessageBox.Show($"Câble {result.CableReference} validé avec succès!", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                
                _selectedCable = null;
                CablesListBox.SelectedItem = null;
                
                await RefreshCablesAsync();
            }
            else
            {
                MessageBox.Show("Erreur: Réponse vide du serveur.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (HttpRequestException hre)
        {
            System.Diagnostics.Debug.WriteLine($"HTTP Error: {hre.Message}");
            MessageBox.Show($"Erreur réseau: {hre.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Exception: {ex}");
            MessageBox.Show($"Erreur lors de la validation: {ex.Message}\n\n{ex.StackTrace}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ConfirmCableButton.IsEnabled = true;
            CablesListBox.IsEnabled = true;
        }
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        
        // Focus on the listbox to enable keyboard shortcuts
        CablesListBox.Focus();
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == System.Windows.Input.Key.Return)
        {
            e.Handled = true;
            _ = ValidateSelectedCableAsync();
        }
    }
}
