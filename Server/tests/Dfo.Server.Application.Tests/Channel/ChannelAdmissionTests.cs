using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Application.Channel;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.Channel;

[Trait("Category", "Unit")]
public sealed class ChannelAdmissionTests
{
    [Theory]
    [InlineData(50, (byte)24, ChannelEntryKind.TraditionalDuel, SkillGroup.Duel)]
    [InlineData(60, (byte)8, ChannelEntryKind.MatchmakingDuel, SkillGroup.Duel)]
    [InlineData(161, (byte)24, ChannelEntryKind.TraditionalDuel, SkillGroup.Duel)]
    [InlineData(162, (byte)8, ChannelEntryKind.MatchmakingDuel, SkillGroup.Duel)]
    [InlineData(68, (byte)13, ChannelEntryKind.FreeDuel, SkillGroup.Duel)]
    [InlineData(70, (byte)13, ChannelEntryKind.FreeDuel, SkillGroup.Duel)]
    [InlineData(200, (byte)23, ChannelEntryKind.Raid, SkillGroup.Pve)]
    [InlineData(82, (byte)8, ChannelEntryKind.TotalMatchDuel, SkillGroup.Duel)]
    [InlineData(83, (byte)8, ChannelEntryKind.MatchmakingDuel, SkillGroup.Duel)]
    public async Task Script_channel_environment_controls_login_and_skill_group(
        int id, byte environment, ChannelEntryKind kind, SkillGroup group)
    {
        var policy = new ChannelEntryDomainService(new ChannelContentRuleService(new StubChannelCatalog(
            [new ChannelServerInfo(id, $"#ch.{id}", 100, 0, "127.0.0.1", 10000 + id, environment)]),
            new NoChannelScript()), ChannelEntryOptions.Legacy with { TotalMatchmakingChannelIds = [82] },
            new ChannelListenerOptions([10000 + id], AllowAdvertisedPortFallback: false), new UnusedDeployment(), new RecordingQuestLogs());
        var channel = await policy.ResolveAsync(10000 + id, default);
        Assert.Equal(environment, channel.LoginEnvironment);
        Assert.Equal(kind, channel.Kind);
        Assert.Equal(environment is 8 or 13 or 24, channel.IsPvp);
        Assert.Equal(group, channel.CharacterSkillGroup);
    }

    [Fact]
    public async Task A_listener_resolves_to_the_channel_configured_on_that_port()
    {
        var policy = Policy();

        var restricted = await policy.ResolveAsync(10161, CancellationToken.None);
        var general = await policy.ResolveAsync(10011, CancellationToken.None);

        Assert.Equal(100, restricted.ChannelId);
        Assert.Equal(ChannelEntryKind.SaintHorn, restricted.Kind);
        Assert.Equal(11, general.ChannelId);
        Assert.Equal(ChannelEntryKind.General, general.Kind);
    }

    [Fact]
    public async Task Public_advertisement_does_not_replace_the_independent_listener_mapping()
    {
        var policy = new ChannelEntryDomainService(
            new ChannelContentRuleService(
                new StubChannelCatalog(
                [
                    new ChannelServerInfo(
                        11, "#ch.11", 500, 0, "198.51.100.24", 37102),
                ]),
                new NoChannelScript()),
            ChannelEntryOptions.Legacy,
            new ChannelListenerOptions([10011], AllowAdvertisedPortFallback: false), new UnusedDeployment(), new RecordingQuestLogs());

        var mapped = await policy.ResolveAsync(10011, CancellationToken.None);
        var advertisementIsNotABind = await policy.ResolveAsync(37102, CancellationToken.None);

        Assert.Equal(11, mapped.ChannelId);
        Assert.Equal(0, advertisementIsNotABind.ChannelId);
    }

