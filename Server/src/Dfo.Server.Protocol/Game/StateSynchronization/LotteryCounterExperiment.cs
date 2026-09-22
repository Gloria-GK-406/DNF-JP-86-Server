using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

/// <summary>Opt-in client experiment, not a persistent quota or a business rule.</summary>
public sealed record LotteryCounterExperimentOptions(bool Enabled, byte Current, byte Threshold);

internal sealed record LotteryCounterStateMessage(byte Current, byte Threshold)
    : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0353;
    internal override byte[] EncodeBody()
    {
        // A21 expands both wire bytes with movsx; avoid projecting negative values.
        if (Current > sbyte.MaxValue || Threshold > sbyte.MaxValue)
            throw new InvalidDataException("Lottery counter projection must be in 0..127.");
        return [Current, Threshold];
    }
}
