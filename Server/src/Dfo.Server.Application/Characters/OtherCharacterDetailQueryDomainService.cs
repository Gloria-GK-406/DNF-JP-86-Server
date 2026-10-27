using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Characters;

internal sealed class OtherCharacterDetailQueryDomainService(
    ISelectedSessionDomainService sessions,
    ISelectedCharacterCurrentViewDomainService currentViews,
    TimeProvider clock) : IOtherCharacterDetailQueryDomainService
{
    public async ValueTask<CharacterDetailQueryResult> QueryAsync(
        SelectedSessionIdentity requester,
        ushort targetWireUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requester);
        if (targetWireUserId == ushort.MaxValue)
            return CharacterDetailQueryResult.Reject(CharacterDetailQueryFailure.InvalidTarget);

        if (!sessions.IsInWorld(requester))
            return CharacterDetailQueryResult.Reject(
                CharacterDetailQueryFailure.RequesterNotCurrent);

        if (!sessions.TryGetInWorld(targetWireUserId, out var target))
            return CharacterDetailQueryResult.Reject(CharacterDetailQueryFailure.TargetNotCurrent);
        if (target.ListenerPort != requester.ListenerPort)
            return CharacterDetailQueryResult.Reject(CharacterDetailQueryFailure.DifferentListener);

        var targetState = await currentViews.TryLoadAsync(target, cancellationToken)
            .ConfigureAwait(false);
        if (targetState is null)
            return CharacterDetailQueryResult.Reject(
                CharacterDetailQueryFailure.TargetRuntimeUnavailable);

        // Revalidate after reading the independently synchronized runtime directory. Either
        // generation can have been displaced between the first lookup and that snapshot read.
        if (!sessions.IsInWorld(requester))
            return CharacterDetailQueryResult.Reject(
                CharacterDetailQueryFailure.RequesterNotCurrent);
        if (!sessions.IsInWorld(target))
        {
            return CharacterDetailQueryResult.Reject(
                CharacterDetailQueryFailure.TargetNotCurrent);
        }

        if (targetState.Character.Identity.CharacterId != target.CharacterId)
            return CharacterDetailQueryResult.Reject(
                CharacterDetailQueryFailure.TargetRuntimeUnavailable);

        return CharacterDetailQueryResult.Success(
            new OtherCharacterDetailProjection(
                requester, target, targetState, clock.GetUtcNow()));
    }
}
