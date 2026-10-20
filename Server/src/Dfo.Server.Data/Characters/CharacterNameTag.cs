namespace Dfo.Server.Data.Characters;

public sealed record CharacterNameTag(int ItemId, long ExpiresAtUnixSeconds)
{
    public static CharacterNameTag Empty { get; } = new(0, 0);
}
