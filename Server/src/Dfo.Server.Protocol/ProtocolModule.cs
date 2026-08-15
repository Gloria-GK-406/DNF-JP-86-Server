using Dfo.Server.Abstractions;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Channel;
using Dfo.Server.Protocol.Channel.Connect;
using Dfo.Server.Protocol.Channel.Info;
using Dfo.Server.Protocol.Channel.Script;
using Dfo.Server.Protocol.Channel.ScriptVersion;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.AccountRoster;
using Dfo.Server.Protocol.Game.ClientHardwareDetail;
using Dfo.Server.Protocol.Game.ClientSpecProfile;
using Dfo.Server.Protocol.Game.ClientSpecStatistic;
using Dfo.Server.Protocol.Game.Exit;
using Dfo.Server.Protocol.Game.InitialConnection;
using Dfo.Server.Protocol.Game.Login;
using Dfo.Server.Protocol.Game.RosterFollowUp;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.SessionMetrics;
using Dfo.Server.Protocol.Game.TaggedIdReport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Protocol;

/// <summary>
/// Registers the protocol slices: every wire key that can be decoded and every outbound
/// message that can be encoded.
/// </summary>
/// <remarks>
/// This module is the only place a protocol is wired. Every artifact a protocol needs -- its
/// messages, its decoder, its encoder, its message handler, its command and that command's
/// handler -- lives in that protocol's own folder, and the tables below are where those artifacts
/// become reachable.
/// </remarks>
public sealed class ProtocolModule : IServerModule
{
    public string Name => "Protocol";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton<ISelectedCharacterStateMessageFactory, SelectedCharacterStateMessageFactoryService>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<INetworkConnectionObserver, OnlineUserConnectionObserver>());
        RegisterInboundDecoders(services);
        RegisterOutboundEncoders(services);
        RegisterCommands(services);
        RegisterMessageHandlers(services);
    }

    /// <summary>The commands a protocol slice owns, with their execution kind and access modes.</summary>
    private static void RegisterCommands(IServiceCollection services)
    {
        services.AddCommandHandler<EstablishChannelConnectionCommand, EstablishChannelConnectionResult, EstablishChannelConnectionCommandHandler>(
            CommandExecutionKind.Direct);
        services.AddCommandHandler<GetChannelScriptVersionCommand, GetChannelScriptVersionResult, GetChannelScriptVersionCommandHandler>(
            CommandExecutionKind.Direct);
        services.AddCommandHandler<GetChannelScriptCommand, GetChannelScriptResult, GetChannelScriptCommandHandler>(
            CommandExecutionKind.Direct);
        services.AddCommandHandler<GetChannelInfoCommand, GetChannelInfoResult, GetChannelInfoCommandHandler>(
            CommandExecutionKind.Direct);
        services.AddCommandHandler<AcknowledgeGameExitCommand, AcknowledgeGameExitResult, AcknowledgeGameExitCommandHandler>(
            CommandExecutionKind.Direct);
        services.AddCommandHandler<LoginGameUserCommand, LoginGameUserResult, LoginGameUserCommandHandler>(
            CommandExecutionKind.Direct,
            UserAccessMode.Exclusive);
        services.AddCommandHandler<GetAccountCharacterRosterCommand, AccountCharacterRoster, GetAccountCharacterRosterCommandHandler>(
            CommandExecutionKind.Direct,
            accountAccessMode: UserAccessMode.Shared);
        services.AddCommandHandler<PrepareSelectedCharacterSessionCommand, PreparedSelectedCharacterSession, PrepareSelectedCharacterSessionCommandHandler>(
            CommandExecutionKind.Workflow,
            accountAccessMode: UserAccessMode.Exclusive);
        services.AddCommandHandler<ApplySelectedCharacterDailyRefillCommand, EntryInventoryPreparation, ApplySelectedCharacterDailyRefillCommandHandler>(
            CommandExecutionKind.Workflow,
            accountAccessMode: UserAccessMode.Exclusive);
        services.AddCommandHandler<PublishSelectedCharacterSessionCommand, PublishedSelectedCharacterSession, PublishSelectedCharacterSessionCommandHandler>(
            CommandExecutionKind.Workflow,
            accountAccessMode: UserAccessMode.Exclusive);
        services.AddCommandHandler<MarkSelectedCharacterPvpReadyCommand, SelectedCharacterPvpPublication, MarkSelectedCharacterPvpReadyCommandHandler>(
            CommandExecutionKind.Workflow,
            accountAccessMode: UserAccessMode.Exclusive);
        services.AddCommandHandler<SynchronizeSelectedCharacterQuestsCommand, SynchronizedSelectedCharacterQuests, SynchronizeSelectedCharacterQuestsCommandHandler>(
            CommandExecutionKind.Workflow,
            accountAccessMode: UserAccessMode.Exclusive);
        services.AddCommandHandler<ReviveSelectedCharacterPetCommand, RevivedSelectedCharacterPet, ReviveSelectedCharacterPetCommandHandler>(
            CommandExecutionKind.Workflow,
            accountAccessMode: UserAccessMode.Exclusive);
        services.AddCommandHandler<PlanSelectedCharacterRejoinCommand, PlannedSelectedCharacterRejoin, PlanSelectedCharacterRejoinCommandHandler>(
            CommandExecutionKind.Workflow,
            accountAccessMode: UserAccessMode.Exclusive);
        services.AddCommandHandler<CompensateSelectedCharacterSessionCommand, CompensatedSelectedCharacterSession, CompensateSelectedCharacterSessionCommandHandler>(
            CommandExecutionKind.Workflow,
            accountAccessMode: UserAccessMode.Exclusive);
    }

    /// <summary>Binds each catalogue member to the handler that consumes it.</summary>
    private static void RegisterMessageHandlers(IServiceCollection services)
    {
        services.RegisterMessageHandler<ChannelConnectHandler>(ClientMessageType.ChannelConnect);
        services.RegisterMessageHandler<ChannelScriptVersionHandler>(ClientMessageType.ChannelScriptVersion);
        services.RegisterMessageHandler<ChannelScriptHandler>(ClientMessageType.ChannelScript);
        services.RegisterMessageHandler<ChannelInfoHandler>(ClientMessageType.ChannelInfo);
        services.RegisterMessageHandler<GameExitHandler>(ClientMessageType.GameExit);
        services.RegisterMessageHandler<GameLoginHandler>(ClientMessageType.GameLogin);
        services.RegisterMessageHandler<GetAccountCharacterRosterHandler>(
            ClientMessageType.GameGetAccountCharacterRoster);
        services.RegisterMessageHandler<SelectCharacterHandler>(ClientMessageType.GameSelectCharacter);
        services.RegisterMessageHandler<ClientSpecStatisticHandler>(ClientMessageType.GameClientSpecStatistic);
        services.RegisterMessageHandler<ClientHardwareDetailHandler>(ClientMessageType.GameClientHardwareDetail);
        services.RegisterMessageHandler<ClientSpecProfileHandler>(ClientMessageType.GameClientSpecProfile);
        services.RegisterMessageHandler<SessionMetricsHandler>(ClientMessageType.GameSessionMetrics);
        services.RegisterMessageHandler<TaggedIdReportHandler>(ClientMessageType.GameTaggedIdReport);
        services.RegisterMessageHandler<RosterFollowUpHandler>(ClientMessageType.GameRosterFollowUp);
    }

    /// <summary>
    /// The inbound routing table: one line per protocol, binding its wire key to its decoder and
    /// declaring which catalogue members that decoder can produce.
    /// </summary>
    /// <remarks>
    /// Registration is unconditional on purpose. The declared set is what the startup coverage
    /// check reads, and that check must hold whether or not this process is listening.
    ///
    /// Most lines declare the one catalogue member named after their wire key. GET_USERINFO does
    /// not: type 0x0008 produces the account roster request, which carries a synthesised catalogue
    /// value with no wire number of its own.
    ///
    /// Every Game line below registers under <see cref="Game.GameProtocol.Command"/>, so that is
    /// currently the only Game command byte any wire key here carries. The command is part of the
    /// key rather than a separate condition, so a frame carrying another command matches nothing
    /// registered here.
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
    /// server code, so its type is known at the call site.
    ///
    /// The endpoint column is the whole endpoint rule. It used to be nine `when` clauses spliced
    /// into the encoding switch, where no reader could check the routing without reading the bytes.
    /// </remarks>
    private static void RegisterOutboundEncoders(IServiceCollection services)
    {
        StateSynchronizationEncoderRegistration.AddStateSynchronizationEncoders(services);
        SupplementalSynchronizationEncoderRegistration.AddSupplementalSynchronizationEncoders(services);
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
    }
}
