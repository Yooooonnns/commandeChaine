using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Yazaki.CommandeChaine.Desktop.Services;

public sealed class FOHarnessRow : INotifyPropertyChanged
{
    private string _reference = string.Empty;
    private int _productionTimeMinutes;
    private bool _isUrgent;
    private bool _isLate;

    public int OrderIndex { get; set; }

    public string Reference
    {
        get => _reference;
        set => SetField(ref _reference, value);
    }

    public int ProductionTimeMinutes
    {
        get => _productionTimeMinutes;
        set => SetField(ref _productionTimeMinutes, value);
    }

    public bool IsUrgent
    {
        get => _isUrgent;
        set => SetField(ref _isUrgent, value);
    }

    public bool IsLate
    {
        get => _isLate;
        set => SetField(ref _isLate, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
