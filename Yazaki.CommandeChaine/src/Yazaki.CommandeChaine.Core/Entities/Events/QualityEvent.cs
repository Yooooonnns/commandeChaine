namespace Yazaki.CommandeChaine.Core.Entities.Events;

public sealed class QualityEvent
{
    public Guid Id { get; set; }

    public Guid ChainId { get; set; }
    public Guid? TableId { get; set; }

    public QualityEventKind Kind { get; set; }
    public QualityEventCause? Cause { get; set; }
    public double? DelayPercent { get; set; }
    public double? DurationMinutes { get; set; }
    public string? Note { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public enum QualityEventKind
{
    Ok = 0,
    Defect = 1,
    Rework = 2,
    Stop = 3
}

public enum QualityEventCause
{
    Retard = 0,
    Panne = 1,
    Qualite = 2,
    Autre = 3
}
