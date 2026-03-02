namespace Yazaki.CommandeChaine.Core.Entities.Chains;

public sealed class Chain
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
    public int WorkerCount { get; set; } = 1;
    public double ProductivityFactor { get; set; } = 1.0;
    public double PitchDistanceMeters { get; set; } = 1.0;
    public double BalancingTuningK { get; set; } = 0.7;
    public List<ChainTable> Tables { get; set; } = new();
}
