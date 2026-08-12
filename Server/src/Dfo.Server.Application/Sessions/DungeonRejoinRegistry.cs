using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

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

    public bool ReleaseExact(SelectedSessionIdentity session) =>
        _byCharacter.TryGetValue(session.CharacterId, out var current)
        && current.Session == session
        && _byCharacter.TryRemove(new KeyValuePair<CharacterId, DungeonAttachment>(session.CharacterId, current));

    public bool EndSessionExact(SelectedSessionIdentity session) => ReleaseExact(session);

    public bool ReleaseAttachmentExact(DungeonAttachment attachment) =>
        _byCharacter.TryRemove(new KeyValuePair<CharacterId, DungeonAttachment>(attachment.CharacterId, attachment));

    public bool TryReplaceExact(DungeonAttachment expected, DungeonAttachment replacement)
    {
        if (expected.CharacterId != replacement.CharacterId)
            throw new InvalidDataException("A dungeon attachment replacement cannot change character ownership.");
        return _byCharacter.TryUpdate(expected.CharacterId, replacement, expected);
    }

    public DungeonAttachment? Find(CharacterId characterId) =>
        _byCharacter.TryGetValue(characterId, out var attachment) ? attachment : null;
}
