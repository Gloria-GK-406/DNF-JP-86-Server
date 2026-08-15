using Dfo.Server.Abstractions;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Networking.Configuration;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Networking.Framing.Decoders;
using Dfo.Server.Networking.Framing.Encoders;
using Dfo.Server.Networking.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TouchSocket.Core;
using TouchSocket.Hosting;
using TouchSocket.Sockets;

namespace Dfo.Server.Networking;

public sealed class NetworkingModule : IServerModule
{
    public string Name => "Networking";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(NetworkingOptions.SectionName);
        var gamePorts = section.GetSection(nameof(NetworkingOptions.GamePorts))
            .GetChildren()
            .Select(value => int.TryParse(value.Value, out var port) ? port : -1)
            .ToArray();
        var options = new NetworkingOptions
        {
            Enabled = bool.TryParse(section[nameof(NetworkingOptions.Enabled)], out var enabled) && enabled,
            ChannelPort = int.TryParse(section[nameof(NetworkingOptions.ChannelPort)], out var channelPort)
                ? channelPort
                : 7001,
            GamePorts = gamePorts.Length == 0 ? new[] { 10011 } : gamePorts
        };

        ValidatePort(options.ChannelPort, $"{NetworkingOptions.SectionName}:{nameof(NetworkingOptions.ChannelPort)}");
        foreach (var gamePort in options.GamePorts)
        {
            ValidatePort(gamePort, $"{NetworkingOptions.SectionName}:{nameof(NetworkingOptions.GamePorts)}");
        }

        services.AddSingleton(options);
        services.AddSingleton(GameLoginOptions.FromConfiguration(configuration));
        services.TryAddSingleton<TouchSocketConnectionRegistry>();
        services.TryAddSingleton<ISelectCharacterProjectionComposer, SelectCharacterProjectionComposer>();
        services.TryAddSingleton<INetworkMessageSender, TouchSocketNetworkMessageSender>();
        services.TryAddSingleton<INetworkConnectionCloser, TouchSocketNetworkConnectionCloser>();
        services.TryAddSingleton<ChannelNetworkPlugin>();
        services.TryAddSingleton<GameNetworkPlugin>();
        services.TryAddSingleton<GameMessageDecoderRegistry>();
        services.TryAddSingleton<ChannelMessageDecoderRegistry>();
        services.TryAddSingleton<ServerMessageEncoderRegistry>();
        RegisterInboundDecoders(services);
        RegisterOutboundEncoders(services);

        if (!options.Enabled)
        {
            return;
        }

        services.AddTcpService<IChannelTcpService, ChannelTcpService>(config => config
            .SetListenIPHosts(options.ChannelPort)
            .SetSingleStreamDataHandlingAdapter(() => new ChannelFrameDataHandlingAdapter())
            .ConfigurePlugins(plugins => plugins.Add<ChannelNetworkPlugin>()));

