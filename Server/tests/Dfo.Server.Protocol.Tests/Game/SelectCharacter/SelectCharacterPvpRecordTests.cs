using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.PvpBattle;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Tests.Game.SelectCharacter;

/// <summary>
/// Spec RQ-055 / AC-055: every character entry carries its own duel record right after HOTKEY_OPTION, after both
/// USERINFO0 frames, as upstream <c>NewCharacterInitSequence</c> places it.
/// </summary>
public sealed class SelectCharacterPvpRecordTests
{
    [Fact]
    public void The_record_follows_the_hotkeys_and_both_user_infos_and_nothing_else_moves()
    {
        var batch = SelectedCharacterStateMessageFactory.Create(SelectCharacterPayloadTestData.CreateComplete(), FriendListRefresh.None);
        var record = new PvpRecordView(3, 0, 1500, 1000, 2000, 7, 4);

        var entry = GameSelectCharacterCommandHandler.WithPvpRecord(batch, record);

        Assert.Equal(batch.Count + 1, entry.Count);
        var at = entry.Select((message, index) => (message, index)).Single(x => x.message is PvpRecordMessage).index;
        Assert.IsType<HotkeyStateMessage>(entry[at - 1]);
        Assert.True(entry.Take(at).OfType<CurrentUserStateMessage>().Any());
        Assert.True(entry.Take(at).OfType<ExtendedUserStateMessage>().Any());
        Assert.Equal(record, ((PvpRecordMessage)entry[at]).Record);
        Assert.Equal(51, PvpBattlePayloadCodec.Encode((PvpRecordMessage)entry[at]).Length);
        Assert.Equal(batch.Cast<IGameServerMessage>(), entry.Where(message => message is not PvpRecordMessage));
    }
}
