namespace Yazaki.CommandeChaine.Core.Entities.Chains;

public sealed class ChainTable
{
    public Guid Id { get; set; }

    public Guid ChainId { get; set; }
    public Chain? Chain { get; set; }

    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;

    public double TimeCreditMinutes { get; set; }
    public double TimeCreditRatio { get; set; }
    public double TimeCreditTargetRatio { get; set; }
    public DateTimeOffset? TimeCreditUpdatedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
