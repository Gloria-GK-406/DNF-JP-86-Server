using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Accounts;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// Where the channel a character is on meets the dungeon they are in.
/// </summary>
/// <remarks>
/// The rules beside this are pure and easy to check; what is only checkable here is that the
/// service asks the right question. It has to read the channel out of the published session and
/// the dungeon out of the occasion, and until 2026-08-29 it asked neither -- it paid a flat rate to
/// everyone everywhere, because the rate had been recovered from four kills that all happened to
/// be in a dungeon their channel carried.
/// </remarks>
public sealed class ExperienceBonusDomainServiceTests
{
    [Theory]
    [InlineData(ExperienceEvent.MonsterKill, true, false)]
    [InlineData(ExperienceEvent.DungeonClear, true, true)]
    [InlineData(ExperienceEvent.QuestCompletion, false, false)]
    public void Growth_and_potion_have_separate_occasion_rules(ExperienceEvent occasion, bool growth, bool potion)
    {
        TestGameplay.Run(() =>
        {
            var potions = new OnlineCharacterRuntimeDomainService(TimeProvider.System, new OnlineCharacterLocationDomainService());
            potions.Begin(OneSession.Identity, new Dfo.Server.Data.Characters.CharacterPositionState(1, 0, 0, 0, 0), 100);
            potions.ApplySpiritPotion(OneSession.Identity, 1, TimeSpan.FromHours(2));
            var found = Collect(Service(premiums: [new(84, 4_000_000_000)], potions: potions), OtherDungeon, occasion);
            Assert.Equal(growth, found.Any(x => x.Kind == ExperienceBonusKind.GrowthContract && x.Rate == .2));
            Assert.Equal(potion, found.Any(x => x.Kind == ExperienceBonusKind.SpiritPotion && x.Rate == 1));
            Assert.Equal((growth ? 1 : 0) + (potion ? 1 : 0), found.Count);
        });
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    public void Expired_growth_does_not_contribute(long expiry) =>
        Assert.Empty(Collect(Service(premiums: [new(84, expiry)]), OtherDungeon));

    [Theory]
    [InlineData(ExperienceEvent.DungeonClear, true, true)]
    [InlineData(ExperienceEvent.DungeonClear, false, false)]
    [InlineData(ExperienceEvent.MonsterKill, true, false)]
    [InlineData(ExperienceEvent.QuestCompletion, true, false)]
    public void BlackDiamondIsOnlyGrantedForEligibleClear(ExperienceEvent occasion, bool enabled, bool expected)
    {
        TestGameplay.Run(() =>
        {
            var found = Collect(Service(blackDiamond: enabled, premiums: [new(56, 4_000_000_000)]), OtherDungeon, occasion);
            Assert.Equal(expected ? 1 : 0, found.Count);
            if (expected)
            {
                Assert.Equal(ExperienceBonusKind.BlackDiamond, found[0].Kind);
                Assert.Equal(.05, found[0].Rate);
            }
        });
    }

    private sealed class Rates : IPremiumServiceRuleService
    {
        public int GrowthBossEquipmentBonusPercent(int count) => count == 0 ? 0 : new[] {20,23,27,30}[count-1];
        public double BlackDiamondExperienceBonusRate => .05;
        public double GrowthExperienceBonusRate => .2;
        public IReadOnlyList<int> QuestHelperMinimumDropRates => throw new NotSupportedException();
        public int DrawQuestHelperBonusCount(ulong seed) => throw new NotSupportedException();
        public int DoublePotDailyLimit => throw new NotSupportedException();
        public int DoublePotTotalDailyLimit => throw new NotSupportedException();
        public int DoublePotRenewedUsage(int totalUsed) => throw new NotSupportedException();
        public bool SupportsDoublePot(int itemId) => throw new NotSupportedException();
        public int FreeWeaknessDailyLimit => throw new NotSupportedException();
        public int DungeonBuffDailyLimit => throw new NotSupportedException();
        public int AutoRepairDailyLimit => throw new NotSupportedException();
        public int FreeGoldCardDailyLimit => throw new NotSupportedException();
        public int LevelAllowance(PremiumBenefit benefit) => throw new NotSupportedException();
    }

    private const int CarriedDungeon = 144;
    private const int OtherDungeon = 157;

    /// <summary>The channel the session is on, which carries <see cref="CarriedDungeon"/>.</summary>
    private const int Channel = 10;

    /// <summary>Another channel, which carries <see cref="OtherDungeon"/> at a different rate.</summary>
    /// <remarks>
    /// Present so the service has to look the channel up rather than answer from the only row
    /// there is. With one channel in the catalogue, a service that ignored the session's channel
    /// entirely -- or read the listener port by mistake, which is the neighbouring field -- would
    /// still answer correctly.
    /// </remarks>
    private const int OtherChannel = 11;

    /// <summary>A kill in one of the channel's own dungeons earns the declared rate.</summary>
    [Fact]
    public void AKillInTheChannelsOwnDungeonEarnsTheDeclaredRate()
    {
        TestGameplay.Run(() =>
        {
            var found = Collect(Service(), CarriedDungeon);

            var channel = Assert.Single(found);
            Assert.Equal(0.05, channel.Rate);
            Assert.Equal(ExperienceBonusKind.SeriaBlessing, channel.Kind);
        });
    }

    /// <summary>The rate is the one declared for the channel the session is actually on.</summary>
    /// <remarks>
    /// Both channels are asked, because one is not enough: with a single row in the catalogue, a
    /// service that ignored the session's channel -- answering from a constant, or from the
    /// listener port sitting next to it -- would agree with every assertion. Two sessions on two
    /// channels, each carrying a different dungeon at a different rate, leave nowhere for that to
    /// hide.
    /// </remarks>
    [Theory]
    [InlineData(Channel, CarriedDungeon, 0.05)]
    [InlineData(OtherChannel, OtherDungeon, OtherChannelRate)]
    public void TheRateIsTheOneDeclaredForTheChannelTheSessionIsOn(
        int channelId, int dungeonId, double expected)
    {
        TestGameplay.Run(() =>
        {
            var found = Collect(Service(channelId: channelId), dungeonId);

            Assert.Equal(expected, Assert.Single(found).Rate);
        });
    }

    /// <summary>A channel earns nothing for the other channel's dungeon.</summary>
    [Theory]
    [InlineData(Channel, OtherDungeon)]
    [InlineData(OtherChannel, CarriedDungeon)]
    public void AChannelEarnsNothingForAnotherChannelsDungeon(int channelId, int dungeonId) =>
        Assert.Empty(Collect(Service(channelId: channelId), dungeonId));

    /// <summary>
    /// A catalogue that declares nothing -- an unreadable script -- pays nothing and throws nothing.
    /// </summary>
    [Fact]
    public void ACatalogueThatDeclaresNothingPaysNothing() =>
        Assert.Empty(Collect(Service(channels: ChannelExperienceCatalog.Empty), CarriedDungeon));

    /// <summary>A kill anywhere else on the same channel earns nothing.</summary>
    /// <remarks>
    /// The same session, the same channel, the same declared rate -- one dungeon apart. This is
    /// the condition the service exists to apply, and the case that was silently wrong before.
    /// </remarks>
    [Fact]
    public void AKillOutsideTheChannelsDungeonsEarnsNothing() =>
        Assert.Empty(Collect(Service(), OtherDungeon));

    /// <summary>A character with no published session earns no channel reward.</summary>
    /// <remarks>
    /// There is no channel to ask about. Answering with the last known rate, or with a default,
    /// would be paying on a guess about where the player is.
    /// </remarks>
    [Fact]
    public void ACharacterWithNoPublishedSessionEarnsNothing() =>
        Assert.Empty(Collect(Service(published: false), CarriedDungeon));

    /// <summary>The other occasions are not paid a channel reward.</summary>
    [Theory]
    [InlineData(ExperienceEvent.DungeonClear)]
    [InlineData(ExperienceEvent.QuestCompletion)]
    public void OnlyAKillIsPaid(ExperienceEvent occasion) =>
        Assert.Empty(Collect(Service(), CarriedDungeon, occasion));

    private static IReadOnlyList<ExperienceModifier> Collect(
        IExperienceBonusDomainService service,
        int dungeonId,
        ExperienceEvent occasion = ExperienceEvent.MonsterKill) =>
        TestGameplay.Run(() =>
        service.CollectAsync(new ExperienceOccasion(
            occasion, new AccountId(1), new CharacterId(60), dungeonId),
            CancellationToken.None).AsTask().GetAwaiter().GetResult());

    /// <summary>
    /// The service over one published session on <see cref="Channel"/>, with no premium terms.
    /// </summary>
    /// <remarks>
    /// No entitlement policy is configured, so anything this returns is the channel's doing and
    /// nothing else -- which is what lets <c>Assert.Single</c> and <c>Assert.Empty</c> above mean
    /// what they say.
    /// </remarks>
    private static IExperienceBonusDomainService Service(
        bool published = true,
        ChannelExperienceCatalog? channels = null,
        int channelId = Channel,
        IReadOnlyList<AccountPremiumRow>? premiums = null,
        IOnlineCharacterRuntimeDomainService? potions = null,
        bool blackDiamond = false) =>
        new ExperienceBonusDomainService(
            new OneSession(published),
            new PremiumServiceDomainService(new NoPremium(premiums ?? []), TestRequired.TransactionScope(), TimeProvider.System, new Rates(), new Dfo.Server.Infrastructure.Configuration.ServerConfigService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Premium:BlackDiamondEnabled"] = blackDiamond.ToString() }).Build())),
            new OneChannel(channelId),
            new ExperienceRuleService(
                new UnpricedKills(),
                new UngradedRanks(),
                new NoProgression(),
                new FixedChannelExperience(channels ?? TwoChannels),
                ExperienceTuning.Default,
                ExperienceEntitlementPolicy.None),
            TimeProvider.System, potions ?? new OnlineCharacterRuntimeDomainService(TimeProvider.System, new OnlineCharacterLocationDomainService()));

    /// <summary>What the other channel declares, which is deliberately not five percent.</summary>
    private const double OtherChannelRate = 0.20;

    /// <summary>Two channels, each carrying one dungeon, at rates that differ.</summary>
    private static readonly ChannelExperienceCatalog TwoChannels =
        new(new Dictionary<int, ChannelExperienceEntry>
        {
            [Channel] = new(Channel, "[granfloris]", 0.05, new HashSet<int> { CarriedDungeon }),
            [OtherChannel] = new(
                OtherChannel, "[sky_catle]", OtherChannelRate, new HashSet<int> { OtherDungeon }),
        });

    /// <summary>Nothing on this path prices a kill.</summary>
    private sealed class UnpricedKills : IExperienceParameterSource
    {
        public ExperienceParameters Load() => throw new NotSupportedException();
    }

    /// <summary>Nothing on this path reads a rank grade.</summary>
    private sealed class UngradedRanks : IDungeonRankGradeSource
    {
        public DungeonRankGrades Load() => throw new NotSupportedException();
    }

    /// <summary>Nothing on this path turns experience into a level.</summary>
    private sealed class NoProgression : IDungeonExperienceProgressionPolicySource
    {
        public DungeonExperienceProgressionPolicy Load() => throw new NotSupportedException();
    }

    /// <summary>Nothing on this path reads a quest.</summary>
    private sealed class NoQuestContent : IQuestContentRuleService
    {
        public ValueTask<IReadOnlyList<QuestDefinition>> AllAsync(CancellationToken ct) =>
            throw new NotSupportedException();

        public ValueTask<QuestDefinition?> FindAsync(int questId, CancellationToken ct) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyList<TrainingQuestAvailability>> TrainingAvailabilityAsync(
            CancellationToken ct) => throw new NotSupportedException();

        public QuestParameters Parameters() => throw new NotSupportedException();
    }

    private sealed class FixedChannelExperience(ChannelExperienceCatalog catalog)
        : IChannelExperienceSource
    {
        public ChannelExperienceCatalog Load() => catalog;
    }

    /// <summary>One selected session, in the world or not as the case says.</summary>
    private sealed class OneSession(bool published = true) : ISelectedSessionDomainService
    {
        public void EnterWorld(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture only answers.");

        public bool Fence(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture only answers.");

        public SelectedSessionStanding? StandingOf(SelectedSessionIdentity session) =>
            session == Identity
                ? new(published ? SelectedSessionPhase.InWorld : SelectedSessionPhase.Selected, false, false)
                : null;

        public bool IsInWorld(SelectedSessionIdentity session) => published && session == Identity;

        public bool IsSelected(SelectedSessionIdentity session) => session == Identity;

        public bool TryGetInWorld(CharacterId characterId, [NotNullWhen(true)] out SelectedSessionIdentity? session) =>
            (session = published && characterId == Identity.CharacterId ? Identity : null) is not null;

        public bool TryGetInWorld(ushort wireUserId, [NotNullWhen(true)] out SelectedSessionIdentity? session) =>
            (session = published && wireUserId == Identity.WireUserId ? Identity : null) is not null;

        public bool TryGetInWorld(NetworkConnectionId connectionId, [NotNullWhen(true)] out SelectedSessionIdentity? session) =>
            (session = published && connectionId == Identity.ConnectionId ? Identity : null) is not null;

        public static readonly SelectedSessionIdentity Identity = SelectedSessionIdentity.Create(
            new AccountId(1),
            new CharacterId(60),
            new NetworkConnectionId("one"),
            connectionGeneration: 1,
            listenerPort: 10010);

        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) => Identity;

        public SelectedSessionIdentity? Find(AccountId accountId) => Identity;

        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => Identity;

        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) =>
            Identity;

        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture only answers.");

        public bool ReleaseExact(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture only answers.");
    }

    private sealed class NoPremium(IReadOnlyList<AccountPremiumRow> rows) : IAccountPremiumTable
    {
        public ValueTask<IReadOnlyList<AccountPremiumRow>> ReadAsync(
            AccountId accountId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(rows);
    }

    private sealed class OneChannel(int channelId) : IChannelEntryDomainService
    {
        public ValueTask<Dfo.Server.Data.Channel.ChannelLoginEntry> ResolveLoginAsync(int listenerPort,CancellationToken ct) =>
            ValueTask.FromException<Dfo.Server.Data.Channel.ChannelLoginEntry>(new NotSupportedException("This channel-identity fixture does not supply login deployment facts."));

        public ValueTask<ChannelEntryIdentity> ResolveAsync(
            int listenerPort, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ChannelEntryIdentity(
                channelId, listenerPort, ChannelEntryKind.General));

        public ChannelAdmissionRefusal? Refuse(ChannelEntryIdentity channel, int characterLevel) => null;
    }
}
