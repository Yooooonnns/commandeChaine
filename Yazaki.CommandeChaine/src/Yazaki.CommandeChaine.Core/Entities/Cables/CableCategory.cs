namespace Yazaki.CommandeChaine.Core.Entities.Cables;

public sealed class CableCategory
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty; // "charge", "moyen", "leger"
    public string DisplayName { get; set; } = string.Empty;
}