        services.AddTcpService<IGameTcpService, GameTcpService>(config => config
            .SetListenIPHosts(options.GamePorts.Select(port => new IPHost(port)).ToArray())
            .SetSingleStreamDataHandlingAdapter(() => new GameClientFrameDataHandlingAdapter())
            .ConfigurePlugins(plugins => plugins.Add<GameNetworkPlugin>()));
    }

    /// <summary>
    /// The inbound routing table: one line per protocol, binding its wire key to its decoder and
    /// declaring which catalogue members that decoder can produce.
    /// </summary>
    /// <remarks>
    /// Registration happens before the <c>Enabled</c> check on purpose. The declared set is what
    /// the startup coverage check reads, and that check must hold whether or not this process is
    /// listening.
    ///
    /// Most lines declare the one catalogue member named after their wire key. GET_USERINFO does
    /// not: type 0x0008 produces the account roster request, which carries a synthesised catalogue
    /// value with no wire number of its own.
    /// </remarks>
    private static void RegisterInboundDecoders(IServiceCollection services)
    {
        services.AddGameMessageDecoder<GameLoginMessageDecoder>(
            GameProtocol.Command, GameProtocol.LoginType, ClientMessageType.GameLogin);
        services.AddGameMessageDecoder<GameExitMessageDecoder>(
            GameProtocol.Command, GameProtocol.ExitType, ClientMessageType.GameExit);
        services.AddGameMessageDecoder<SelectCharacterMessageDecoder>(
            GameProtocol.Command, GameProtocol.SelectCharacterType, ClientMessageType.GameSelectCharacter);
        services.AddGameMessageDecoder<GetUserInfoMessageDecoder>(
            GameProtocol.Command, GameProtocol.GetUserInfoType, ClientMessageType.GameGetAccountCharacterRoster);
        services.AddGameMessageDecoder<ClientSpecStatisticMessageDecoder>(
            GameProtocol.Command, GameProtocol.ClientSpecStatisticType, ClientMessageType.GameClientSpecStatistic);
        services.AddGameMessageDecoder<ClientHardwareDetailMessageDecoder>(
            GameProtocol.Command, GameProtocol.ClientHardwareDetailType, ClientMessageType.GameClientHardwareDetail);
        services.AddGameMessageDecoder<ClientSpecProfileMessageDecoder>(
            GameProtocol.Command, GameProtocol.ClientSpecProfileType, ClientMessageType.GameClientSpecProfile);
        services.AddGameMessageDecoder<SessionMetricsMessageDecoder>(
            GameProtocol.Command, GameProtocol.SessionMetricsType, ClientMessageType.GameSessionMetrics);
        services.AddGameMessageDecoder<TaggedIdReportMessageDecoder>(
            GameProtocol.Command, GameProtocol.TaggedIdReportType, ClientMessageType.GameTaggedIdReport);
        services.AddGameMessageDecoder<RosterFollowUpMessageDecoder>(
            GameProtocol.Command, GameProtocol.RosterFollowUpType, ClientMessageType.GameRosterFollowUp);
        services.AddChannelMessageDecoder<ChannelScriptVersionMessageDecoder>(
            ChannelProtocol.ScriptVersionMessageNumber, ClientMessageType.ChannelScriptVersion);
        services.AddChannelMessageDecoder<ChannelScriptMessageDecoder>(
            ChannelProtocol.ScriptMessageNumber, ClientMessageType.ChannelScript);
        services.AddChannelMessageDecoder<ChannelConnectMessageDecoder>(
            ChannelProtocol.ConnectMessageNumber, ClientMessageType.ChannelConnect);
        services.AddChannelMessageDecoder<ChannelInfoMessageDecoder>(
            ChannelProtocol.InfoMessageNumber, ClientMessageType.ChannelInfo);
    }

    /// <summary>
    /// The outbound routing table: one line per message, binding it to its encoder and to the
    /// endpoint allowed to receive it.
    /// </summary>
    /// <remarks>
    /// Keyed by message CLR type rather than by catalogue member. An outbound message is built by
    /// server code, so its type is known at the call site; and the catalogue could not key this
    /// table anyway, because the select-character response and its projection delivery share one
    /// catalogue value.
    ///
    /// The endpoint column is the whole endpoint rule. It used to be nine `when` clauses spliced
    /// into the encoding switch, where no reader could check the routing without reading the bytes.
    /// </remarks>
    private static void RegisterOutboundEncoders(IServiceCollection services)
    {
        services.AddServerMessageEncoder<ChannelConnectResponse, ChannelConnectResponseEncoder>(
            NetworkEndpointKind.Channel);
        services.AddServerMessageEncoder<ChannelScriptVersionResponse, ChannelScriptVersionResponseEncoder>(
            NetworkEndpointKind.Channel);
        services.AddServerMessageEncoder<ChannelScriptResponse, ChannelScriptResponseEncoder>(
            NetworkEndpointKind.Channel);
        services.AddServerMessageEncoder<ChannelInfoResponse, ChannelInfoResponseEncoder>(
            NetworkEndpointKind.Channel);
        services.AddServerMessageEncoder<GameExitResponse, GameExitResponseEncoder>(
            NetworkEndpointKind.Game);
        services.AddServerMessageEncoder<GameInitialConnectionNotice, GameInitialConnectionNoticeEncoder>(
            NetworkEndpointKind.Game);
        services.AddServerMessageEncoder<GameLoginResponse, GameLoginResponseEncoder>(
            NetworkEndpointKind.Game);
        services.AddServerMessageEncoder<AccountCharacterRosterResponse, AccountCharacterRosterResponseEncoder>(
            NetworkEndpointKind.Game);
        services.AddServerMessageEncoder<SelectCharacterResponse, SelectCharacterResponseEncoder>(
            NetworkEndpointKind.Game);
    }

    private static void ValidatePort(int port, string settingName)
    {
        if (port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"{settingName} must contain ports between 1 and 65535.");
        }
    }
}
