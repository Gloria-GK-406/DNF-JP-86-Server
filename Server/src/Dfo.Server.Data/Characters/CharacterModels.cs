using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Data.Characters;

public readonly record struct CharacterId(int Value)
{
    public override string ToString() => Value.ToString();
}

public sealed record CharacterSummary(
    CharacterId CharacterId,
    ushort SlotIndex,
    byte[] Name,
    byte Job,
    byte GrowType,
    byte Level,
    uint CloneTitleItemId);

public sealed record AccountCharacterRoster(
    AccountId AccountId,
    ushort SlotLimit,
    IReadOnlyList<CharacterSummary> Characters);

public sealed record CharacterIdentityState(
    CharacterId CharacterId,
    ushort SlotIndex,
    byte[] Name,
    byte Job,
    byte GrowType);

public sealed record CharacterProgressionState(byte Level, uint Experience);

public sealed record CharacterFatigueState(ushort Total, ushort Used);

public sealed record CharacterPositionState(
    byte TownId,
    byte AreaId,
    short X,
    short Y,
    byte Direction);

public sealed record DarkKnightComboState(IReadOnlyList<uint> SkillIds);

public sealed record ActiveCharacterState(
    CharacterIdentityState Identity,
    CharacterProgressionState Progression,
    CharacterFatigueState Fatigue,
    CharacterPositionState Position,
    DateTimeOffset CreatedAt,
    DarkKnightComboState? DarkKnightCombo);

public sealed record AccountEntryState(AccountId AccountId);

public sealed record GameWorldEntryState(DateTimeOffset PreparedAt);

public sealed record CharacterSessionEntryState(
    AccountId AccountId,
    CharacterId CharacterId);

public sealed record SelectCharacterResult(
    ActiveCharacterState Character,
    AccountEntryState Account,
    GameWorldEntryState World,
    CharacterSessionEntryState Session);
