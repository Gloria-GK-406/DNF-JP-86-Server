using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;

namespace Dfo.Server.Protocol.Tests.Game;

/// <summary>
/// The stand-ins the four slices that send an experience frame have in common.
/// </summary>
/// <remarks>
/// Four paths raise <c>NOTI 0x0025</c> -- a kill, a dungeon settlement, a quest handed in and the
/// tutorial's own level -- and all four reach the same three seams to do it: which session is
/// asking, what that session is holding, and where the frames go. Held in one place rather than
/// four, because the thing they are all now checked for is one thing: that the skill points on the
/// frame come from the character rather than from the session's entry-time projection. Four copies
/// of the ledger would let one of them drift into answering the same figure twice, and a ledger
/// that answers the same figure twice cannot fail that check.
/// </remarks>
internal sealed class OneSession(SelectedSessionIdentity identity) : ISelectedSessionDomainService
{
    public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) => identity;

    public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) => identity;

    public SelectedSessionIdentity? Find(AccountId accountId) => identity;

    public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => identity;

    public SelectedSessionIdentity? Select(SelectedSessionIdentity session) =>
        throw new NotSupportedException("This fixture only answers.");

    public bool ReleaseExact(SelectedSessionIdentity session) =>
        throw new NotSupportedException("This fixture only answers.");
}

/// <summary>The one published session these cases play, and what it holds after they run.</summary>
internal sealed class OneRuntimeSession(SelectCharacterResult entry)
    : ISelectedSessionRuntimeDirectory
{
    private SelectCharacterResult _entry = entry;

    /// <summary>What the session holds now, which a slice that corrected it has replaced.</summary>
    public SelectCharacterResult Published => this._entry;

    public bool TryGet(
        SelectedSessionIdentity session,
        out SelectCharacterResult? result,
        out bool shouldPersistPosition)
    {
        result = this._entry;
        shouldPersistPosition = false;
        return true;
    }

    public void ReplaceExact(SelectedSessionIdentity session, SelectCharacterResult result) =>
        this._entry = result;

    public SelectCharacterResult GetRequired(SelectedSessionIdentity session) => this._entry;

    public void Publish(
        SelectedSessionIdentity session, SelectCharacterResult result, bool persist) =>
        throw new NotSupportedException("This fixture only answers.");

    public bool RemoveExact(SelectedSessionIdentity session) =>
        throw new NotSupportedException("This fixture only answers.");
}

/// <summary>Every frame a slice handed out, in the order it handed them out.</summary>
internal sealed class RecordingSender : IGameMessageSender
{
    public List<IGameServerMessage> Sent { get; } = [];

