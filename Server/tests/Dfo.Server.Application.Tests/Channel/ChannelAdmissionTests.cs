using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Application.Channel;
using Dfo.Server.Data.Channel;

namespace Dfo.Server.Application.Tests.Channel;

public sealed class ChannelAdmissionTests
{
    [Theory]
    [InlineData(50, (byte)24, ChannelEntryKind.Pvp)]
    [InlineData(60, (byte)8, ChannelEntryKind.Pvp)]
    [InlineData(68, (byte)13, ChannelEntryKind.FreeDuel)]
    [InlineData(70, (byte)13, ChannelEntryKind.FreeDuel)]
    [InlineData(200, (byte)23, ChannelEntryKind.Raid)]
    public async Task Script_channel_environment_controls_login_and_pvp_profile(int id, byte environment, ChannelEntryKind kind)
    {
        var policy = new ChannelEntryPolicy(new ChannelContentRuleService(new StubChannelCatalog(
            [new ChannelServerInfo(id, $"#ch.{id}", 100, 0, "127.0.0.1", 10000 + id, environment)]),
            new NoChannelScript()), ChannelEntryOptions.Legacy,
            new ChannelListenerOptions([10000 + id], AllowAdvertisedPortFallback: false));
        var channel = await policy.ResolveAsync(10000 + id, default);
        Assert.Equal(environment, channel.LoginEnvironment);
        Assert.Equal(kind, channel.Kind);
        Assert.Equal(environment is 8 or 13 or 24, channel.IsPvp);
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
        var policy = new ChannelEntryPolicy(
            new ChannelContentRuleService(
                new StubChannelCatalog(
                [
                    new ChannelServerInfo(
                        11, "#ch.11", 500, 0, "198.51.100.24", 37102),
                ]),
                new NoChannelScript()),
            ChannelEntryOptions.Legacy,
            new ChannelListenerOptions([10011], AllowAdvertisedPortFallback: false));

        var mapped = await policy.ResolveAsync(10011, CancellationToken.None);
        var advertisementIsNotABind = await policy.ResolveAsync(37102, CancellationToken.None);

        Assert.Equal(11, mapped.ChannelId);
        Assert.Equal(0, advertisementIsNotABind.ChannelId);
    }

    [Fact]
    public async Task Disabled_launcher_matches_advertised_ports_even_when_listener_order_differs()
    {
        var policy = new ChannelEntryPolicy(
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
                AllowAdvertisedPortFallback: true));

        var general = await policy.ResolveAsync(10011, CancellationToken.None);
        var restricted = await policy.ResolveAsync(10161, CancellationToken.None);

        Assert.Equal(11, general.ChannelId);
        Assert.Equal(ChannelEntryKind.General, general.Kind);
        Assert.Null(policy.Refuse(general, characterLevel: 1));
        Assert.Equal(100, restricted.ChannelId);
        Assert.Equal(ChannelEntryKind.SaintHorn, restricted.Kind);
        Assert.NotNull(policy.Refuse(restricted, characterLevel: 1));
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

        var refusal = policy.Refuse(channel, characterLevel: 69);

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

        Assert.Null(policy.Refuse(channel, level));
    }

    [Fact]
    public async Task An_ordinary_channel_refuses_nobody_on_level()
    {
        var policy = Policy();
        var channel = await policy.ResolveAsync(10011, CancellationToken.None);

        Assert.Null(policy.Refuse(channel, characterLevel: 1));
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
        var policy = new ChannelEntryPolicy(new ChannelContentRuleService(new StubChannelCatalog(
            [new(10, "#ch.10", 500, 0, "127.0.0.1", 40010),
             new(11, "#ch.11", 500, 0, "127.0.0.1", 40011),
             new(100, "#ch.100", 500, 0, "127.0.0.1", 40100)]), new NoChannelScript()),
            ChannelEntryOptions.Legacy with { SaintHornMinimumLevel = 75 },
            new ChannelListenerOptions([10010, 10011, 10100], AllowAdvertisedPortFallback: false));
        var channel = await policy.ResolveAsync(10100, default);
        Assert.Equal(100, channel.ChannelId);
        Assert.Equal(ChannelEntryKind.SaintHorn, channel.Kind);
        Assert.True(channel.IsTransient);
        Assert.Contains("75", policy.Refuse(channel, 74)!.Message);
        Assert.Null(policy.Refuse(channel, 75));
        Assert.Equal(11, (await policy.ResolveAsync(10011, default)).ChannelId);
    }

    private static ChannelEntryPolicy Policy(bool freeDuelEnabled = false) =>
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
            new ChannelListenerOptions([], AllowAdvertisedPortFallback: true));

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