    [Fact]
    public async Task Disabled_launcher_matches_advertised_ports_even_when_listener_order_differs()
    {
        var policy = new ChannelEntryDomainService(
            new ChannelContentRuleService(
                new StubChannelCatalog(
                [
                    new ChannelServerInfo(
                        100, "#ch.100", 500, 0, "127.0.0.1", 10161),
                    new ChannelServerInfo(
                        11, "#ch.11", 500, 0, "127.0.0.1", 10011),
                ]),
                new NoChannelScript()),
            ChannelEntryOptions.Legacy,
            new ChannelListenerOptions(
                [10011, 10161],
                AllowAdvertisedPortFallback: true), new UnusedDeployment(), new RecordingQuestLogs());

        var general = await policy.ResolveAsync(10011, CancellationToken.None);
        var restricted = await policy.ResolveAsync(10161, CancellationToken.None);

        Assert.Equal(11, general.ChannelId);
        Assert.Equal(ChannelEntryKind.General, general.Kind);
        Assert.Null(await policy.RefuseAsync(general, new(1), new(1), 1, default));
        Assert.Equal(100, restricted.ChannelId);
        Assert.Equal(ChannelEntryKind.SaintHorn, restricted.Kind);
        Assert.NotNull(await policy.RefuseAsync(restricted, new(1), new(1), 1, default));
    }

    /// <remarks>
    /// Refusing an unlisted listener would make a deployment that simply has not enumerated
    /// its channels unplayable, which is a worse failure than treating it as ordinary.
    /// </remarks>
    [Fact]
    public async Task A_listener_no_channel_claims_resolves_to_an_ordinary_channel()
    {
        var identity = await Policy().ResolveAsync(19999, CancellationToken.None);

        Assert.Equal(ChannelEntryKind.General, identity.Kind);
        Assert.Equal(19999, identity.ListenerPort);
    }

    [Fact]
    public async Task The_duelling_channel_is_ordinary_while_it_is_switched_off()
    {
        var disabled = await Policy(freeDuelEnabled: false).ResolveAsync(10068, CancellationToken.None);
        var enabled = await Policy(freeDuelEnabled: true).ResolveAsync(10068, CancellationToken.None);

        Assert.Equal(ChannelEntryKind.General, disabled.Kind);
        Assert.False(disabled.IsFreeDuel);
        Assert.Equal(ChannelEntryKind.FreeDuel, enabled.Kind);
        Assert.True(enabled.IsFreeDuel);
    }

    [Fact]
    public async Task A_character_below_the_minimum_level_is_refused_the_restricted_channel()
    {
        var policy = Policy();
        var channel = await policy.ResolveAsync(10161, CancellationToken.None);

        var refusal = await policy.RefuseAsync(channel, new(1), new(1), 69, default);

        Assert.NotNull(refusal);
        Assert.Equal(0xFD, refusal!.ErrorCode);
        Assert.NotEmpty(refusal.Message);
    }

    [Theory]
    [InlineData(70)]
    [InlineData(86)]
    public async Task A_character_at_or_above_the_minimum_level_is_admitted(int level)
    {
        var policy = Policy();
        var channel = await policy.ResolveAsync(10161, CancellationToken.None);

        Assert.Null(await policy.RefuseAsync(channel, new(1), new(1), level, default));
    }

    [Theory]
    [InlineData(12828, true)]
    [InlineData(12829, false)]
    public async Task A_raid_channel_requires_the_selected_character_to_clear_the_access_quest(
        int clearedQuestId, bool refused)
    {
        var quests = new RecordingQuestLogs();
        quests.Cleared[1] = [new(clearedQuestId, 1)];
        quests.Cleared[2] = [new(12829, 1)];
        var policy = Policy(quests: quests);
        var raid = new ChannelEntryIdentity(200, 10200, ChannelEntryKind.Raid);

        var refusal = await policy.RefuseAsync(raid, new(1), new(1), 86, default);

        Assert.Equal(refused, refusal is not null);
    }

