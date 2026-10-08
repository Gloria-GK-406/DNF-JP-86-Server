using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Achievements;

internal sealed class AchievementCharacterFactsDomainService(ICharacterDomainService characters,
    ICharacterTable owners, ICharacterQuestLogDomainService quests) : IAchievementCharacterFactsDomainService
{
    public async ValueTask<AchievementCharacterFacts> ReadAsync(CharacterId character, CancellationToken ct)
    {
        var progress = await characters.Get(character).ProgressAsync(ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Achievement character is unavailable.");
        var account = await owners.FindOwnerAsync(character, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Achievement character owner is unavailable.");
        var cleared = await quests.LogOf(account, character).ClearedAsync(ct).ConfigureAwait(false);
        return new(progress.Level, cleared.Select(x => x.QuestId).ToHashSet());
    }
}
