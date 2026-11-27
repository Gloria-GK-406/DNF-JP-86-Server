using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Application.Guilds;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.GuildInfo;
using Dfo.Server.Protocol.Game.GuildManagement;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.GuildManagement;

public sealed class GuildProfileProjectionTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("Renamed", false)]
    [InlineData("Renamed", true)]
    public async Task Projects_each_guilds_own_panel_and_limits_private_and_visible_updates(string? name, bool omitActor)
    {
        var actor = SelectedSessionIdentity.Create(new(1), new(11), new("actor"), 1, 10010);
        var observer = SelectedSessionIdentity.Create(new(4), new(41), new("observer"), 1, 10010);
        var own = new Guild(new(1), "Renamed", "", 1, Mark: 5);
        var ally = new Guild(new(2), "Ally", "", 1);
        GuildMember[] members = [new(own.Id, actor.CharacterId, true, DateTimeOffset.UnixEpoch),
            new(own.Id, new(12), false, DateTimeOffset.UnixEpoch)];
        GuildMember[] allies = [new(ally.Id, new(21), true, DateTimeOffset.UnixEpoch),
            new(ally.Id, new(22), false, DateTimeOffset.UnixEpoch)];
        var query = TestRequired.Absent<IGuildMembershipQueryDomainService>((method, args) => method.Name switch
        {
            nameof(IGuildMembershipQueryDomainService.FindMembershipAsync) => ValueTask.FromResult<GuildMembership?>(
                (CharacterId)args![0]! == actor.CharacterId ? new(own, members[0]) : new(ally, allies[0])),
            nameof(IGuildMembershipQueryDomainService.ReadRosterAsync) => ValueTask.FromResult<IReadOnlyList<GuildMember>>(
                (CharacterId)args![0]! == actor.CharacterId ? members : allies),
            _ => null,
        });
        var alliances = TestRequired.Absent<IGuildAllianceDomainService>((method, args) => method.Name switch
        {
            nameof(IGuildAllianceDomainService.FindAllyAsync) => ValueTask.FromResult<GuildAlliancePeer?>(
                (GuildId)args![0]! == own.Id ? new(ally.Id, ally.Name, ally.Mark, allies[0].Character)
                : new(own.Id, own.Name, own.Mark, actor.CharacterId)),
            _ => null,
        });
        var overviews = new GuildOverviewDomainService(query, TestRequired.Absent<IGuildAttendanceDomainService>(),
            TestRequired.Absent<IGuildMonthlyContributionDomainService>(), TestRequired.Absent<IGuildBenefitDomainService>(), alliances);
        var profile = await GuildProfileProjection.ReadAsync(overviews, actor.CharacterId, default);
        var sessions = TestRequired.Absent<ISelectedSessionDomainService>((method, args) =>
        {
            Assert.Equal(nameof(ISelectedSessionDomainService.TryGetInWorld), method.Name);
            if ((CharacterId)args![0]! != actor.CharacterId) return false;
            args[1] = actor;
            return true;
        });
        var visibility = TestRequired.Absent<ICharacterVisibleSessionDomainService>((_, _) =>
            new[] { actor, observer });
        var calls = new List<(NotificationAudience Audience, object Notice)>();
        var projections = TestRequired.Absent<ICommandProjections>((_, args) =>
        {
            calls.Add(((NotificationAudience)args![0]!, args[1]!));
            return ValueTask.FromResult(new NotificationDelivery([]));
        });

        await profile.PublishAsync(sessions, visibility, projections, default, name, omitActor ? actor.CharacterId : null);

        Assert.Equal(name is null ? 3 : 4, calls.Count);
        Assert.Same(profile.Own, calls[0].Notice);
        Assert.Equal(new[] { new CharacterId(11), new CharacterId(12) }, Characters(calls[0].Audience));
        if (name is not null)
        {
            Assert.Equal(name, Assert.IsType<GuildRenamed>(calls[1].Notice).Name);
            Assert.Equal(omitActor ? [new CharacterId(12)] : new[] { actor.CharacterId, new CharacterId(12) },
                Characters(calls[1].Audience));
        }
        Assert.Equal(actor, Assert.IsType<CharacterUserStateRefresh>(calls[^2].Notice).Owner);
        Assert.Equal(new[] { actor, observer }, Assert.IsType<NotificationAudience.TheseSessions>(calls[^2].Audience).Sessions);
        var peerPanel = Assert.IsType<GuildInfoPanel>(calls[^1].Notice);
        Assert.Equal(ally.Id, peerPanel.Guild!.Id);
        Assert.Equal(own.Name, peerPanel.Ally!.Name);
        Assert.Equal(own.Mark, peerPanel.Ally.Mark);
        Assert.Equal(new[] { new CharacterId(21), new CharacterId(22) }, Characters(calls[^1].Audience));
    }

    private static IReadOnlyList<CharacterId> Characters(NotificationAudience audience) =>
        Assert.IsType<NotificationAudience.TheseCharacters>(audience).CharacterIds;
}
