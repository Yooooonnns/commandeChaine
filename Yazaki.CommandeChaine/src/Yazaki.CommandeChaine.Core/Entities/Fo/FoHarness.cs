namespace Yazaki.CommandeChaine.Core.Entities.Fo;

public sealed class FoHarness
{
    public Guid Id { get; set; }
    public Guid FoBatchId { get; set; }
    public FoBatch? FoBatch { get; set; }

    public string Reference { get; set; } = string.Empty;
    public int ProductionTimeMinutes { get; set; }
    public bool IsUrgent { get; set; }
    public bool IsLate { get; set; }
    public int OrderIndex { get; set; }
    public bool IsCompleted { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>
    /// True when harness is physically on the chain (between entry and exit points).
    /// </summary>
    public bool IsOnChain { get; set; }

    /// <summary>
    /// When the harness entered the chain.
    /// </summary>
    public DateTimeOffset? EnteredChainAtUtc { get; set; }

    /// <summary>
    /// Position on the chain (0 = just entered, increases as it moves toward exit).
    /// </summary>
    public int ChainPosition { get; set; }
}
