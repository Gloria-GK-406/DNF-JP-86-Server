using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Sessions;

/// <summary>Keeps interrupted dungeon runs in memory for the server's lifetime.</summary>
internal sealed class DungeonRejoinRegistry : IDungeonRejoinRegistry
{
    private readonly ConcurrentDictionary<CharacterId, DungeonAttachment> _byCharacter = new();

    public void Attach(DungeonAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        _byCharacter[attachment.CharacterId] = attachment;
    }

    public bool Release(CharacterId characterId) => _byCharacter.TryRemove(characterId, out _);

    public DungeonAttachment? Find(CharacterId characterId) =>
        _byCharacter.TryGetValue(characterId, out var attachment) ? attachment : null;
}
