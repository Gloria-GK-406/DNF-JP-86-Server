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
using Dfo.Server.Protocol.Game.AuctionService;
using Dfo.Server.Protocol.Game.CharacterName;
using Dfo.Server.Protocol.Game.InformNotice;
using Dfo.Server.Protocol.Game.ClientHardwareDetail;
using Dfo.Server.Protocol.Game.ClientSpecProfile;
using Dfo.Server.Protocol.Game.ClientSpecStatistic;
using Dfo.Server.Protocol.Game.ContentsPlayInfo;
using Dfo.Server.Protocol.Game.CreateCharacter;
using Dfo.Server.Protocol.Game.DeleteCharacter;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.Exit;
using Dfo.Server.Protocol.Game.HiddenCharacterInfo;
using Dfo.Server.Protocol.Game.HonorLevel;
using Dfo.Server.Protocol.Game.InitialConnection;
using Dfo.Server.Protocol.Game.Login;
using Dfo.Server.Protocol.Game.MapEntryReports;
using Dfo.Server.Protocol.Game.MercenaryInfo;
using Dfo.Server.Protocol.Game.RosterFollowUp;
using Dfo.Server.Protocol.Game.SaveGameOption;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
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

        services.AddGameMessageSending();
        services.TryAddSingleton<ChannelProtocolKeyProvider>();
        services.TryAddSingleton(SelectCharacterDeliveryOptions.ReferenceSequence);
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
        services.AddCommandHandler<CheckCharacterNameCommand, CharacterNameStatus, CheckCharacterNameCommandHandler>(
            CommandExecutionKind.Direct,
            accountAccessMode: UserAccessMode.Shared);

        // Creation and deletion both derive a slot from the account's current roster and then write
        // against it, so they take the account exclusively. Two concurrent creations sharing access
        // would compute the same next slot.
        services.AddCommandHandler<CreateCharacterCommand, CharacterCreationResult, CreateCharacterCommandHandler>(
            CommandExecutionKind.Direct,
            accountAccessMode: UserAccessMode.Exclusive);
        services.AddCommandHandler<DeleteCharacterCommand, CharacterDeletionResult, DeleteCharacterCommandHandler>(
            CommandExecutionKind.Direct,
            accountAccessMode: UserAccessMode.Exclusive);
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
        services.RegisterMessageHandler<CheckCharacterNameHandler>(ClientMessageType.GameCheckCharacterName);
        services.RegisterMessageHandler<CreateCharacterHandler>(ClientMessageType.GameCreateCharacter);
        services.RegisterMessageHandler<DeleteCharacterHandler>(ClientMessageType.GameDeleteCharacter);
        services.RegisterMessageHandler<MercenaryInfoHandler>(ClientMessageType.GameMercenaryInfo);
        services.RegisterMessageHandler<HiddenCharacterInfoHandler>(ClientMessageType.GameHiddenCharacterInfo);
        services.RegisterMessageHandler<SaveGameOption1Handler>(ClientMessageType.GameSaveGameOption1);
        services.RegisterMessageHandler<ContentsPlayInfoHandler>(ClientMessageType.GameContentsPlayInfo);

        // One handler, six wire keys: the map-entry report family.
        services.RegisterMessageHandler<MapEntryReportHandler>(ClientMessageType.GameSetUdpIpPort);
        services.RegisterMessageHandler<MapEntryReportHandler>(ClientMessageType.GameCeraQuery);
        services.RegisterMessageHandler<MapEntryReportHandler>(ClientMessageType.GameInformNoticeSecond);
        services.RegisterMessageHandler<MapEntryReportHandler>(ClientMessageType.GameSecurityStatus);
        services.RegisterMessageHandler<MapEntryReportHandler>(ClientMessageType.GameRuntimeTingStatistic);
        services.RegisterMessageHandler<EnterSelectDungeonHandler>(ClientMessageType.GameEnterSelectDungeon);
        services.RegisterMessageHandler<SelectDungeonHandler>(ClientMessageType.GameSelectDungeon);
        services.RegisterMessageHandler<FinishLoadingHandler>(ClientMessageType.GameFinishLoading);
        services.RegisterMessageHandler<MoveMapHandler>(ClientMessageType.GameMoveMap);

        // Position and area: the only inputs that move a published character, and so the only
        // way the town its next entry reports can change.
        services.RegisterMessageHandler<SetUserPositionHandler>(ClientMessageType.GameSetUserPosition);
        services.RegisterMessageHandler<SetUserAreaHandler>(ClientMessageType.GameSetUserArea);
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
    /// Every client-originated Game line below is explicitly CMD. The selector remains part of the
    /// wire key, so an unregistered selector/type pair matches nothing and is silently ignored.
    /// </remarks>
    private static void RegisterInboundDecoders(IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<GameLoginMessageDecoder>(
            GameProtocol.LoginType, ClientMessageType.GameLogin);
        services.AddGameCmdMessageDecoder<GameExitMessageDecoder>(
            GameProtocol.ExitType, ClientMessageType.GameExit);
        services.AddGameCmdMessageDecoder<SelectCharacterMessageDecoder>(
            GameProtocol.SelectCharacterType, ClientMessageType.GameSelectCharacter);
        services.AddGameCmdMessageDecoder<GetUserInfoMessageDecoder>(
            GameProtocol.GetUserInfoType, ClientMessageType.GameGetAccountCharacterRoster);
        services.AddGameCmdMessageDecoder<ClientSpecStatisticMessageDecoder>(
            GameProtocol.ClientSpecStatisticType, ClientMessageType.GameClientSpecStatistic);
        services.AddGameCmdMessageDecoder<ClientHardwareDetailMessageDecoder>(
            GameProtocol.ClientHardwareDetailType, ClientMessageType.GameClientHardwareDetail);
        services.AddGameCmdMessageDecoder<ClientSpecProfileMessageDecoder>(
            GameProtocol.ClientSpecProfileType, ClientMessageType.GameClientSpecProfile);
        services.AddGameCmdMessageDecoder<SessionMetricsMessageDecoder>(
            GameProtocol.SessionMetricsType, ClientMessageType.GameSessionMetrics);
        services.AddGameCmdMessageDecoder<TaggedIdReportMessageDecoder>(
            GameProtocol.TaggedIdReportType, ClientMessageType.GameTaggedIdReport);
        services.AddGameCmdMessageDecoder<RosterFollowUpMessageDecoder>(
            GameProtocol.RosterFollowUpType, ClientMessageType.GameRosterFollowUp);
        services.AddGameCmdMessageDecoder<CheckCharacterNameMessageDecoder>(
            GameProtocol.CheckCharacterNameType, ClientMessageType.GameCheckCharacterName);
        services.AddGameCmdMessageDecoder<CreateCharacterMessageDecoder>(
            GameProtocol.CreateCharacterType, ClientMessageType.GameCreateCharacter);
        services.AddGameCmdMessageDecoder<DeleteCharacterMessageDecoder>(
            GameProtocol.DeleteCharacterType, ClientMessageType.GameDeleteCharacter);
        services.AddGameCmdMessageDecoder<MercenaryInfoMessageDecoder>(
            GameProtocol.MercenaryInfoType, ClientMessageType.GameMercenaryInfo);
        services.AddGameCmdMessageDecoder<SaveGameOption1MessageDecoder>(
            GameProtocol.SaveGameOption1Type, ClientMessageType.GameSaveGameOption1);
        services.AddGameCmdMessageDecoder<ContentsPlayInfoMessageDecoder>(
            GameProtocol.ContentsPlayInfoType, ClientMessageType.GameContentsPlayInfo);
        services.AddGameCmdMessageDecoder<SetUdpIpPortDecoder>(
            GameProtocol.SetUdpIpPortType, ClientMessageType.GameSetUdpIpPort);
        services.AddGameCmdMessageDecoder<CeraQueryDecoder>(
            GameProtocol.CeraQueryType, ClientMessageType.GameCeraQuery);
        services.AddGameCmdMessageDecoder<SetUserPositionDecoder>(
            GameProtocol.SetUserPositionType, ClientMessageType.GameSetUserPosition);
        services.AddGameCmdMessageDecoder<SetUserAreaDecoder>(
            GameProtocol.SetUserAreaType, ClientMessageType.GameSetUserArea);
        services.AddGameCmdMessageDecoder<InformNoticeSecondDecoder>(
            GameProtocol.InformNoticeSecondType, ClientMessageType.GameInformNoticeSecond);
        services.AddGameCmdMessageDecoder<SecurityStatusDecoder>(
            GameProtocol.SecurityStatusType, ClientMessageType.GameSecurityStatus);
        services.AddGameCmdMessageDecoder<RuntimeTingStatisticDecoder>(
            GameProtocol.RuntimeTingStatisticType, ClientMessageType.GameRuntimeTingStatistic);
        services.AddGameCmdMessageDecoder<EnterSelectDungeonDecoder>(
            GameProtocol.EnterSelectDungeonType, ClientMessageType.GameEnterSelectDungeon);
        services.AddGameCmdMessageDecoder<SelectDungeonDecoder>(
            GameProtocol.SelectDungeonType, ClientMessageType.GameSelectDungeon);
        services.AddGameCmdMessageDecoder<FinishLoadingDecoder>(
            GameProtocol.FinishLoadingType, ClientMessageType.GameFinishLoading);
        services.AddGameCmdMessageDecoder<MoveMapDecoder>(
            GameProtocol.MoveMapType, ClientMessageType.GameMoveMap);
        services.AddGameCmdMessageDecoder<HiddenCharacterInfoMessageDecoder>(
            GameProtocol.HiddenCharacterInfoType, ClientMessageType.GameHiddenCharacterInfo);
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
        services.AddGameCmdMessageEncoder<GameExitResponse, GameExitResponseEncoder>();
        services.AddGameNotiMessageEncoder<GameInitialConnectionNotice, GameInitialConnectionNoticeEncoder>();
        services.AddGameCmdMessageEncoder<GameLoginResponse, GameLoginResponseEncoder>();
        services.AddGameNotiMessageEncoder<AccountCharacterRosterResponse, AccountCharacterRosterResponseEncoder>();
        services.AddGameNotiMessageEncoder<HonorLevelInfoNotice, HonorLevelInfoNoticeEncoder>();
        services.AddGameNotiMessageEncoder<AuctionServiceStateNotice, AuctionServiceStateNoticeEncoder>();
        services.AddGameNotiMessageEncoder<InformNoticeNotice, InformNoticeNoticeEncoder>();
        services.AddGameCmdMessageEncoder<CheckCharacterNameResponse, CheckCharacterNameResponseEncoder>();
        services.AddGameCmdMessageEncoder<CreateCharacterResponse, CreateCharacterResponseEncoder>();
        services.AddGameCmdMessageEncoder<DeleteCharacterResponse, DeleteCharacterResponseEncoder>();
        services.AddGameCmdMessageEncoder<MercenaryInfoResponse, MercenaryInfoResponseEncoder>();
        services.AddGameCmdMessageEncoder<ContentsPlayInfoResponse, ContentsPlayInfoResponseEncoder>();
        services.AddGameNotiMessageEncoder<UserAreaNotification, UserAreaEncoder>();
        services.AddGameNotiMessageEncoder<AreaUsersNotification, AreaUsersEncoder>();
        services.AddGameNotiMessageEncoder<UserStateNotification, UserStateEncoder>();
        services.AddGameNotiMessageEncoder<UdpHostNotification, UdpHostEncoder>();
        services.AddGameNotiMessageEncoder<EnterSelectDungeonNotification, EnterSelectDungeonEncoder>();
        services.AddGameNotiMessageEncoder<DungeonInfoNotification, DungeonInfoEncoder>();
        services.AddGameNotiMessageEncoder<StartMapNotification, StartMapEncoder>();
        services.AddGameNotiMessageEncoder<SupportTagNotification, SupportTagEncoder>();
        services.AddGameCmdMessageEncoder<FinishLoadingResponse, FinishLoadingResponseEncoder>();
        services.AddGameNotiMessageEncoder<LoadingCompleteNotification, LoadingCompleteEncoder>();
        services.AddGameCmdMessageEncoder<HiddenCharacterInfoResponse, HiddenCharacterInfoResponseEncoder>();
        services.AddGameCmdMessageEncoder<RosterFollowUpResponse, RosterFollowUpResponseEncoder>();
    }
}
