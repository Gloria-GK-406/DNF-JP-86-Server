using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Protocol.Game.GuildInfo;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.GuildManagement;

internal sealed record GuildProfileProjection(GuildInfoPanel Own, GuildInfoPanel? Ally)
{
    public static async ValueTask<GuildProfileProjection> ReadAsync(IGuildOverviewDomainService overviews, CharacterId actor, CancellationToken ct)
    {
        var own = await GuildInfoPanel.ReadAsync(overviews, actor, ct).ConfigureAwait(false);
        var ally = own.Ally is { } peer
            ? await GuildInfoPanel.ReadAsync(overviews, peer.Leader, ct).ConfigureAwait(false)
            : null;
        return new(own, ally);
    }

    public async ValueTask PublishAsync(ISelectedSessionDomainService sessions, ICharacterVisibleSessionDomainService visibility,
        ICommandProjections projections, CancellationToken ct, string? renamedTo = null, CharacterId? omitNoticeFor = null)
    {
        var members = Own.Members.Select(member => member.Character).ToArray();
        await projections.ProjectAsync(new NotificationAudience.TheseCharacters(members), Own, ct).ConfigureAwait(false);
        if (renamedTo is not null)
        {
            var notified = omitNoticeFor is { } actor ? members.Where(member => member != actor).ToArray() : members;
            await projections.ProjectAsync(new NotificationAudience.TheseCharacters(notified),
                new GuildRenamed(renamedTo), ct).ConfigureAwait(false);
        }
        await RefreshAppearanceAsync(Own, sessions, visibility, projections, ct).ConfigureAwait(false);
        if (Ally is { Guild: not null })
            await projections.ProjectAsync(new NotificationAudience.TheseCharacters(
                Ally.Members.Select(member => member.Character).ToArray()), Ally, ct).ConfigureAwait(false);
    }

    internal static async ValueTask RefreshAppearanceAsync(GuildInfoPanel panel, ISelectedSessionDomainService sessions,
        ICharacterVisibleSessionDomainService visibility, ICommandProjections projections, CancellationToken ct,
        bool refreshMedalAura = false)
    {
        foreach (var member in panel.Members)
            if (sessions.TryGetInWorld(member.Character, out var online))
                await projections.ProjectAsync(new NotificationAudience.TheseSessions(visibility.Resolve(online)),
                    new CharacterUserStateRefresh(online, RefreshGuildMedalAura: refreshMedalAura), ct).ConfigureAwait(false);
    }
}
