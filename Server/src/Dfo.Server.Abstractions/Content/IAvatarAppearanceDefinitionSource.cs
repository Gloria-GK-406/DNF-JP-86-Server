namespace Dfo.Server.Abstractions.Content;

/// <summary>Archive facts for a wearable avatar; null means the item is not an avatar.</summary>
public interface IAvatarAppearanceDefinitionSource
{
    ValueTask<bool?> IsCloneAsync(int itemId, CancellationToken cancellationToken);
}
