namespace Yazaki.CommandeChaine.Desktop.Services;

public sealed class HarnessQueueService
{
    private readonly HarnessStandardsService _standards;
    private readonly List<HarnessItem> _queue = new();
    private int _nextIndex = 1;

    public HarnessQueueService(HarnessStandardsService standards)
    {
        _standards = standards;
    }

    public HarnessItem? DequeueNext(DateOnly today)
    {
        if (_queue.Count == 0)
        {
            Seed(today);
        }

        if (_queue.Count == 0)
        {
            return null;
        }

        var urgent = _queue
            .Where(x => x.IsUrgent)
            .OrderByDescending(PriorityScore)
            .ThenBy(x => x.EnqueueIndex)
            .FirstOrDefault();

        var item = urgent ?? _queue.OrderBy(x => x.EnqueueIndex).First();
        _queue.Remove(item);
        return item;
    }

    private static int PriorityScore(HarnessItem item)
    {
        var typeWeight = item.Type switch
        {
            HarnessType.High => 3,
            HarnessType.Medium => 2,
            _ => 1
        };

        return (typeWeight * 100000) + item.Quantity;
    }

    private void Seed(DateOnly today)
    {
        _queue.Clear();

        AddHarness(today.AddDays(-1), HarnessType.Low, 12, urgent: false);
        AddHarness(today, HarnessType.Medium, 20, urgent: false);
        AddHarness(today, HarnessType.High, 8, urgent: true);
        AddHarness(today.AddDays(-1), HarnessType.Medium, 16, urgent: false);
        AddHarness(today.AddDays(1), HarnessType.Low, 10, urgent: false);
        AddHarness(today, HarnessType.Low, 14, urgent: true);
        AddHarness(today.AddDays(1), HarnessType.High, 6, urgent: false);
        AddHarness(today.AddDays(-1), HarnessType.High, 18, urgent: false);
        AddHarness(today, HarnessType.Medium, 24, urgent: false);
        AddHarness(today, HarnessType.Low, 30, urgent: false);
        AddHarness(today.AddDays(1), HarnessType.Medium, 11, urgent: true);
        AddHarness(today, HarnessType.High, 9, urgent: false);
    }

    private void AddHarness(DateOnly plannedDate, HarnessType type, int quantity, bool urgent)
    {
        var range = _standards.GetRange(plannedDate, type);
        var productionTime = Random.Shared.Next(range.Min, range.Max + 1);

        var fo = $"FO-{plannedDate:yyyyMMdd}-{_nextIndex:0000}";
        var barcode = $"{fo}-{type.ToString().ToUpperInvariant()}-{quantity:00}";

        _queue.Add(new HarnessItem(
            Barcode: barcode,
            Fo: fo,
            Type: type,
            Quantity: quantity,
            ProductionTimeInMinutes: productionTime,
            PlannedDate: plannedDate,
            IsUrgent: urgent,
            EnqueueIndex: _nextIndex));

        _nextIndex++;
    }
}
