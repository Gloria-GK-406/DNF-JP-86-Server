using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;

namespace Dfo.Server.Protocol.Tests.Game.Quests;

/// <summary>
/// What a character may take is recomputed from where they stand now.
/// </summary>
/// <remarks>
/// The load-bearing part is the level. A kill that crosses a threshold has to be followed by
/// working the offer list out again, and the level it must be worked out at is the new one -- the
/// session's own copy is the entry-time level and would leave a quest whose band was just entered
/// hidden until the next login. Nothing exercised that until this file: the refresher was reached
/// only from live play, which is also how its absence was found, twice.
/// </remarks>
public sealed class QuestOfferRefresherTests
{
    private static readonly AccountId Account = new(1);
    private static readonly CharacterId Character = new(0x1234);

    /// <summary>The list is recomputed at the level handed in, not the one the session holds.</summary>
    [Fact]
    public async Task TheOfferListIsRecomputedAtTheNewLevel()
    {
        var content = new StubWorldContentEntryRuleService();
        var refresher = new QuestOfferRefreshDomainService(content, new RecordingQuestLogs());

        var refreshed = await refresher.RefreshAsync(
            SessionAtLevel(2), Account, Character, level: 3, CancellationToken.None);

        Assert.Equal(3, content.LastSubject?.Level);
        Assert.Equal(3, refreshed.World.Subject?.Level);
    }

    /// <summary>
    /// A session prepared before the subject was recorded is answered with itself.
    /// </summary>
    /// <remarks>
    /// There is nothing to recompute from, and a stale list beats an empty one: emptying it is what
    /// leaves a player with no quest to take and no way into a dungeon.
    /// </remarks>
    [Fact]
    public async Task ASessionWithNoSubjectIsLeftAsItIs()
    {
        var content = new StubWorldContentEntryRuleService();
        var refresher = new QuestOfferRefreshDomainService(content, new RecordingQuestLogs());
        var without = SessionAtLevel(2);
        without = without with { World = without.World with { Subject = null } };

        var refreshed = await refresher.RefreshAsync(
            without, Account, Character, level: 3, CancellationToken.None);

        Assert.Same(without, refreshed);
        Assert.Null(content.LastSubject);
    }

    private static SelectCharacterResult SessionAtLevel(int level)
    {
        var result = SelectCharacterPayloadTestData.Create();
        return result with
        {
            World = result.World with
            {
                Subject = new WorldContentSubject(
                    level, Job: 0, GrowType: 0,
                    ClearedQuestFlags: new Dictionary<int, int>(),
                    CarriedQuestIds: [],
                    ClaimedDailyChallengeGroupIds: [],
                    CollectionBoxes: [],
                    EquippedCreatureItemId: 0,
                    EquippedCreatureLevel: 0),
            },
        };
    }

}
