using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Mail;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Protocol.Game.Mailbox;
using Dfo.Server.Protocol.Game.StateSynchronization;
namespace Dfo.Server.Protocol.Tests.Game.Mailbox;
public sealed class MailboxProtocolTests
{
    [Fact]
    public async Task CommittedMailDeliveryComposesCurrentCountAndRetainsExistingAlarmWire()
    {
        var query = new CurrentMailbox(); var handler = new MailDeliveredHandler(query); var character = new CharacterId(1);
        var recipient = new NotificationRecipient(character, new NetworkConnectionId("Game:1")); var notice = new MailDelivered(character);
        query.Count = 2;
        var first = Assert.IsType<MailboxAlarm>(Assert.Single(await handler.ComposeAsync(recipient, notice, default)));
        Assert.Equal(new byte[] { 2, 0 }, Body(new MailboxAlarmEncoder().Encode(first)));
        query.Count = 0;
        var afterClaim = Assert.IsType<MailboxAlarm>(Assert.Single(await handler.ComposeAsync(recipient, notice, default)));
        Assert.Equal(new byte[] { 0, 0 }, Body(new MailboxAlarmEncoder().Encode(afterClaim)));
        query.Count = 7;
        Assert.Equal((ushort)7, Assert.IsType<MailboxAlarm>(Assert.Single(await handler.ComposeAsync(recipient, notice, default))).Count);
        Assert.Empty(await handler.ComposeAsync(new(new CharacterId(2), new NetworkConnectionId("Game:2")), notice, default));
    }
    private sealed class CurrentMailbox : IMailboxQueryDomainService
    {
        internal int Count { get; set; }
        public ValueTask<MailPage> PageAsync(CharacterId id, int size, CancellationToken token = default) => ValueTask.FromResult(new MailPage(Count, []));
        public ValueTask<MailRecipient?> FindRecipientAsync(byte[] name, CancellationToken token = default) => throw new NotSupportedException();
        public ValueTask<Dfo.Server.Abstractions.Characters.CharacterMailboxStorageRecord> ReadStorageAsync(CharacterId id, CancellationToken token = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<MailView>> InboxAsync(CharacterId id, CancellationToken token = default) => throw new NotSupportedException();
    }
    [Theory]
    [InlineData(0x5F, "01000040")]
    [InlineData(0x86, "010000000200")]
    public void Registered_requests_preserve_fixed_boundaries(int opcode, string hex)
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var registration = Assert.Single(provider.GetServices<GameMessageDecoderRegistration>(), x => x.Selector == 1 && x.Type == opcode);
        var decoder = (IGameMessageDecoder)provider.GetRequiredService(registration.DecoderType);
        var bytes = Convert.FromHexString(hex);
        Assert.NotNull(decoder.Decode(GameMessageMetadata.Empty, bytes));
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, bytes[..^1]));
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, [.. bytes, 0]));
    }

    [Theory]
    [InlineData(false, "0300000032333400000000001100A34B050601000000010000002000000000", 1, 0, " ")]
    [InlineData(true, "030000003233343075000003001100A34B050601000000004E0004B698000100000000580008B6980001000000010000002000000000", 3, 30000, " ")]
    [InlineData(false, "05000000313131313100000000008100E70C00006500000025000000313131323232E8BF99E698AFE4B880E4B8AAE6B58BE8AF95E982AEE4BBB67465737441414100000000", 1, 0, "111222这是一个测试邮件testAAA")]
    public void Observed_send_frames_decode_and_reject_truncation_and_unknown_tail(bool multi, string hex, int count, int gold, string text)
    {
        IGameMessageDecoder decoder = multi ? new MailboxSendMultiDecoder() : new MailboxSendDecoder();
        var bytes = Convert.FromHexString(hex);
        var request = Assert.IsType<MailboxSendRequest>(decoder.Decode(GameMessageMetadata.Empty, bytes));
        Assert.Equal(count, request.Mail.Attachments.Count);
        Assert.Equal(gold, request.Mail.Gold);
        Assert.Equal(text, request.Mail.Body);
        if (text.Length > 1) Assert.Equal(101, request.Mail.Attachments[0].Count);
        for (var n = 0; n < bytes.Length; n++)
        {
            var partial = bytes[..n];
            Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, partial));
        }
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, [.. bytes, 0]));
        bytes[^1] = 1;
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, bytes));
        Assert.Equal(new byte[] { 1 }, Body(new MailboxSendEncoder().Encode(new MailboxSendResponse(multi))));
        Assert.Equal(new byte[] { 0, 3 }, Body(new MailboxSendEncoder().Encode(new MailboxSendResponse(multi, 3))));
    }

    [Fact]
    public void Observed_empty_mailbox_open_request_is_registered_and_rejects_trailing_bytes()
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var registration = Assert.Single(provider.GetServices<GameMessageDecoderRegistration>(),
            x => x.Selector == 1 && x.Type == 0x0060);
        var decoder = (IGameMessageDecoder)provider.GetRequiredService(registration.DecoderType);
        Assert.IsType<MailboxOpenRequest>(decoder.Decode(GameMessageMetadata.Empty, []));
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, [0]));
    }

    [Fact]
    public void Mailbox_ack_bodies_have_exact_success_and_error_shapes()
    {
        Assert.Equal(Convert.FromHexString("01EFCDAB40"), Body(new MailboxClaimEncoder().Encode(new MailboxClaimResponse(0x40ABCDEF))));
        Assert.Equal(Convert.FromHexString("0004"), Body(new MailboxClaimEncoder().Encode(new MailboxClaimResponse(0x40ABCDEF, 4))));
        Assert.Equal(Convert.FromHexString("01785634120200"), Body(new MailboxActionEncoder().Encode(new MailboxActionResponse(0x12345678, 2))));
        Assert.Equal(Convert.FromHexString("0015"), Body(new MailboxActionEncoder().Encode(new MailboxActionResponse(0x12345678, 2, 21))));
        Assert.Equal(Convert.FromHexString("010700"), Body(new MailboxOpenEncoder().Encode(new MailboxOpenResponse(7))));
        Assert.Equal(Convert.FromHexString("0015"), Body(new MailboxOpenEncoder().Encode(new MailboxOpenResponse(7, 21))));
    }

    [Fact]
    public void Recipient_query_preserves_raw_name_bytes_and_the_regular_result_flags()
    {
        var name = Encoding.UTF8.GetBytes("小明");
        var decoder = new MailboxQueryDecoder();
        var request = Assert.IsType<MailboxQueryRequest>(decoder.Decode(GameMessageMetadata.Empty,
            [6, 0, 0, 0, .. name]));
        Assert.Equal(name, request.Name);
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, [2, 0, 0, 0, .. name]));
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, [20, 0, 0, 0, .. Enumerable.Repeat((byte)'x', 20)]));
        Assert.IsType<MailboxQueryRequest>(decoder.Decode(GameMessageMetadata.Empty,
            [19, 0, 0, 0, .. Enumerable.Repeat((byte)'x', 19)]));

        var body = Body(new MailboxQueryEncoder().Encode(new MailboxQueryResponse(
            new MailRecipient(new CharacterId(2), name, 50, 12, 2))));
        Assert.Equal(Convert.FromHexString("0106000000E5B08FE6988E0032000C020000"), body);
        Assert.Equal(Convert.FromHexString("0015"), Body(new MailboxQueryEncoder().Encode(new MailboxQueryResponse(null))));
    }

    [Fact]
    public void Two_plain_letters_consume_all_summary_blocks_and_each_detail_terminal()
    {
        static CharacterMailboxMessageEntryState Letter(int id, string body) => new(
            id, 0, Encoding.UTF8.GetBytes("DNFadmin"), 1, 0,
            Encoding.UTF8.GetBytes("notice"), Encoding.UTF8.GetBytes(body),
            0, 3600, 1770000000 + id, 1, []);
        var encoded = StateSynchronizationPayloadCodec.EncodeMailbox(new(0, [Letter(11, "first"), Letter(12, "second")]));
        using var reader = new BinaryReader(new MemoryStream(encoded));
        Assert.Equal(2, reader.ReadByte());
        Assert.Equal(0, reader.ReadByte());
        foreach (var id in new[] { 11, 12 })
        {
            Assert.Equal(id, reader.ReadInt32());
            Assert.Equal(0, reader.ReadInt32());
            Assert.Equal(string.Empty, ReadDstr(reader));
            Assert.Equal(0, reader.ReadInt32()); // no gold
            Assert.Equal(0, reader.ReadInt32()); // no item template
            Assert.Equal(0, reader.ReadByte());
            reader.ReadBytes(4 + 2 + 1 + 4 + 1 + 1 + 2 + 4);
            Assert.Equal(0, reader.ReadByte()); // no emblem IDs
            reader.ReadBytes(2);
            Assert.Equal(0, reader.ReadByte()); // no random options
            reader.ReadBytes(3 + 2 + 5);
            Assert.Equal(4, reader.ReadInt32());
            Assert.Equal(new byte[4], reader.ReadBytes(4));
            Assert.Equal(8, reader.ReadInt32());
            Assert.Equal(new byte[8], reader.ReadBytes(8));
            Assert.Equal(0, reader.ReadByte());
            Assert.Equal(3600, reader.ReadInt32());
            Assert.Equal(id, reader.ReadInt32());
            Assert.Equal(0, reader.ReadByte());
        }
        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(2, reader.ReadUInt16());
        foreach (var (id, content) in new[] { (11, "first"), (12, "second") })
        {
            Assert.Equal(id, reader.ReadInt32());
            Assert.Equal(0, reader.ReadInt32());
            Assert.Equal(string.Empty, ReadDstr(reader));
            Assert.Equal("notice\n" + content, ReadDstr(reader));
            Assert.Equal(1770000000 + id, reader.ReadInt32());
            Assert.Equal(1, reader.ReadUInt16());
            Assert.Equal(0, reader.ReadByte());
        }
        Assert.Equal(encoded.Length, reader.BaseStream.Position);
    }

    private static byte[] Body(IReadOnlyList<byte[]> frames) => Assert.Single(frames)[15..];
    private static string ReadDstr(BinaryReader reader) => Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
}
