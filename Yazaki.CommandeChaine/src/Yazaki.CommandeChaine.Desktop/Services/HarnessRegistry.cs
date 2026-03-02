namespace Yazaki.CommandeChaine.Desktop.Services;

public sealed class HarnessRegistry
{
    private const int MaxItems = 200;
    private readonly Dictionary<string, HarnessItem> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _order = new();

    public void Register(HarnessItem item)
    {
        _items[item.Barcode] = item;
        _order.Enqueue(item.Barcode);
        Trim();
    }

    public bool TryGet(string barcode, out HarnessItem? item)
    {
        if (_items.TryGetValue(barcode, out var found))
        {
            item = found;
            return true;
        }

        item = null;
        return false;
    }

    private void Trim()
    {
        while (_order.Count > MaxItems)
        {
            var key = _order.Dequeue();
            _items.Remove(key);
        }
    }
}
