using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Administration;

internal sealed record GmPlayingCharacter(int AccountId, GmChannelView? Channel, GmPositionView Position);
internal sealed record GmRuntimeSample(HashSet<int> Accounts, IReadOnlyDictionary<int, GmPlayingCharacter> Characters,
    IReadOnlyList<GmChannelView> Channels, DateTimeOffset SampledAt);

internal sealed class GmPlayerRuntimeQuery(IAuthenticatedGamePresenceQuery online, ISelectedSessionSnapshotQuery snapshots,
    ISelectedSessionDomainService selected, IOnlineCharacterRuntimeDomainService onlineCharacters,
    IChannelDirectoryDomainService channels, IChannelEntryPolicy entry, TimeProvider clock)
{
    public async ValueTask<GmRuntimeSample> SampleAsync(CancellationToken token)
    {
        var sampledAt = clock.GetUtcNow();
        var accounts = online.SnapshotAccounts().Select(id => id.Value).ToHashSet();
        var catalog = (await channels.ChannelsAsync(token)).Select(channel => new GmChannelView(channel.ChannelId, channel.Name)).ToArray();
        var playing = new Dictionary<int, GmPlayingCharacter>();
        foreach (var session in snapshots.Snapshot())
        {
            token.ThrowIfCancellationRequested();
            if (!selected.IsInWorld(session)) continue;
            var position = onlineCharacters.PositionOf(session);
            var resolved = await entry.ResolveAsync(session.ListenerPort, token);
            if (!selected.IsInWorld(session)) continue;
            accounts.Add(session.AccountId.Value);
            playing[session.CharacterId.Value] = new(session.AccountId.Value,
                catalog.FirstOrDefault(channel => channel.ChannelId == resolved.ChannelId),
                Position(position is null ? "unavailable" : "current", position));
        }
        return new(accounts, playing, catalog, sampledAt);
    }

    internal static GmPositionView Position(string source, Dfo.Server.Data.Characters.CharacterPositionState? value) =>
        new(source, value?.TownId, value?.AreaId, value?.X, value?.Y, value?.Direction);
}
