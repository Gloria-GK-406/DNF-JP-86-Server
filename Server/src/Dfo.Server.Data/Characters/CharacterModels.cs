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
