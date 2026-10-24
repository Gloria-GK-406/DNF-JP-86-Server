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

public sealed record ReadItemUsageSnapshotCommand(SelectedSessionIdentity Session)
    : ICommand<ItemUsageSnapshotResult>, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed record ItemUsageSnapshotResult(IReadOnlyList<ItemUsageState> Items, bool IsCurrent)
{
    public DateTimeOffset At { get; init; }
}
public sealed class ReadItemUsageSnapshotCommandHandler(IItemUsageDomainService usage, ISelectedSessionDomainService sessions, TimeProvider clock)
    : ICommandHandler<ReadItemUsageSnapshotCommand, ItemUsageSnapshotResult>
{
    public async ValueTask<ItemUsageSnapshotResult> HandleAsync(CommandExecutionContext context,
        ReadItemUsageSnapshotCommand command, CancellationToken token)
    {
        if (!sessions.IsInWorld(command.Session)) return new([], false);
        var at = clock.GetUtcNow();
        return new(await usage.GetSnapshotAtAsync(command.Session.CharacterId, at, token).ConfigureAwait(false), true) { At = at };
    }
}
public sealed class ItemUsageProjectionDelivery(ICommandBus commands, ISelectedSessionDomainService sessions, IGameMessageSender sender)
{
    // Read and delivery (including reset + chunks) run in the caller's job; no other job interleaves them.
    public async ValueTask SendAsync(SelectedSessionIdentity session, CancellationToken token)
    {
        var snapshot = await commands.SendAsync(new ReadItemUsageSnapshotCommand(session), token).ConfigureAwait(false);
        if (!snapshot.IsCurrent || !sessions.IsInWorld(session)) return;
        var messages = ItemUsageCountMessage.FullSnapshot(snapshot.Items).Cast<IGameNotiMessage>()
            .Concat(ItemUsageCooldownProjection.Snapshot(snapshot.Items, snapshot.At)).ToArray();
        foreach (var message in messages)
        {
            if (!sessions.IsInWorld(session)) return;
            await sender.SendNotiAsync(session.ConnectionId, message, token).ConfigureAwait(false);
        }
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

// One server timer only checks online sessions. Offline counters remain untouched and reset on read.
public sealed class ItemUsageDailyRefreshService(ISelectedSessionSnapshotQuery directory,
    ISelectedSessionDomainService sessions,
    IDailyResetRuleService days, TimeProvider clock, ItemUsageProjectionDelivery delivery,
    ILogger<ItemUsageDailyRefreshService> logger, IGameplayQueue queue) : BackgroundService
{
    private readonly Dictionary<SelectedSessionIdentity, DateTimeOffset> delivered = [];
    private const string Command = "timer:ItemUsageDailyRefreshService";
    private readonly CoalescingGameplaySource ticks = new(queue);
    // One refresh is one timer CMD on the executor; the entry enqueues it directly, the loop through its source.
    public Task RefreshAsync(CancellationToken token) => GameplayTimerWork.RunAsync(queue, Command, RefreshCoreAsync, token);
    private async ValueTask RefreshCoreAsync(CancellationToken token)
    {
        GameplayWriteAssertion.Writing(nameof(ItemUsageDailyRefreshService) + "." + nameof(RefreshCoreAsync));
        var current = directory.Snapshot();
        foreach (var stale in delivered.Keys.Except(current).ToArray()) delivered.Remove(stale);
        foreach (var session in current)
        {
            var period = days.PeriodStart(clock.GetUtcNow());
            if (delivered.TryGetValue(session, out var previous) && previous == period) continue;
            try
            {
                if (!sessions.IsInWorld(session)) continue;
                await delivery.SendAsync(session, token).ConfigureAwait(false);
                delivered[session] = period;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) { logger.LogWarning(ex, "Item usage projection failed character={CharacterId}", session.CharacterId); }
        }
    }
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        try { while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false)) await GameplayTimerWork.TickAsync(ticks, Command, RefreshCoreAsync, logger, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
