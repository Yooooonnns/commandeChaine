namespace Yazaki.CommandeChaine.Core.Entities.Events;

public sealed class TimeCreditHistory
{
    public Guid Id { get; set; }
    public Guid ChainId { get; set; }
    public Guid TableId { get; set; }

    public double ProgressRatio { get; set; }
    public double TargetRatio { get; set; }
    public double CreditRatio { get; set; }
    public double CreditMinutes { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
