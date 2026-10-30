using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.LearnSkill;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.LearnSkill;

/// <summary>What the LearnSkill slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class LearnSkillRegistration
{
    internal static IServiceCollection AddLearnSkillSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<LearnSkillMessageDecoder>(ClientMessageType.GameLearnSkill);
        services.AddGameCmdMessageEncoder<LearnSkillResponse, LearnSkillResponseEncoder>();
        services.AddProtocolCommandHandler<GameChangeSkillSlotCommand, bool, GameChangeSkillSlotCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameChangeSkillSlot, typeof(ChangeSkillSlotRequest)));
        services.RegisterProtocolMessageHandler<ChangeSkillSlotHandler, ChangeSkillSlotRequest, GameChangeSkillSlotCommand>(ClientMessageType.GameChangeSkillSlot);
        services.AddGameCmdMessageDecoder<ChangeSkillSlotDecoder>(ClientMessageType.GameChangeSkillSlot);
        services.AddGameCmdMessageEncoder<ChangeSkillSlotResponse, ChangeSkillSlotEncoder>();
        services.AddProtocolCommandHandler<GameSwitchSkillBuildCommand, bool, GameSwitchSkillBuildCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSwitchSkillBuild, typeof(SwitchSkillBuildRequest)));
        services.RegisterProtocolMessageHandler<SwitchSkillBuildHandler, SwitchSkillBuildRequest, GameSwitchSkillBuildCommand>(ClientMessageType.GameSwitchSkillBuild);
        services.AddGameCmdMessageDecoder<SwitchSkillBuildDecoder>(ClientMessageType.GameSwitchSkillBuild);
        services.AddGameCmdMessageEncoder<SwitchSkillBuildResponse, SwitchSkillBuildEncoder>();
        services.AddProtocolCommandHandler<GameLearnSkillCommand, CharacterSkillLearningResult, GameLearnSkillCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameLearnSkill, typeof(LearnSkillRequest)));
        services.RegisterProtocolMessageHandler<LearnSkillHandler, LearnSkillRequest, GameLearnSkillCommand>(ClientMessageType.GameLearnSkill);
        return services;
    }
}
