using Dfo.Server.Application.Commands;
using Dfo.Server.Data.World;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Protocol.Game.Fatigue;

internal sealed record FatigueNotification(CharacterFatigueBalance Balance) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameFatigue;
}
internal sealed class FatigueEncoder : GameNotiMessageEncoder<FatigueNotification>
{
    internal static byte[] Body(CharacterFatigueBalance balance)
    {
        var body = new byte[10];
        BinaryPrimitives.WriteUInt16LittleEndian(body, balance.Used);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2), balance.Total);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(4), checked((ushort)Math.Min(ushort.MaxValue, balance.ConsumedToday)));
        // The two A21-specific trailing fields remain zero until their semantics are verified.
        return body;
    }
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(FatigueNotification message) =>
        OnePayload(0x0024, Body(message.Balance));
}
public sealed class FatigueProjectionDelivery(ICharacterFatigueDomainService fatigue,
    ISelectedSessionDomainService sessions, IGameMessageSender sender)
{
    public async ValueTask SendAsync(SelectedSessionIdentity session, CancellationToken token)
    {
        var balance = await fatigue.BalanceOfAsync(session.CharacterId, token).ConfigureAwait(false);
        if (sessions.FindByCharacter(session.CharacterId) != session) return;
        await sender.SendNotiAsync(session.ConnectionId, new FatigueNotification(balance), token).ConfigureAwait(false);
    }
}
// One server clock; persistence rolls over lazily even when this worker is stopped or a player is offline.
public sealed class FatigueDailyRefreshService(ISelectedSessionSnapshotQuery directory,
    ISelectedSessionDomainService sessions, SelectedSessionTransitionGate transitions,
    ICharacterFatigueDomainService fatigue, IGameMessageSender sender, TimeProvider clock,
    ILogger<FatigueDailyRefreshService> logger) : BackgroundService
{
    private readonly Dictionary<SelectedSessionIdentity, CharacterFatigueBalance> delivered = [];
    public async Task RefreshAsync(CancellationToken token)
    {
        var current = directory.Snapshot();
        foreach (var old in delivered.Keys.Except(current).ToArray()) delivered.Remove(old);
        foreach (var session in current)
        {
            try
            {
                await using var lease = await transitions.AcquireAsync(session.AccountId, UserAccessMode.Exclusive, token).ConfigureAwait(false);
                var balance = await fatigue.BalanceOfAsync(session.CharacterId, token).ConfigureAwait(false);
                if (sessions.FindByCharacter(session.CharacterId) != session) continue;
                if (delivered.TryGetValue(session, out var old) && old == balance) continue;
                await sender.SendNotiAsync(session.ConnectionId, new FatigueNotification(balance), token).ConfigureAwait(false);
                delivered[session] = balance;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) { logger.LogWarning(ex, "Fatigue projection failed character={CharacterId}", session.CharacterId); }
        }
    }
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        try { while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false)) await RefreshAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
