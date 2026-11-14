using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Application.Channel;
using Dfo.Server.Application.Pvp;
using Dfo.Server.Data.Channel;

namespace Dfo.Server.Application.Tests.Pvp;

/// <summary>Plan amendment A1: duel channels without entitlements and duel content that does not load fail startup.</summary>
public sealed class PvpStartupVerificationTests
{
    private static readonly ChannelServerInfo Ordinary = new(1, "#1", 100, 0, "127.0.0.1", 10011);
    private static readonly ChannelServerInfo Ranked = new(2, "#2", 100, 0, "127.0.0.1", 10024, 24);
    private static readonly ChannelServerInfo Free = new(3, "#3", 100, 0, "127.0.0.1", 10013, 13);

    [Fact]
    public async Task Every_duel_channel_is_asked_for_its_entitlements_and_the_content_is_loaded()
    {
        var grades = new Grades();
        var maps = new Maps();

        Assert.Equal(2, await Verification([Ordinary, Ranked, Free], grades, maps).VerifyAsync(default));
        Assert.True(grades.Loaded);
        Assert.True(maps.Loaded);
    }

    [Fact]
    public async Task A_free_duel_channel_named_by_id_with_no_environment_passes()
    {
        var named = new ChannelServerInfo(9, "#9", 100, 0, "127.0.0.1", 10090);
        var options = ChannelEntryOptions.Legacy with { FreeDuelChannelId = 9, FreeDuelEnabled = true };

        Assert.Equal(1, await Verification([Ordinary, named], new Grades(), new Maps(), options).VerifyAsync(default));
    }

    [Fact]
    public async Task A_duel_channel_without_entitlements_fails()
    {
        var verification = new PvpStartupVerification(
            new ChannelContentRuleService(new Catalog([Ordinary, Ranked]), new NoScript()),
            new UnknownDuelEnvironment(), new ChannelListenerOptions([10011, 10024], AllowAdvertisedPortFallback: false),
            new PvpSettlementRuleService(new Grades(), PvpSettlementRuleServiceTests.Days), new PvpRoomRuleService(new Maps()));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => verification.VerifyAsync(default).AsTask());
    }

    [Fact]
    public async Task A_grade_table_that_does_not_load_fails()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Verification([Ordinary], new Grades(broken: true), new Maps()).VerifyAsync(default).AsTask());
    }

    [Fact]
    public async Task A_map_order_that_does_not_load_fails()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Verification([Ordinary], new Grades(), new Maps(broken: true)).VerifyAsync(default).AsTask());
    }

    private static PvpStartupVerification Verification(
        IReadOnlyList<ChannelServerInfo> channels, Grades grades, Maps maps, ChannelEntryOptions? options = null)
    {
        var content = new ChannelContentRuleService(new Catalog(channels), new NoScript());
        var listeners = new ChannelListenerOptions([.. channels.Select(channel => channel.Port)], AllowAdvertisedPortFallback: false);
        return new PvpStartupVerification(content,
            new ChannelEntryDomainService(content, options ?? ChannelEntryOptions.Legacy, listeners, new NoDeployment()),
            listeners, new PvpSettlementRuleService(grades, PvpSettlementRuleServiceTests.Days), new PvpRoomRuleService(maps));
    }

    private sealed class Grades(bool broken = false) : IPvpGradeDefinitionSource
    {
        public bool Loaded { get; private set; }

        public PvpGradeCatalog Load()
        {
            if (broken) throw new InvalidDataException("etc/pvp_ref.etc lacks [pvp grade point].");
            Loaded = true;
            return new([new PvpGradeStep(0, 0, 100, 100, 0), new PvpGradeStep(1, 100, 9999999, 100, -50)], 1,
                TimeSpan.FromSeconds(180), TimeSpan.FromSeconds(120));
        }
    }

    private sealed class Maps(bool broken = false) : IPvpMapDefinitionSource
    {
        public bool Loaded { get; private set; }

        public PvpMapCatalog Load()
        {
            if (broken) throw new InvalidDataException("etc/pvpmapbasicdata.etc lacks [pvp map order].");
            Loaded = true;
            return new([0, 101]);
        }
    }

    /// <summary>A channel entry service that classifies every channel as a duel channel of an environment nobody registered.</summary>
    private sealed class UnknownDuelEnvironment : IChannelEntryDomainService
    {
        public ValueTask<ChannelEntryIdentity> ResolveAsync(int listenerPort, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ChannelEntryIdentity(listenerPort - 10000, listenerPort, ChannelEntryKind.Pvp, 99));

        public ChannelAdmissionRefusal? Refuse(ChannelEntryIdentity channel, int characterLevel) => null;

        public ValueTask<ChannelLoginEntry> ResolveLoginAsync(int listenerPort, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class Catalog(IReadOnlyList<ChannelServerInfo> channels) : IChannelCatalog
    {
        public ValueTask<IReadOnlyList<ChannelServerInfo>> GetChannelsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(channels);
    }

    private sealed class NoScript : IChannelScriptSource
    {
        public string Version => throw new NotSupportedException();
        public ValueTask<byte[]> ReadAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NoDeployment : IChannelLoginDeploymentSource
    {
        public byte ReadDefaultEnvironment() => throw new NotSupportedException();
        public Dfo.Server.Data.Configuration.PublicGameLoginEndpoints ReadEndpoints() => throw new NotSupportedException();
    }
}
