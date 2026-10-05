using System.Text.Json;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

/// <summary>
/// Applies what a dungeon left behind for a character, before that character is entered.
/// </summary>
/// <remarks>
/// An effect is claimed, carried out, and marked -- and every one of those steps names the lease
/// this recovery took, so an effect another recovery has claimed cannot be finished by this one.
/// That is the whole of what makes the outbox safe, and none of it is about how a row is stored.
///
/// The effects settle with the use case this runs inside rather than one at a time. That differs
/// from what came before, where each effect had a transaction of its own. It converges on the same
/// place: whenever anything fails or is left over, the caller refuses the whole selection, and the
/// use case takes back everything this did -- which is what a per-effect rollback amounted to. What
/// is lost is partial progress across a failed selection, and the next attempt does that work
/// again.
/// </remarks>
internal sealed class DungeonEffectRecoveryService(
    IDungeonEffectOutboxTable outbox,
    IAccountTable accounts,
    ICharacterTable characters,
    IExperienceRuleService experienceRules,
    IDungeonEffectRuleService rules, ICharacterProgressionDomainService progression) : IDungeonPersistentEffectRecoveryRepository
{
    /// <summary>How long a claim holds before another recovery may take the effect.</summary>
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(1);

    public async ValueTask<DungeonPersistentEffectRecoveryOutcome> RecoverAsync(
        AccountId accountId, CharacterId characterId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var waiting = await outbox.ReadClaimableAsync(
            characterId, now.ToUnixTimeMilliseconds(), cancellationToken).ConfigureAwait(false);

        var committed = 0;
        var dead = 0;
        var failed = 0;
        foreach (var effect in waiting)
        {
            var leaseId = Guid.NewGuid().ToString("N");
            var claimedAt = DateTimeOffset.UtcNow;
            if (!await outbox.TryClaimAsync(
                    effect, leaseId, claimedAt.Add(Lease).ToUnixTimeMilliseconds(),
                    claimedAt.ToUnixTimeMilliseconds(), cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            if (!rules.CanCarryOut(
                    effect.PayloadVersion, effect.AccountId, effect.EffectScope, effect.ScopeTarget,
                    effect.EffectKind, accountId.Value, characterId.Value))
            {
                await outbox.SettleAsync(
                    effect, 4,
                    "Unsupported persistent effect kind, version, scope, target, or owner.",
                    "{}", leaseId, cancellationToken).ConfigureAwait(false);
                dead++;
                continue;
            }

            try
            {
                var resultJson = effect.EffectKind == DungeonEffectKinds.LuckyStar
                    ? await GrantLuckyStarAsync(effect, accountId, characterId, cancellationToken)
                        .ConfigureAwait(false)
                    : await ApplyExperienceAsync(effect, accountId, characterId, cancellationToken)
                        .ConfigureAwait(false);
                await outbox.SettleAsync(effect, 2, string.Empty, resultJson, leaseId, cancellationToken)
                    .ConfigureAwait(false);
                committed++;
            }
            catch (Exception error) when (error is InvalidDataException or JsonException)
            {
                // Permanent validation failures are detected before a domain change is accepted, so
                // the dead letter goes through the same claim and nothing half-done is left behind.
                await outbox.SettleAsync(
                    effect, 4, error.Message, "{}", leaseId, CancellationToken.None)
                    .ConfigureAwait(false);
                dead++;
            }
            catch
            {
                failed++;
            }
        }

        return new DungeonPersistentEffectRecoveryOutcome(
            committed, dead, failed,
            await outbox.CountUnfinishedAsync(characterId, cancellationToken).ConfigureAwait(false));
    }

    private async ValueTask<string> GrantLuckyStarAsync(
        DungeonEffectOutboxRow effect,
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<LuckyStarPayload>(effect.PayloadJson, Lenient)
            ?? throw new InvalidDataException("Dungeon persistent effect payload is invalid.");
        if (payload.CharacterId != characterId.Value || payload.AccountId != accountId.Value
            || payload.Amount != 1)
        {
            throw new InvalidDataException("Dungeon persistent effect payload identity is invalid.");
        }

        var current = await accounts.FindLuckyStarAsync(accountId, cancellationToken)
            .ConfigureAwait(false) ?? 0;
        var (granted, newTotal) = rules.GrantLuckyStar(current);
        if (granted)
        {
            RequireExactOwner(await accounts.SetLuckyStarAsync(accountId, newTotal, cancellationToken)
                .ConfigureAwait(false));
        }

        return JsonSerializer.Serialize(new { granted, newTotal });
    }

    private async ValueTask<string> ApplyExperienceAsync(
        DungeonEffectOutboxRow effect,
        AccountId accountId,
        CharacterId characterId,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<ExperiencePayload>(effect.PayloadJson, Lenient)
            ?? throw new InvalidDataException("Dungeon experience payload is invalid.");
        if (payload.CharacterId != characterId.Value || payload.AccountId != accountId.Value)
        {
            throw new InvalidDataException("Dungeon experience payload identity is invalid.");
        }

        var stored = await characters.FindProgressAsync(characterId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("Dungeon experience character is unavailable.");
        if (stored.Level != payload.ExpectedDatabaseLevel
            || stored.Experience != payload.ExpectedDatabaseExp)
        {
            throw new InvalidDataException(
                "Dungeon experience expected progress does not match persisted progress.");
        }

        // The stored pair above is solely the stale-write guard. Settlement carries the
        // authoritative in-dungeon progress separately; calculating from the stored pair would
        // discard experience accumulated in the live dungeon session.
        var policy = experienceRules.Progression();
        var split = rules.SplitExperience(
            new DungeonExperienceSettlement(
                payload.PreviousLevel, payload.PreviousExp, payload.RawGain,
                payload.NormalizeMaxLevelExp),
            policy);

        ulong oldHonour = 0;
        uint oldCapsule = 0;
        if (split.HonorGain > 0)
        {
            // Resolve every required owner before the first write, so a permanent validation
            // failure can be dead-lettered without a partial change having been accepted.
            var totals = await accounts.FindExperienceAsync(accountId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidDataException("Dungeon experience account is unavailable.");
            oldHonour = checked((ulong)Math.Max(0, totals.HonourExperience));
            oldCapsule = checked((uint)Math.Max(0, totals.GrowthCapsuleExperience));
        }

        if (!await progression.TryApplyAsync(characterId,
                new(checked((byte)stored.Level), checked((uint)stored.Experience)),
                new(checked((byte)split.NewLevel), checked((uint)split.NewExp)), cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("Dungeon experience character is unavailable.");
        }

        ulong totalHonour = 0;
        uint totalCapsule = 0;
        uint appliedCapsule = 0;
        if (split.HonorGain > 0)
        {
            var accrued = rules.AccumulateHonour(split.HonorGain, oldHonour, oldCapsule, policy);
            totalHonour = accrued.TotalHonor;
            totalCapsule = accrued.TotalCapsule;
            appliedCapsule = accrued.AppliedCapsule;
            RequireExactOwner(await accounts.SetExperienceAsync(
                accountId, checked((long)totalHonour), totalCapsule, cancellationToken)
                .ConfigureAwait(false));
        }

        return JsonSerializer.Serialize(new
        {
            rawGain = payload.RawGain,
            honorExpGain = split.HonorGain,
            normalExpGain = split.NormalGain,
            previousLevel = payload.PreviousLevel,
            previousExp = payload.PreviousExp,
            newLevel = split.NewLevel,
            newExp = split.NewExp,
            normalizedMaxLevelExp = split.NormalizedMaxLevelExp,
            persisted = split.Persisted,
            growthCapsuleExpGain = appliedCapsule,
            totalHonorExp = totalHonour,
            totalGrowthCapsuleExp = totalCapsule,
        });
    }

    /// <summary>
    /// A change to an account has to have landed on exactly that account.
    /// </summary>
    /// <remarks>
    /// Nothing else would say so. An effect whose account is not there updates no row and reports
    /// success, and the outbox would then record it as carried out with a result saying the
    /// player got something they did not. It is a dead letter instead.
    /// </remarks>
    private static void RequireExactOwner(int touched)
    {
        if (touched != 1)
        {
            throw new InvalidDataException(
                "Dungeon persistent effect mutation did not affect its exact owner.");
        }
    }

    private static readonly JsonSerializerOptions Lenient =
        new() { PropertyNameCaseInsensitive = true };

    private sealed record LuckyStarPayload(
        int CharacterId, int AccountId, int DungeonId, int ClearLevel, int Amount);

    private sealed record ExperiencePayload(
        int CharacterId, int AccountId, byte PreviousLevel, uint PreviousExp, uint RawGain,
        bool NormalizeMaxLevelExp, byte ExpectedDatabaseLevel, uint ExpectedDatabaseExp);
}
