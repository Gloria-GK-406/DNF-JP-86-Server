using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Application.Parties;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game.Party;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game.Party;

public sealed class PartyCommandCodecTests
{
    [Theory]
    [InlineData("000302000000000500020000", "", "030002000000000500020000")]
    [InlineData("000604000000000500020000", "", "060004000000000500020000")]
    [InlineData(
        "01000C000000E697A0E9989FE4BC8DE5908D04000000000500020000",
        "E697A0E9989FE4BC8DE5908D",
        "000104000000000500020000")]
    public void Current_a21_set_party_info_shapes_project_to_canonical_s2c_order(
        string bodyHex,
        string titleHex,
        string settingsHex)
    {
        var request = Assert.IsType<SetPartyInfoRequest>(
            new SetPartyInfoDecoder(new PartyModeRuleService()).Decode(
                GameMessageMetadata.Empty,
                Hex(bodyHex)));

        Assert.Equal(Hex(titleHex), request.Title.ToBytes());
        Assert.Equal(Hex(settingsHex), request.Settings.ToWireBytes());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(13)]
    [InlineData(15)]
    [InlineData(17)]
    public void Set_party_info_rejects_unknown_or_trailing_lengths(int length) =>
        Assert.Throws<InvalidDataException>(() =>
            new SetPartyInfoDecoder(new PartyModeRuleService()).Decode(
                GameMessageMetadata.Empty,
                new byte[length]));

    [Fact]
    public void Set_party_info_rejects_negative_overflowing_truncated_and_invalid_utf8_titles()
    {
        var decoder = new SetPartyInfoDecoder(new PartyModeRuleService());

        Assert.Throws<InvalidDataException>(() => decoder.Decode(
            GameMessageMetadata.Empty,
            Hex("0100FFFFFFFF0004000000000500020000")));
        Assert.Throws<InvalidDataException>(() => decoder.Decode(
            GameMessageMetadata.Empty,
            Hex("0100FFFFFF7F0004000000000500020000")));
        Assert.Throws<InvalidDataException>(() => decoder.Decode(
            GameMessageMetadata.Empty,
            Hex("01000400000041420004000000000500020000")));
        Assert.Throws<InvalidDataException>(() => decoder.Decode(
            GameMessageMetadata.Empty,
            Hex("010001000000FF04000000000500020000")));
    }

    [Theory]
    [InlineData("03000000000004000000000500020000")]
    [InlineData("01010000000004000000000500020000")]
    [InlineData("000302000000000500030000")]
    [InlineData("000300000000000500020000")]
    [InlineData("000305000000000500020000")]
    [InlineData("000304000000000500020100")]
    public void Set_party_info_rejects_invalid_edit_gates_or_rule_values(string bodyHex)
    {
        Assert.Throws<InvalidDataException>(() =>
            new SetPartyInfoDecoder(new PartyModeRuleService()).Decode(
                GameMessageMetadata.Empty,
                Hex(bodyHex)));
    }

    [Theory]
    [InlineData("05AC11D001AC11D0010079C00500000C000000646162333266356439643466", 121)]
    [InlineData("05AC11D001AC11D00108F9C00500000C000000646162333266356439643466", 2297)]
    public void Current_a21_endpoint_samples_decode_the_fixed_prefix(string bodyHex, ushort port)
    {
        var request = Assert.IsType<SetUdpIpPortRequest>(
            new SetUdpIpPortDecoder().Decode(GameMessageMetadata.Empty, Hex(bodyHex)));

        Assert.Equal(5, request.Endpoint.NatType);
        Assert.Equal(new(172, 17, 208, 1), request.Endpoint.InnerAddress);
        Assert.Equal(new(172, 17, 208, 1), request.Endpoint.OuterAddress);
        Assert.Equal(port, request.Endpoint.Port);
        Assert.Equal(1472u, request.Endpoint.Mtu);
        Assert.Equal(Hex("0C000000646162333266356439643466"), request.IdentityTail);
    }

    [Theory]
    [InlineData("F57A", 62842)]
    [InlineData("D8AD", 55469)]
    public void Live_socket_correlated_endpoint_port_uses_network_byte_order(
        string wirePortHex,
        ushort expectedPort)
    {
        var body = Hex("05C0A80866C0A80866" + wirePortHex + "C0050000");

        var request = Assert.IsType<SetUdpIpPortRequest>(
            new SetUdpIpPortDecoder().Decode(GameMessageMetadata.Empty, body));

        Assert.Equal(expectedPort, request.Endpoint.Port);
    }

