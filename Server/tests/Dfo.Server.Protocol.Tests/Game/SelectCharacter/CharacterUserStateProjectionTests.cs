using Dfo.Server.Protocol.Game.SelectCharacter;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Tests.Game.SelectCharacter;

public sealed class CharacterUserStateProjectionTests
{
    [Fact]
    public void Complete_refresh_matches_entry_userinfo_bytes_including_name_tag_and_all_other_state()
    {
        var source = SelectCharacterPayloadTestData.CreateComplete();
        source = source with { CharacterEntry = source.CharacterEntry with
        { Public = source.CharacterEntry.Public with { Extras = source.CharacterEntry.Public.Extras with
            { NameTagItemId = 100330501, NameTagExpireTime = 1795000000 } } } };
        var entry = SelectedCharacterStateMessageFactory.Create(source)
            .Where(m => m is CurrentUserStateMessage or ExtendedUserStateMessage).ToArray();
        var refresh = CharacterUserStateProjection.Compose(source).Cast<GameStateSynchronizationMessage>().ToArray();
        Assert.Equal(2, refresh.Length);
        Assert.Equal(entry.Select(m => m.EncodeBody()), refresh.Select(m => m.EncodeBody()));
        Assert.Equal(refresh[0].EncodeBody(), Assert.Single(CharacterUserStateProjection.Compose(source, owner: false)
            .Cast<GameStateSynchronizationMessage>()).EncodeBody());
    }
}
