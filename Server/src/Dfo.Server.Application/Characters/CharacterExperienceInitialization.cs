using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;

namespace Dfo.Server.Application.Characters;

internal sealed class CharacterExperienceInitializationDomainService(IExperienceRuleService rules,
    ICharacterExperienceStoragePolicy storage) : ICharacterExperienceInitializationDomainService
{
    public async ValueTask<CharacterExperienceScale> InitializeAsync(CancellationToken cancellationToken)
    {
        var scale = rules.CharacterScale();
        await storage.InstallAsync(scale, cancellationToken).ConfigureAwait(false);
        return scale;
    }
}
public sealed record InitializeCharacterExperienceStorageCommand : ICommand<CharacterExperienceScale>;
internal sealed class InitializeCharacterExperienceStorageCommandHandler(ICharacterExperienceInitializationDomainService progression)
    : ICommandHandler<InitializeCharacterExperienceStorageCommand, CharacterExperienceScale>
{
    public ValueTask<CharacterExperienceScale> HandleAsync(CommandExecutionContext context,
        InitializeCharacterExperienceStorageCommand command, CancellationToken token) => progression.InitializeAsync(token);
}