    [Fact]
    public void Mtu_discovery_fallback_of_512_is_a_valid_a21_endpoint_report()
    {
        var request = Assert.IsType<SetUdpIpPortRequest>(
            new SetUdpIpPortDecoder().Decode(
                GameMessageMetadata.Empty,
                Hex("05C0A80866C0A80866D0B8000200000C000000646162333266356439643466")));

        Assert.Equal(5, request.Endpoint.NatType);
        Assert.Equal(new(192, 168, 8, 102), request.Endpoint.InnerAddress);
        Assert.Equal(new(192, 168, 8, 102), request.Endpoint.OuterAddress);
        Assert.Equal(53432, request.Endpoint.Port);
        Assert.Equal(512u, request.Endpoint.Mtu);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(14)]
    public void Endpoint_requires_its_complete_fixed_prefix(int length) =>
        Assert.Throws<InvalidDataException>(() =>
            new SetUdpIpPortDecoder().Decode(GameMessageMetadata.Empty, new byte[length]));

    [Theory]
    [InlineData("050011D001AC11D0010079C0050000")]
    [InlineData("05AC11D0010011D0010079C0050000")]
    [InlineData("057F0000017F0000010079C0050000")]
    [InlineData("05AC11D001AC11D0010000C0050000")]
    [InlineData("05AC11D001AC11D00100793F020000")]
    [InlineData("05AC11D001AC11D001007900000100")]
    public void Invalid_endpoint_values_are_rejected(string bodyHex) =>
        Assert.Throws<InvalidDataException>(() =>
            new SetUdpIpPortDecoder().Decode(GameMessageMetadata.Empty, Hex(bodyHex)));

