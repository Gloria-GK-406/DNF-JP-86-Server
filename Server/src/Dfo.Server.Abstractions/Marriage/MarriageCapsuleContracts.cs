using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Marriage;

public sealed record MarriageCapsuleRules(int MaximumOwned, int MaximumPending, int Gain, int MaximumEnergy, int ItemId);
public interface IMarriageCapsuleContentSource { MarriageCapsuleRules Load(); }

/// <summary>The archive's capsule limits, and which clears charge a capsule.</summary>
public interface IMarriageCapsuleRuleService
{
    MarriageCapsuleRules Rules();

    /// <summary>Whether clearing this dungeon at this level is a recommended clear that charges energy.</summary>
    ValueTask<bool> ChargesOnClearAsync(int dungeonId, int level, CancellationToken ct);
}
public interface IMarriageCapsuleDomainStore
{
    ValueTask<MarriageCapsuleState> ReadAsync(Guid relation, CharacterId character, CancellationToken ct);
    ValueTask SaveAsync(Guid relation, CharacterId character, MarriageCapsuleState state, CancellationToken ct);
    ValueTask<bool> CreditAsync(Guid relation, CharacterId character, DungeonRunId run, int gain, int maximum, CancellationToken ct);
}
public interface IMarriageCapsuleQueryDomainService
{ ValueTask<MarriageCapsuleSnapshot> ReadAsync(AccountId account, CharacterId character, CancellationToken ct); }
public interface IMarriageCapsuleDomainService
{
    ValueTask<MarriageCapsuleResult> CapsuleAsync(SelectedSessionIdentity actor, MarriageCapsuleAction action,
        IReadOnlyList<AccountId> accounts, IEffectFootprintRecorder footprint, CancellationToken ct);
}
public sealed record MarriageCapsuleEnergyChanged(CharacterId Character, int Energy) : IDomainEvent;
public sealed record MarriageCapsuleGiftChanged(CharacterId Character) : IDomainEvent;
