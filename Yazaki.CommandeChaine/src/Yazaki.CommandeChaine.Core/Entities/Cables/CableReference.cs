namespace Yazaki.CommandeChaine.Core.Entities.Cables;

public sealed class CableReference
{
    public Guid Id { get; set; }
    public string Reference { get; set; } = string.Empty; // barcode content

    public Guid CategoryId { get; set; }
    public CableCategory? Category { get; set; }
}