    [Fact]
    public async Task An_ordinary_channel_refuses_nobody_on_level()
    {
        var policy = Policy();
        var channel = await policy.ResolveAsync(10011, CancellationToken.None);

        Assert.Null(await policy.RefuseAsync(channel, new(1), new(1), 1, default));
    }

    [Fact]
    public async Task Special_channels_place_a_character_transiently()
    {
        var policy = Policy(freeDuelEnabled: true);

        var restricted = await policy.ResolveAsync(10161, CancellationToken.None);
        var general = await policy.ResolveAsync(10011, CancellationToken.None);
        var duel = await policy.ResolveAsync(10068, CancellationToken.None);

        Assert.True(restricted.IsTransient);
        Assert.False(general.IsTransient);
        Assert.True(duel.IsTransient);
    }

    [Fact]
    public async Task The_third_listener_maps_to_saint_horn_and_uses_the_configured_minimum()
    {
        var policy = new ChannelEntryDomainService(new ChannelContentRuleService(new StubChannelCatalog(
            [new(10, "#ch.10", 500, 0, "127.0.0.1", 40010),
             new(11, "#ch.11", 500, 0, "127.0.0.1", 40011),
             new(100, "#ch.100", 500, 0, "127.0.0.1", 40100)]), new NoChannelScript()),
            ChannelEntryOptions.Legacy with { SaintHornMinimumLevel = 75 },
            new ChannelListenerOptions([10010, 10011, 10100], AllowAdvertisedPortFallback: false), new UnusedDeployment(), new RecordingQuestLogs());
        var channel = await policy.ResolveAsync(10100, default);
        Assert.Equal(100, channel.ChannelId);
        Assert.Equal(ChannelEntryKind.SaintHorn, channel.Kind);
        Assert.True(channel.IsTransient);
        Assert.Contains("75", (await policy.RefuseAsync(channel, new(1), new(1), 74, default))!.Message);
        Assert.Null(await policy.RefuseAsync(channel, new(1), new(1), 75, default));
        Assert.Equal(11, (await policy.ResolveAsync(10011, default)).ChannelId);
    }

    private static ChannelEntryDomainService Policy(bool freeDuelEnabled = false, RecordingQuestLogs? quests = null) =>
        new(
            new ChannelContentRuleService(
                new StubChannelCatalog(
                [
                    new ChannelServerInfo(11, "#ch.11", 500, 0, "127.0.0.1", 10011),
                    new ChannelServerInfo(100, "#ch.100", 500, 0, "127.0.0.1", 10161),
                    new ChannelServerInfo(68, "#ch.68", 500, 0, "127.0.0.1", 10068),
                ]),
                new NoChannelScript()),
            ChannelEntryOptions.Legacy with { FreeDuelEnabled = freeDuelEnabled },
            new ChannelListenerOptions([], AllowAdvertisedPortFallback: true), new UnusedDeployment(), quests ?? new RecordingQuestLogs());

    private sealed class UnusedDeployment : Dfo.Server.Abstractions.Configuration.IChannelLoginDeploymentSource
    {
        public byte ReadDefaultEnvironment() => throw new InvalidOperationException("Ordinary admission must not read login configuration.");
        public Dfo.Server.Data.Configuration.PublicGameLoginEndpoints ReadEndpoints() => throw new InvalidOperationException("Ordinary admission must not read login configuration.");
    }

    private sealed class StubChannelCatalog(IReadOnlyList<ChannelServerInfo> channels) : IChannelCatalog
    {
        public ValueTask<IReadOnlyList<ChannelServerInfo>> GetChannelsAsync(
            CancellationToken cancellationToken) => ValueTask.FromResult(channels);
    }

    /// <summary>Admission reads the catalogue; no script is downloaded on this path.</summary>
    private sealed class NoChannelScript : IChannelScriptSource
    {
        public string Version => throw new NotSupportedException();

        public ValueTask<byte[]> ReadAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
