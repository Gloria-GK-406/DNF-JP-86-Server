using System.Runtime.CompilerServices;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildNameOperationSessionDomainService(IGuildMembershipDomainService guilds,
    IGuildRenameDomainService renaming)
    : IGuildNameOperationSessionDomainService
{
    // Replacing the slot detaches any earlier check that is still awaiting its result.
    private readonly ConditionalWeakTable<SelectedSessionIdentity, NameCheck> checks = new();

    public async ValueTask<GuildMutationStatus> StartAsync(SelectedSessionIdentity session, string? name, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(GuildNameOperationSessionDomainService) + "." + nameof(StartAsync));
        if (name is null)
        {
            checks.Remove(session);
            return GuildMutationStatus.InvalidName;
        }

        var check = new NameCheck();
        checks.AddOrUpdate(session, check);
        var result = await guilds.CheckCreationAsync(session.CharacterId, name, ct).ConfigureAwait(false);
        if (result == GuildMutationStatus.AlreadyMember)
        {
            var rename = await renaming.CheckAsync(session.CharacterId, name, ct).ConfigureAwait(false);
            if (rename.Authorization is { } authorization)
                check.Operation = new Rename(authorization);
            return rename.Status;
        }
        if (result == GuildMutationStatus.Success)
            check.Operation = new CreationDraft(name);
        return result;
    }

    public bool SetPromotion(SelectedSessionIdentity session, string? promotion)
    {
        GameplayWriteAssertion.Writing(nameof(GuildNameOperationSessionDomainService) + "." + nameof(SetPromotion));
        if (OperationFor(session) is not CreationDraft draft)
            return false;
        draft.Permitted = false;
        draft.Promotion = promotion is not null && GuildProfileRuleService.AllowsCreationPromotion(promotion) ? promotion : null;
        return draft.Promotion is not null;
    }

    public bool Authorize(SelectedSessionIdentity session)
    {
        GameplayWriteAssertion.Writing(nameof(GuildNameOperationSessionDomainService) + "." + nameof(Authorize));
        if (OperationFor(session) is not CreationDraft draft || draft.Promotion is null)
            return false;
        draft.Permitted = true;
        return true;
    }

    public GuildCreationAuthorization? AuthorizedCreation(SelectedSessionIdentity session) =>
        OperationFor(session) is CreationDraft { Permitted: true, Promotion: { } promotion } draft
            ? new(draft.Name, promotion)
            : null;

    public GuildRenameAuthorization? CheckedRename(SelectedSessionIdentity session) =>
        (OperationFor(session) as Rename)?.Authorization;

    public void Cancel(SelectedSessionIdentity session)
    {
        GameplayWriteAssertion.Writing(nameof(GuildNameOperationSessionDomainService) + "." + nameof(Cancel));
        checks.Remove(session);
    }

    private NameOperation? OperationFor(SelectedSessionIdentity session) =>
        checks.TryGetValue(session, out var check) ? check.Operation : null;

    private sealed class NameCheck
    {
        public NameOperation? Operation { get; set; }
    }

    private abstract class NameOperation;
    private sealed class Rename(GuildRenameAuthorization authorization) : NameOperation
    {
        public GuildRenameAuthorization Authorization { get; } = authorization;
    }

    private sealed class CreationDraft(string name) : NameOperation
    {
        public string Name { get; } = name;
        public string? Promotion { get; set; }
        public bool Permitted { get; set; }
    }
}