    public ValueTask SendCmdAsync(
        NetworkConnectionId connectionId, IGameCmdMessage message,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("These slices send their frames explicitly.");

    public ValueTask SendNotiAsync(
        NetworkConnectionId connectionId, IGameNotiMessage message,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("These slices send their frames explicitly.");

    public ValueTask SendExplicitAsync(
        NetworkConnectionId requiredDirectConnectionId,
        GameExplicitMessageDelivery delivery,
        CancellationToken cancellationToken)
    {
        this.Sent.AddRange(delivery.RequiredDirectMessages);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// A character's skill-point ledger, answering a different figure each time it is asked.
/// </summary>
/// <remarks>
/// Different on purpose. The real ledger is worked out from the level the store holds, so the
/// answer before an award and the answer after one differ exactly when the award crossed a
/// level -- and a stand-in that answered the same figure twice would let a handler that never
/// asked again pass every case here. Which figures come back is not the subject; that the
/// frame carries the later one is.
/// <para>
/// Both figures also differ from <see cref="StaleEntryMirror"/>, which is what the session's
/// entry-time projection holds in these fixtures. A slice that went back to reading the session
/// therefore reports a figure this ledger never gave out, rather than one that happens to match.
/// </para>
/// </remarks>
internal sealed class SkillPointLedger(bool present = true) : ICharacter
{
    /// <summary>What the character had to spend before the clear was paid.</summary>
    public static CharacterSkillProtocolMirrorState BeforeTheLevel { get; } = new(11, 22, 33, 44);

    /// <summary>And after, the level having bought more.</summary>
    public static CharacterSkillProtocolMirrorState AfterTheLevel { get; } = new(14, 25, 33, 44);

    /// <summary>
    /// The figure the session's entry-time projection carries, which is none of the above.
    /// </summary>
    /// <remarks>
    /// The projection made on the way in, which nothing recomputes. It is the figure every one of
    /// these slices used to report, and a fixture that left it equal to what the ledger answers
    /// would be unable to tell the two apart -- which is exactly how the defect survived.
    /// </remarks>
    public static CharacterSkillProtocolMirrorState StaleEntryMirror { get; } =
        new(200, 201, 202, 203);

    /// <summary>How many times the handler asked, which a second ask raises.</summary>
    public int TimesAsked { get; private set; }

    public CharacterId Id => new(60);

    public ValueTask<CharacterSkillProtocolMirrorState?> RemainingSkillPointsAsync(
        CancellationToken cancellationToken)
    {
        if (!present)
            return ValueTask.FromResult<CharacterSkillProtocolMirrorState?>(null);

        this.TimesAsked++;
        return ValueTask.FromResult<CharacterSkillProtocolMirrorState?>(
            this.TimesAsked == 1 ? BeforeTheLevel : AfterTheLevel);
    }

    public ValueTask<CharacterProgressionState?> ProgressAsync(
        CancellationToken cancellationToken) => throw Unasked();

    public ValueTask<CharacterIdentityState?> IdentityAsync(
        CancellationToken cancellationToken) => throw Unasked();

    public ValueTask<ItemUsability> CanUseAsync(
        int itemId, CancellationToken cancellationToken) => throw Unasked();

    public ValueTask<int> GoldAsync(CancellationToken cancellationToken) => throw Unasked();

    public ValueTask<int> AddGoldAsync(int amount, CancellationToken cancellationToken) =>
        throw Unasked();

    public ValueTask<int?> TrySpendGoldAsync(
        int amount, CancellationToken cancellationToken) => throw Unasked();

    private static NotSupportedException Unasked() =>
        new("These slices ask a character for its skill points and nothing else.");
}

/// <summary>
/// The published session the three unpaid-for paths start from: a character standing somewhere,
/// with an entry-time skill-point projection that is deliberately wrong.
/// </summary>
/// <remarks>
/// <b>The mirror is the point of this fixture.</b> The projection is what every one of these
/// slices used to report, so a fixture that left it null would fail a slice that went back to
/// reading it -- but it would fail with a null reference, which is the wrong reason: it would pass
/// again the moment the entry happened to carry one. Filled with a figure the ledger never gives
/// out instead, so a slice reading the session reports something plausible and wrong, exactly as
/// the live client saw it.
/// <para>
/// Only the members these slices read are filled in. The rest stays as the shared builder leaves
/// it, so the day one of them starts reading a fourth is a day this fixture fails loudly.
/// </para>
/// </remarks>
internal static class ExperienceFrameEntry
{
    /// <summary>A published session for a character at <paramref name="level"/>.</summary>
    public static SelectCharacterResult At(byte level, uint experience)
    {
        var result = SelectCharacterPayloadTestData.Create();
        return result with
        {
            Character = result.Character with
            {
                Progression = new CharacterProgressionState(level, experience),
            },
            CharacterEntry = result.CharacterEntry with
            {
                Public = new CharacterPublicEntryState(
                    null!, null!, null!, null!, null!, null!,
                    new CharacterEntryExtrasState(
                        SkillTreeIndex: 0,
                        NameTagItemId: 0,
                        NameTagExpireTime: 0,
                        ManageLevel: 0,
                        CharacterExperience: experience,
                        ExtraEquipmentSlotState: 0,
                        Dimensions: [],
                        DimensionFlags: (0, 0, 0, 0),
                        SpecialRewardQuestIds: []),
                    null!,
                    VisibilityBits: 0,
                    CloneTitleItemId: 0),
                Skills = new CharacterSkillsEntryState(
                    [],
                    [],
                    new CharacterSkillPointBasisState(0, 0),
                    null,
                    null,
                    SkillPointLedger.StaleEntryMirror),
            },
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
