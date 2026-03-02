namespace Yazaki.CommandeChaine.Core.Entities.Events;

public sealed class BarcodeScanEvent
{
    public Guid Id { get; set; }

    public string Barcode { get; set; } = string.Empty;
    public string? ScannerId { get; set; }
    public string? FoName { get; set; }
    public string? HarnessType { get; set; }
    public int? ProductionTimeMinutes { get; set; }
    public bool? IsUrgent { get; set; }

    public Guid? ChainId { get; set; }
    public Guid? TableId { get; set; }

    public DateTimeOffset ScannedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
