namespace Yazaki.CommandeChaine.Desktop.Services;

public sealed class HarnessStandardsService
{
    private readonly Dictionary<DateOnly, Dictionary<HarnessType, (int Min, int Max)>> _byDate;

    public HarnessStandardsService()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        _byDate = new Dictionary<DateOnly, Dictionary<HarnessType, (int Min, int Max)>>
        {
            [today] = new Dictionary<HarnessType, (int Min, int Max)>
            {
                [HarnessType.Low] = (43, 57),
                [HarnessType.Medium] = (58, 91),
                [HarnessType.High] = (92, 120)
            },
            [today.AddDays(1)] = new Dictionary<HarnessType, (int Min, int Max)>
            {
                [HarnessType.Low] = (52, 70),
                [HarnessType.Medium] = (71, 95),
                [HarnessType.High] = (96, 130)
            },
            [today.AddDays(-1)] = new Dictionary<HarnessType, (int Min, int Max)>
            {
                [HarnessType.Low] = (40, 55),
                [HarnessType.Medium] = (56, 80),
                [HarnessType.High] = (81, 110)
            }
        };
    }

    public (int Min, int Max) GetRange(DateOnly date, HarnessType type)
    {
        if (_byDate.TryGetValue(date, out var map) && map.TryGetValue(type, out var range))
        {
            return range;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        return _byDate[today][type];
    }

    public bool IsMismatch(DateOnly plannedDate, DateOnly today)
        => plannedDate != today;

    public string GetRangeLabel(DateOnly date, HarnessType type)
    {
        var range = GetRange(date, type);
        return $"{type} {range.Min}-{range.Max} min";
    }
}
