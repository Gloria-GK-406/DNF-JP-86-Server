using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;

namespace Dfo.Server.Application.Marriage;

internal sealed class MarriageGuestbookRuleService : IMarriageGuestbookRuleService
{ public int MaximumBytes => 20; public int MaximumComments => 100; }

/// <summary>A relation's guestbook comments.</summary>
internal sealed class Guestbook(IMarriageGuestbookDomainStore store, IMarriageGuestbookRuleService rules,
    ICharacterDomainService characters, TimeProvider clock)
{
    public bool Accepts(byte[] text) => text.Length != 0 && text.Length <= rules.MaximumBytes && !text.Any(x => x < 32 || x == 127);

    public ValueTask<IReadOnlyList<MarriageGuestComment>> ReadAsync(Guid relation, CancellationToken ct) => store.ReadAsync(relation, ct);

    public async ValueTask<int> PostAsync(Guid relation, CharacterId author, byte[] text, CancellationToken ct)
    {
        if ((await store.ReadAsync(relation, ct).ConfigureAwait(false)).Count >= rules.MaximumComments) return -1;
        if (await characters.Get(author).IdentityAsync(ct).ConfigureAwait(false) is not { } identity) return -1;
        await store.AddAsync(relation, author, identity.Name, text, checked((uint)clock.GetUtcNow().ToUnixTimeSeconds()), ct).ConfigureAwait(false);
        return 0;
    }

    public ValueTask<bool> DeleteAsync(Guid relation, uint id, CancellationToken ct) => store.DeleteAsync(relation, id, ct);
}
