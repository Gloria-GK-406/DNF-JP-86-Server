using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.LearnSkill;

public sealed record LearnSkillCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session,
    byte PageIndex, IReadOnlyList<CharacterSkillLearningChange> Changes, byte Mode)
    : ICommand<CharacterSkillLearningResult>, IRequestSettlementCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed class LearnSkillCommandHandler(ICharacterSkillLearningDomainService learning)
    : ICommandHandler<LearnSkillCommand, CharacterSkillLearningResult>
{
    public ValueTask<CharacterSkillLearningResult> HandleAsync(CommandExecutionContext context,
        LearnSkillCommand command, CancellationToken cancellationToken) =>
        learning.LearnAsync(command.Session, command.PageIndex, command.Changes, command.Mode, context.LegacyFootprint, cancellationToken);
}
