using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Events;

/// <summary>How the online-time reward pays: every full <see cref="Interval"/> in the world, these amounts to the account.</summary>
/// <param name="Notice">Whether the character is also shown a server notice naming what was paid.</param>
public sealed record OnlineTimeRewardRule(bool Enabled, TimeSpan Interval, int Cera, int TokenCera, bool Notice)
{
    public static OnlineTimeRewardRule Disabled { get; } = new(false, TimeSpan.FromMinutes(5), 0, 0, false);
}

/// <summary>Reads the deployment's online-time reward settings; the only reader of them.</summary>
public interface IOnlineTimeRewardRuleService
{
    OnlineTimeRewardRule Rule { get; }
}

/// <summary>
/// Times each session's stay in the world and pays the online-time reward when a period completes.
/// </summary>
/// <remarks>
/// The clock is the session, not the character: entering the world again, on any connection or as
/// another character, starts a new period. Nothing is persisted, so a restart drops the partial period.
/// </remarks>
public interface IOnlineTimeRewardDomainService
{
    /// <summary>Forgets every session that is not in <paramref name="inWorld"/>; called once per round before <see cref="AccrueAsync"/>.</summary>
    void Retain(IReadOnlyCollection<SelectedSessionIdentity> inWorld);

    /// <summary>
    /// Starts the session's clock on first sight, and pays once when its period has completed.
    /// Requires the caller's writable transaction.
    /// </summary>
    /// <returns>True when something was paid.</returns>
    ValueTask<bool> AccrueAsync(SelectedSessionIdentity session, CancellationToken ct);
}

/// <summary>An online-time reward was paid to the session's account.</summary>
/// <remarks>Carries the session's exact identity so the live notice reaches only the stay that earned it.</remarks>
public sealed record OnlineTimeRewardGranted(AccountId AccountId, CharacterId CharacterId, string ConnectionId,
    long ConnectionGeneration, int Cera, int TokenCera, bool Notice) : IDomainEvent;
