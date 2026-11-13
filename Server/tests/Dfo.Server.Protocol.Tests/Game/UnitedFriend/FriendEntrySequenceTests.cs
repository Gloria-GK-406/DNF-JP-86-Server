using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;

namespace Dfo.Server.Protocol.Tests.Game.UnitedFriend;

/// <summary>RQ-038: entering the world carries the character's real friend list, once, and its blacklist straight after.</summary>
[Trait("Category", "Unit")]
public sealed class FriendEntrySequenceTests
{
    private static readonly FriendListRefresh Relations = new(
        new FriendListMessage([new(3, "pal"u8.ToArray(), 70, 12, 1)]),
        new BlacklistListResponse([new("foe"u8.ToArray(), new DateOnly(2026, 4, 1))]),
        [], []);

    [Fact]
    public void The_friend_list_keeps_its_place_after_cera_and_the_blacklist_follows_it()
    {
        var messages = SelectedCharacterStateMessageFactory.Create(SelectCharacterPayloadTestData.CreateComplete(), Relations);
        var identities = messages.Select(message => (message is IGameCmdMessage ? 1 : 0, message.Type)).ToArray();

        var friends = Array.IndexOf(identities, (0, (ushort)0x0111));
        Assert.Equal((0, (ushort)0x0035), identities[friends - 1]);
        Assert.Equal((1, (ushort)0x0078), identities[friends + 1]);
        Assert.Equal((0, (ushort)0x0016), identities[friends + 2]);
        Assert.Single(identities, identity => identity == (0, (ushort)0x0111));
        Assert.Single(identities, identity => identity == (1, (ushort)0x0078));
    }

    [Fact]
    public void The_entry_frames_are_the_bytes_of_the_lists_handed_in()
    {
        var messages = SelectedCharacterStateMessageFactory.Create(SelectCharacterPayloadTestData.CreateComplete(), Relations);

        Assert.Equal(
            FriendListEncoder.EncodeBody(Relations.Friends),
            Assert.Single(messages, message => message.Type == 0x0111).EncodeBody());
        Assert.Equal(
            BlacklistListResponseEncoder.EncodeBody(Relations.Blacklist),
            Assert.Single(messages, message => message.Type == 0x0078).EncodeBody());
    }

    [Fact]
    public void A_character_with_no_relations_enters_with_the_eight_byte_empty_list_and_an_empty_blacklist()
    {
        var messages = SelectedCharacterStateMessageFactory.Create(SelectCharacterPayloadTestData.CreateComplete(), FriendListRefresh.None);

        Assert.Equal(new byte[8], Assert.Single(messages, message => message.Type == 0x0111).EncodeBody());
        Assert.Equal(new byte[] { 1, 0 }, Assert.Single(messages, message => message.Type == 0x0078).EncodeBody());
    }

    [Fact]
    public void The_validating_factory_encodes_the_lists_before_anything_is_sent()
    {
        var unreadable = Relations with { Friends = new([new(0, new byte[256], 1, 0, 0)]) };

        Assert.Throws<InvalidDataException>(() =>
            new SelectedCharacterStateMessageFactoryService().CreateValidated(SelectCharacterPayloadTestData.CreateComplete(), unreadable));
    }
}
