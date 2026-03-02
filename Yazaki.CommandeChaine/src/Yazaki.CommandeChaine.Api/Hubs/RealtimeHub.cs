using Microsoft.AspNetCore.SignalR;

namespace Yazaki.CommandeChaine.Api.Hubs;

public sealed class RealtimeHub : Hub
{
    public static string ChainGroup(Guid chainId) => $"chain:{chainId}";

    public override Task OnConnectedAsync()
        => base.OnConnectedAsync();

    public async Task JoinChain(Guid chainId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, ChainGroup(chainId));

    public async Task LeaveChain(Guid chainId)
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, ChainGroup(chainId));
}
