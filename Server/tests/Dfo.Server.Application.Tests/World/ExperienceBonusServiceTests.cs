using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

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
public sealed class ExperienceBonusServiceTests
{
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
        var found = Collect(Service(), CarriedDungeon);

        var channel = Assert.Single(found);
        Assert.Equal(0.05, channel.Rate);
        Assert.Equal(ExperienceBonusKind.SeriaBlessing, channel.Kind);
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
        var found = Collect(Service(channelId: channelId), dungeonId);

        Assert.Equal(expected, Assert.Single(found).Rate);
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
        IExperienceBonusService service,
        int dungeonId,
        ExperienceEvent occasion = ExperienceEvent.MonsterKill) =>
        service.Collect(new ExperienceOccasion(
            occasion, new AccountId(1), new CharacterId(60), dungeonId));

    /// <summary>
    /// The service over one published session on <see cref="Channel"/>, with no premium terms.
    /// </summary>
    /// <remarks>
    /// No entitlement policy is configured, so anything this returns is the channel's doing and
    /// nothing else -- which is what lets <c>Assert.Single</c> and <c>Assert.Empty</c> above mean
    /// what they say.
    /// </remarks>
    private static IExperienceBonusService Service(
        bool published = true,
        ChannelExperienceCatalog? channels = null,
        int channelId = Channel) =>
        new ExperienceBonusService(
            new OneSession(),
            new OneRuntimeSession(published, channelId),
            ExperienceEntitlementPolicy.None,
            new FixedChannelExperience(channels ?? TwoChannels),
            TimeProvider.System);

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

    private sealed class FixedChannelExperience(ChannelExperienceCatalog catalog)
        : IChannelExperienceSource
    {
        public ChannelExperienceCatalog Load() => catalog;
    }

    private sealed class OneSession : ISelectedSessionDirectory
    {
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

        public IReadOnlyList<SelectedSessionIdentity> Current() => [Identity];

        public SelectedSessionIdentity? RegisterReplacing(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture only answers.");

        public bool TryRemoveExact(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture only answers.");
    }

    /// <summary>
    /// One published entry carrying the two facts this service reads.
    /// </summary>
    /// <remarks>
    /// The rest of the result is left null on purpose. The service reads the account's premium
    /// terms and the channel it entered on; filling in the other seven members would hide the day
    /// it starts reading an eighth.
    /// </remarks>
    private sealed class OneRuntimeSession(bool published, int channelId)
        : ISelectedSessionRuntimeDirectory
    {
        private readonly SelectCharacterResult _result = new(
            null!,
            null!,
            null!,
            null!,
            new AccountEntryState(
                new AccountId(1),
                null!,
                null!,
                new AccountEntitlementEntryState([]),
                null!,
                null!),
            new GameWorldEntryState(
                DateTimeOffset.UnixEpoch,
                null!,
                new ChannelEntryPolicyState(channelId, null!, true, false)),
            null!);

        public bool TryGet(
            SelectedSessionIdentity session,
            out SelectCharacterResult? result,
            out bool shouldPersistPosition)
        {
            result = published ? this._result : null;
            shouldPersistPosition = false;
            return published;
        }

        public SelectCharacterResult GetRequired(SelectedSessionIdentity session) =>
            published ? this._result : throw new KeyNotFoundException("Nothing was published.");

        public void Publish(
            SelectedSessionIdentity session, SelectCharacterResult result, bool persist) =>
            throw new NotSupportedException("This fixture only answers.");

        public void ReplaceExact(SelectedSessionIdentity session, SelectCharacterResult result) =>
            throw new NotSupportedException("This fixture only answers.");

        public bool RemoveExact(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture only answers.");
    }
}
