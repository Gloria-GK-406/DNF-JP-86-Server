using Dfo.Server.Application.Execution;
using Dfo.Server.Protocol.Game.Creatures;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.Creatures;

/// <summary>What the Creatures slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class CreaturesRegistration
{
    internal static IServiceCollection AddCreaturesSlice(this IServiceCollection services)
    {
        services.AddTypedGameplayTimer<CreatureClockService>();
        services.AddInternalCommandHandler<AdvanceCreatureTickCommand, bool, AdvanceCreatureTickCommandHandler>(internalActionCancellation: InternalActionCancellationCapability.ConfirmedRollbackWriteResult);
        services.AddGameNotiMessageEncoder<CreatureExperienceNotification, CreatureExperienceEncoder>();
        services.AddGameNotiMessageEncoder<CreatureStateNotification, CreatureStateEncoder>();
        services.AddGameNotiMessageEncoder<CreatureDeathNotification, CreatureDeathEncoder>();
        services.AddGameNotiMessageEncoder<CreatureRevivalNotification, CreatureRevivalEncoder>();
        services.AddGameNotiMessageEncoder<CreatureRenameNotification, CreatureRenameEncoder>();
        services.AddGameNotiMessageEncoder<CreatureEvolutionNotification, CreatureEvolutionEncoder>();
        services.AddGameNotiMessageEncoder<CreatureResponseNotification, CreatureResponseEncoder>();
        services.AddGameCmdMessageDecoder<HatchCreatureMessageDecoder>(ClientMessageType.GameHatchCreature);
        services.AddGameCmdMessageDecoder<RenameCreatureMessageDecoder>(ClientMessageType.GameRenameCreature);
        services.AddGameCmdMessageDecoder<CreatureResponseReportDecoder>(ClientMessageType.GameCreatureResponseReport);
        services.AddGameCmdMessageDecoder<CreatureScriptMessageReportDecoder>(ClientMessageType.GameCreatureScriptMessage);
        services.AddGameCmdMessageEncoder<RenameCreatureResponse, RenameCreatureResponseEncoder>();
        services.AddGameCmdMessageEncoder<HatchCreatureResponse, HatchCreatureResponseEncoder>();
        services.AddProtocolCommandHandler<GameHatchCreatureCommand, bool, GameHatchCreatureCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameHatchCreature, typeof(HatchCreatureRequest)));
        services.AddProtocolCommandHandler<GameRenameCreatureCommand, bool, GameRenameCreatureCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameRenameCreature, typeof(RenameCreatureRequest)));
        services.RegisterProtocolMessageHandler<RenameCreatureHandler, RenameCreatureRequest, GameRenameCreatureCommand>(ClientMessageType.GameRenameCreature);
        services.RegisterProtocolMessageHandler<HatchCreatureHandler, HatchCreatureRequest, GameHatchCreatureCommand>(ClientMessageType.GameHatchCreature);
        services.AddProtocolCommandHandler<GameCreatureResponseReportCommand, bool, GameCreatureResponseReportCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameCreatureResponseReport, typeof(CreatureResponseReport)));
        services.RegisterProtocolMessageHandler<CreatureResponseReportHandler, CreatureResponseReport, GameCreatureResponseReportCommand>(ClientMessageType.GameCreatureResponseReport);
        services.AddProtocolCommandHandler<GameCreatureScriptMessageCommand, bool, GameCreatureScriptMessageCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameCreatureScriptMessage, typeof(CreatureScriptMessageReport)));
        services.RegisterProtocolMessageHandler<CreatureScriptMessageReportHandler, CreatureScriptMessageReport, GameCreatureScriptMessageCommand>(ClientMessageType.GameCreatureScriptMessage);
        return services;
    }
}
