namespace Yazaki.CommandeChaine.Desktop.Services;

public enum HarnessType
{
    Low,
    Medium,
    High
}

public sealed record HarnessItem(
    string Barcode,
    string Fo,
    HarnessType Type,
    int Quantity,
    int ProductionTimeInMinutes,
    DateOnly PlannedDate,
    bool IsUrgent,
    int EnqueueIndex);
