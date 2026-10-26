using System.Buffers.Binary;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

internal sealed record ItemUsageCountMessage(IReadOnlyList<ItemUsageState> Items) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x021E;
    internal override byte[] EncodeBody()
    {
        var body = new byte[checked(1 + Items.Count * 5)];
        body[0] = checked((byte)Items.Count);
        for (var i = 0; i < Items.Count; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(1 + i * 5), Items[i].ItemId);
            body[5 + i * 5] = checked((byte)Items[i].UsedCount);
        }
        return body;
    }
    internal static IReadOnlyList<ItemUsageCountMessage> FullSnapshot(IReadOnlyList<ItemUsageState> items)
    {
        // Zero clears both client maps. Nonempty packets upsert, so chunking does not erase earlier chunks.
        var messages = new List<ItemUsageCountMessage> { new([]) };
        foreach (var chunk in items.Where(item => item.DailyLimit is not null).ToArray().Chunk(byte.MaxValue)) messages.Add(new(chunk));
        // Validate the whole delivery before sending its reset frame. Never silently truncate a count.
        foreach (var message in messages) _ = message.EncodeBody();
        return messages;
    }
}

public sealed record ItemUsageSnapshotResult(IReadOnlyList<ItemUsageState> Items, bool IsCurrent)
{
    public DateTimeOffset At { get; init; }
}
internal static class ItemUsageSnapshotReader
{
    internal static async ValueTask<ItemUsageSnapshotResult> ReadAsync(IItemUsageDomainService usage,
        ISelectedSessionDomainService sessions, TimeProvider clock, SelectedSessionIdentity session, CancellationToken token)
    {
        if (!sessions.IsInWorld(session)) return new([], false);
        var at = clock.GetUtcNow();
        return new(await usage.GetSnapshotAtAsync(session.CharacterId, at, token).ConfigureAwait(false), true) { At = at };
    }
}
internal static class ItemUsageCooldownProjection
{
    internal static IReadOnlyList<ItemCooldownStateMessage> Snapshot(IReadOnlyList<ItemUsageState> items, DateTimeOffset at)
    {
        var entries = items.Where(item => item.CooldownUntil > at).OrderBy(item => item.ItemId)
            .Select((item, index) => new InventoryItemValueState(index, item.ItemId,
                checked((int)Math.Ceiling((item.CooldownUntil!.Value - at).TotalSeconds)))).ToArray();
        // 00AC upserts cooldowns. Do not invent a reset packet or restart an expired cooldown.
        return entries.Chunk(byte.MaxValue).Select(chunk => new ItemCooldownStateMessage(chunk)).ToArray();
    }
}
