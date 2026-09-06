namespace Dfo.Server.Abstractions.World;

/// <summary>
/// The rolls one occasion makes, derived from what the occasion is rather than drawn from a
/// shared generator.
/// </summary>
/// <remarks>
/// <b>This is not the game's own generator and does not claim to be.</b> Nothing anywhere can
/// check it: the client never sees a roll, only what a roll produced, and no capture distinguishes
/// one sequence of the right shape from another. What is required of it is only that the same
/// occasion asked twice answers twice the same -- a room re-described mid-run has to say the same
/// thing it said the first time -- and that two occasions do not march in step.
/// <para>
/// Derived rather than allocated, for the same reason the actor numbers and the floor drops'
/// discriminators are: a run holds no record of a roll, so a second description has nothing to
/// look up and has to be able to work the answer out again.
/// </para>
/// <para>
/// SplitMix64, which is the state update of a generator rather than a hash. It is used here for
/// its avalanche: seeds that differ by one -- the next actor in the room, the room next door --
/// have to produce sequences with nothing in common, and a weaker mix would give neighbouring
/// monsters visibly related drops.
/// </para>
/// </remarks>
public sealed class DropRandom
{
    private const ulong GoldenGamma = 0x9E37_79B9_7F4A_7C15;
    private const ulong MixA = 0xBF58_476D_1CE4_E5B9;
    private const ulong MixB = 0x94D0_49BB_1331_11EB;

    private ulong _state;

    public DropRandom(ulong seed) => this._state = seed;

    /// <summary>
    /// The seed one monster's drops are rolled from.
    /// </summary>
    /// <remarks>
    /// Every part of what makes this occasion this occasion goes in: which run, which map, which
    /// cell of that run's maze, and which actor. The cell rather than the map alone, because a
    /// maze may lead through the same map twice and the two rooms are different occasions -- the
    /// same reason a ground drop is keyed by cell.
    /// </remarks>
    public static ulong SeedFor(int runSeed, int roomMapId, int cellX, int cellY, int actorSequence)
    {
        var seed = Mix((uint)runSeed);
        seed = Mix(seed ^ ((ulong)(uint)roomMapId * GoldenGamma));
        seed = Mix(seed ^ ((ulong)(uint)cellX << 32 | (uint)cellY));
        return Mix(seed ^ ((ulong)(uint)actorSequence * MixA));
    }

    /// <summary>The next roll, from one up to that bound, both included.</summary>
    /// <remarks>
    /// One-based because the archive's ladders are: a rarity group's first band covers rolls up to
    /// its first threshold, and a roll of zero would belong to no band at all.
    /// <para>
    /// The modulo is taken without rejection sampling. The bias it leaves is on the order of the
    /// bound over 2^64 -- for the largest bound these ladders use, about five parts in a hundred
    /// million million -- which is far below the resolution of anything that reads these numbers.
    /// </para>
    /// </remarks>
    public int Next(int inclusiveUpperBound)
    {
        if (inclusiveUpperBound <= 1)
            return inclusiveUpperBound <= 0 ? 0 : 1;

        return 1 + (int)(this.NextUInt64() % (ulong)inclusiveUpperBound);
    }

    /// <summary>Whether a roll of one to <paramref name="basis"/> came in at or under the rate.</summary>
    public bool Hits(int rate, int basis) => rate > 0 && this.Next(basis) <= rate;

    private ulong NextUInt64()
    {
        this._state += GoldenGamma;
        return Mix(this._state);
    }

    private static ulong Mix(ulong value)
    {
        value = (value ^ (value >> 30)) * MixA;
        value = (value ^ (value >> 27)) * MixB;
        return value ^ (value >> 31);
    }
}
