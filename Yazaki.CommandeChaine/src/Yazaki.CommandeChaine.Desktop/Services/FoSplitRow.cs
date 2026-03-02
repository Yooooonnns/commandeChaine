using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Yazaki.CommandeChaine.Desktop.Services;

public sealed class FoSplitRow : INotifyPropertyChanged
{
    private Guid _chainId;
    private int _count;

    public Guid ChainId
    {
        get => _chainId;
        set => SetField(ref _chainId, value);
    }

    public int Count
    {
        get => _count;
        set => SetField(ref _count, value);
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
