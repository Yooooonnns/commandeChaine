namespace Yazaki.CommandeChaine.Desktop.Services;

public sealed class ActiveChainState
{
    public Guid? CurrentChainId { get; private set; }

    public void Set(Guid? chainId)
    {
        CurrentChainId = chainId;
    }
}
