using System.Runtime.CompilerServices;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildRecruitmentSessionDomainService(IGuildDomainService guilds, GuildRuleService rules)
    : IGuildRecruitmentSessionDomainService
{
    // The selected session directory owns the key. A retired selection cannot pick up its
    // authorization again, and its draft can be collected with the old session.
    private readonly ConditionalWeakTable<SelectedSessionIdentity, CreationDraft> drafts = new();

    public async ValueTask<GuildMutationStatus> StartAsync(SelectedSessionIdentity session, string? name, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(GuildRecruitmentSessionDomainService) + "." + nameof(StartAsync));
        drafts.Remove(session);
        if (name is null)
            return GuildMutationStatus.InvalidName;
        var result = await guilds.CheckCreationAsync(session.CharacterId, name, ct).ConfigureAwait(false);
        if (result == GuildMutationStatus.Success)
            drafts.Add(session, new(name));
        return result;
    }

    public bool SetPromotion(SelectedSessionIdentity session, string? promotion)
    {
        GameplayWriteAssertion.Writing(nameof(GuildRecruitmentSessionDomainService) + "." + nameof(SetPromotion));
        if (!drafts.TryGetValue(session, out var draft))
            return false;
        draft.Permitted = false;
        draft.Promotion = promotion is not null && rules.IsValidText(promotion) ? promotion : null;
        return draft.Promotion is not null;
    }

    public bool Authorize(SelectedSessionIdentity session)
    {
        GameplayWriteAssertion.Writing(nameof(GuildRecruitmentSessionDomainService) + "." + nameof(Authorize));
        if (!drafts.TryGetValue(session, out var draft) || draft.Promotion is null)
            return false;
        draft.Permitted = true;
        return true;
    }

    public GuildCreationAuthorization? AuthorizedCreation(SelectedSessionIdentity session) =>
        drafts.TryGetValue(session, out var draft) && draft is { Permitted: true, Promotion: { } promotion }
            ? new(draft.Name, promotion)
            : null;

    public void Cancel(SelectedSessionIdentity session)
    {
        GameplayWriteAssertion.Writing(nameof(GuildRecruitmentSessionDomainService) + "." + nameof(Cancel));
        drafts.Remove(session);
    }

    private sealed class CreationDraft(string name)
    {
        public string Name { get; } = name;
        public string? Promotion { get; set; }
        public bool Permitted { get; set; }
    }
}
