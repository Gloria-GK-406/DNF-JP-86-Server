using System.Collections.Immutable;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Effects;

internal enum EffectOperationState { Pending, Committed, ConfirmedRollback, Rejected, Blocked }

internal sealed class EffectOperationEntry(SelectedSessionIdentity session, long ordinal,
    byte selector, ushort type, ushort sequence, string hash, int length)
{
    public SelectedSessionIdentity Session { get; } = session;
    public long Ordinal { get; } = ordinal;
    public byte Selector { get; } = selector;
    public ushort Type { get; } = type;
    public ushort Sequence { get; } = sequence;
    public string Hash { get; } = hash;
    public int Length { get; } = length;
    public EffectOperationState State { get; set; }
    public ReplayProjectionRequest? Projection { get; set; }
}