    [Theory]
    [InlineData("02000040000000", 64, 0)]
    [InlineData("020000400000000000", 64, 2)]
    public void Request_peer_accepts_only_the_known_seven_or_nine_byte_shapes(
        string bodyHex,
        int peerValue,
        int tailLength)
    {
        var request = Assert.IsType<RequestPeerRequest>(
            new RequestPeerDecoder().Decode(GameMessageMetadata.Empty, Hex(bodyHex)));

        Assert.Equal((ushort)2, request.TargetWireUserId);
        Assert.Equal(0, request.RequestType);
        Assert.Equal(peerValue, request.PeerValue);
        Assert.Equal(tailLength, request.Tail.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(10)]
    public void Request_peer_rejects_unknown_lengths(int length) =>
        Assert.Throws<InvalidDataException>(() =>
            new RequestPeerDecoder().Decode(GameMessageMetadata.Empty, new byte[length]));

    [Theory]
    [InlineData("02000040000000", 0)]
    [InlineData("02000000000000000000000000000000", 9)]
    public void Response_peer_accepts_only_the_known_seven_or_sixteen_byte_shapes(
        string bodyHex,
        int tailLength)
    {
        var response = Assert.IsType<ResponsePeerRequest>(
            new ResponsePeerDecoder().Decode(GameMessageMetadata.Empty, Hex(bodyHex)));

        Assert.Equal((ushort)2, response.InviterWireUserId);
        Assert.Equal(0, response.RequestType);
        Assert.Equal(tailLength, response.Tail.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(17)]
    public void Response_peer_rejects_unknown_lengths(int length) =>
        Assert.Throws<InvalidDataException>(() =>
            new ResponsePeerDecoder().Decode(GameMessageMetadata.Empty, new byte[length]));

    [Fact]
    public void Leave_party_requires_an_empty_body()
    {
        Assert.IsType<LeavePartyRequest>(
            new LeavePartyDecoder().Decode(GameMessageMetadata.Empty, []));
        Assert.Throws<InvalidDataException>(() =>
            new LeavePartyDecoder().Decode(GameMessageMetadata.Empty, [0]));
    }

    [Fact]
    public void Member_removal_and_leadership_transfer_each_decode_one_stable_slot()
    {
        Assert.Equal((byte)3, Assert.IsType<RemovePartyMemberRequest>(
            new RemovePartyMemberDecoder().Decode(GameMessageMetadata.Empty, [3]))!.TargetSlot);
        Assert.Equal((byte)1, Assert.IsType<ChangePartyLeaderRequest>(
            new ChangePartyLeaderDecoder().Decode(GameMessageMetadata.Empty, [1]))!.TargetSlot);
        Assert.Throws<InvalidDataException>(() =>
            new RemovePartyMemberDecoder().Decode(GameMessageMetadata.Empty, []));
        Assert.Throws<InvalidDataException>(() =>
            new ChangePartyLeaderDecoder().Decode(GameMessageMetadata.Empty, [1, 2]));
        Assert.Equal([0x00, 0x13], RemovePartyMemberRefusalEncoder.EncodeBody());
    }

    [Fact]
    public void Party_realtime_query_requires_an_empty_body()
    {
        Assert.IsType<CallPartyMemberRealtimeInfoRequest>(
            new CallPartyMemberRealtimeInfoDecoder().Decode(GameMessageMetadata.Empty, []));
        Assert.Throws<InvalidDataException>(() =>
            new CallPartyMemberRealtimeInfoDecoder().Decode(GameMessageMetadata.Empty, [0]));
    }

    [Fact]
    public void Production_registers_all_eight_wire_keys_and_handlers_plus_seven_party_commands()
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        var decoders = provider.GetServices<GameMessageDecoderRegistration>().ToArray();
        Assert.Contains(decoders, value => value.Selector == 1 && value.Type == 0x0002
            && value.DecoderType == typeof(SetUdpIpPortDecoder));
        Assert.Contains(decoders, value => value.Selector == 1 && value.Type == 0x000A
            && value.DecoderType == typeof(RequestPeerDecoder));
        Assert.Contains(decoders, value => value.Selector == 1 && value.Type == 0x000B
            && value.DecoderType == typeof(ResponsePeerDecoder));
        Assert.Contains(decoders, value => value.Selector == 1 && value.Type == 0x000D
            && value.DecoderType == typeof(LeavePartyDecoder));
        Assert.Contains(decoders, value => value.Selector == 1 && value.Type == 0x000E
            && value.DecoderType == typeof(RemovePartyMemberDecoder));
        Assert.Contains(decoders, value => value.Selector == 1 && value.Type == 0x0079
            && value.DecoderType == typeof(ChangePartyLeaderDecoder));
        Assert.Contains(decoders, value => value.Selector == 1 && value.Type == 0x00A6
            && value.DecoderType == typeof(CallPartyMemberRealtimeInfoDecoder));
        Assert.Contains(decoders, value => value.Selector == 1 && value.Type == 0x000C
            && value.DecoderType == typeof(SetPartyInfoDecoder));

        var handlers = provider.GetServices<MessageHandlerRegistration>().ToArray();
        Assert.Contains(handlers, value => value.MessageType == ClientMessageType.GameSetUdpIpPort
            && value.HandlerType == typeof(SetUdpIpPortHandler));
        Assert.Contains(handlers, value => value.MessageType == ClientMessageType.GameRequestPeer
            && value.HandlerType == typeof(RequestPeerHandler));
        Assert.Contains(handlers, value => value.MessageType == ClientMessageType.GameResponsePeer
            && value.HandlerType == typeof(ResponsePeerHandler));
        Assert.Contains(handlers, value => value.MessageType == ClientMessageType.GameLeaveParty
            && value.HandlerType == typeof(LeavePartyHandler));
        Assert.Contains(handlers, value =>
            value.MessageType == ClientMessageType.GameRemovePartyMember
            && value.HandlerType == typeof(RemovePartyMemberHandler));
        Assert.Contains(handlers, value =>
            value.MessageType == ClientMessageType.GameChangePartyLeader
            && value.HandlerType == typeof(ChangePartyLeaderHandler));
        Assert.Contains(handlers, value =>
            value.MessageType == ClientMessageType.GameCallPartyMemberRealtimeInfo
            && value.HandlerType == typeof(CallPartyMemberRealtimeInfoHandler));
        Assert.Contains(handlers, value => value.MessageType == ClientMessageType.GameSetPartyInfo
            && value.HandlerType == typeof(SetPartyInfoHandler));

        Type[] expectedCommands =
        [
            typeof(RequestPartyInviteCommand),
            typeof(AcceptPartyInviteCommand),
            typeof(LeavePartyCommand),
            typeof(TransferPartyLeadershipCommand),
            typeof(RemovePartyMemberCommand),
            typeof(QueryPartyRealtimeCommand),
            typeof(SetPartyInfoCommand),
        ];
        var commands = provider.GetServices<CommandHandlerRegistration>()
            .Where(value => expectedCommands.Contains(value.CommandType))
            .ToArray();
        Assert.Equal(expectedCommands.Length, commands.Length);
        Assert.All(commands, value =>
        {
            Assert.Equal(CommandExecutionKind.Direct, value.ExecutionKind);
            Assert.Equal(UserAccessMode.Unscoped, value.OnlineUserAccessMode);
            Assert.Equal(UserAccessMode.Unscoped, value.AccountAccessMode);
        });

        Assert.Contains(
            provider.GetServices<ServerMessageEncoderRegistration>(),
            value => value.MessageType == typeof(PartySettingsMessage)
                && value.EncoderType == typeof(PartySettingsEncoder));
        Assert.Contains(
            provider.GetServices<NotificationHandlerRegistration>(),
            value => value.NotificationType == typeof(PartySettingsChanged)
                && value.HandlerType == typeof(PartySettingsChangedHandler));
        Assert.Contains(
            provider.GetServices<NotificationHandlerRegistration>(),
            value => value.NotificationType == typeof(PartyLeadershipChanged)
                && value.HandlerType == typeof(PartyLeadershipChangedHandler));
        Assert.Contains(
            provider.GetServices<ServerMessageEncoderRegistration>(),
            value => value.MessageType == typeof(RemovePartyMemberRefusal)
                && value.EncoderType == typeof(RemovePartyMemberRefusalEncoder));
    }

    private static byte[] Hex(string value) => Convert.FromHexString(value);
}
