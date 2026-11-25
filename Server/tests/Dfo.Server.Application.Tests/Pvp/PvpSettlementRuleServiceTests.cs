using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Application.Channel;
using Dfo.Server.Application.Configuration;
using Dfo.Server.Application.Pvp;
using Dfo.Server.Data.Channel;

namespace Dfo.Server.Application.Tests.Pvp;

public sealed class PvpSettlementRuleServiceTests
{
    internal static readonly IDailyResetRuleService Days = new DailyResetRuleService(new Settings(TimeSpan.FromHours(6)));

    [Theory]
    [InlineData((byte)8)]
    [InlineData((byte)13)]
    [InlineData((byte)24)]
    [InlineData(null)]
    public void A_free_duel_channel_records_nothing_whatever_its_environment(byte? environment)
    {
        Assert.Equal(new PvpChannelEntitlements(false, false, 0, 0),
            Unarchived().EntitlementsOf(new ChannelEntryIdentity(68, 10068, ChannelEntryKind.FreeDuel, environment)));
    }

    [Fact]
    public void The_matchmaking_duel_channel_records_nothing()
    {
        Assert.Equal(new PvpChannelEntitlements(false, false, 0, 0),
            Unarchived().EntitlementsOf(new(60, 10060, ChannelEntryKind.MatchmakingDuel)));
    }

    [Fact]
    public void The_ranked_duel_channel_records_and_caps_daily_experience()
    {
        Assert.Equal(new PvpChannelEntitlements(true, true, 50, 10),
            Unarchived().EntitlementsOf(new(50, 10050, ChannelEntryKind.TraditionalDuel)));
    }

    [Theory]
    [InlineData(ChannelEntryKind.General)]
    [InlineData(ChannelEntryKind.SaintHorn)]
    [InlineData(ChannelEntryKind.Raid)]
    public void A_channel_that_is_not_a_duel_channel_has_no_entitlements(ChannelEntryKind kind)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Unarchived().EntitlementsOf(new ChannelEntryIdentity(1, 10011, kind, 24)));
    }

    [Fact]
    public async Task Every_environment_classified_as_a_duel_channel_has_entitlements()
    {
        var rules = Unarchived();
        var duelling = new List<byte>();
        for (var environment = 0; environment <= byte.MaxValue; environment++)
        {
            var channel = await Classify((byte)environment);
            if (!channel.IsPvp) continue;
            duelling.Add((byte)environment);
            Assert.NotNull(rules.EntitlementsOf(channel));
        }

        Assert.Equal(new byte[] { 8, 13, 24 }, duelling);
    }

    [SkippableFact]
    public void Battle_time_limits_come_from_the_archive()
    {
        var rules = PvpGradeRulesTests.Rules();
        foreach (var mode in Enum.GetValues<PvpBattleMode>().Where(mode => mode != PvpBattleMode.TotalMatch))
            Assert.Equal(mode == PvpBattleMode.Relay ? TimeSpan.FromSeconds(120) : TimeSpan.FromSeconds(180),
                rules.BattleTimeLimit(mode));
        Assert.Equal(TimeSpan.FromSeconds(180), rules.BattleTimeLimit(PvpBattleMode.TotalMatch));
    }

    [Theory]
    [InlineData("2026-09-23T05:59:59+08:00", "2026-09-22")]
    [InlineData("2026-09-23T06:00:00+08:00", "2026-09-23")]
    public void A_battle_belongs_to_the_game_day_it_started_in(string startedAt, string day)
    {
        Assert.Equal(DateOnly.Parse(day), Unarchived().GameDayOf(DateTimeOffset.Parse(startedAt)));
    }

    private static PvpSettlementRuleService Unarchived() => new(new NoGrades(), Days, new PvpMatchmakingRuleService());

    private static ValueTask<ChannelEntryIdentity> Classify(byte environment)
    {
        const int id = 50;
        var channels = new ChannelEntryDomainService(
            new ChannelContentRuleService(
                new Channels([new ChannelServerInfo(id, "#ch", 100, 0, "127.0.0.1", 10000 + id, environment)]),
                new NoChannelScript()),
            ChannelEntryOptions.Legacy, new ChannelListenerOptions([10000 + id], AllowAdvertisedPortFallback: false),
            new NoDeployment(),
            TestRequired.Absent<Dfo.Server.Abstractions.Quests.ICharacterQuestLogDomainService>());
        return channels.ResolveAsync(10000 + id, default);
    }

    private sealed class NoGrades : IPvpGradeDefinitionSource
    {
        public PvpGradeCatalog Load() => throw new InvalidOperationException("Entitlements must not read the grade table.");
    }

    private sealed class Settings(TimeSpan reset) : IServerConfigService
    {
        public T GetConfig<T>(string key) => key == "Game:DailyResetTime"
            ? (T)(object)reset : throw new InvalidOperationException(key);
    }

    private sealed class Channels(IReadOnlyList<ChannelServerInfo> channels) : IChannelCatalog
    {
        public ValueTask<IReadOnlyList<ChannelServerInfo>> GetChannelsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(channels);
    }

    private sealed class NoChannelScript : IChannelScriptSource
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
