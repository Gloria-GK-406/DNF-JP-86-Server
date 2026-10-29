using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Fatigue;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

public sealed record CaptureSelectedSessionRefreshCommand(SelectedSessionRefreshTopic Topic) : ICommand<SelectedSessionRefreshScan>;
public sealed record RefreshQuotaSessionCommand(SelectedSessionRefreshTopic Topic, DateTimeOffset Period, SelectedSessionIdentity Session) : ICommand<bool>;
public sealed record RefreshPremiumSessionCommand(DateTimeOffset Period, SelectedSessionIdentity Session) : ICommand<bool>;
public sealed record RefreshItemUsageSessionCommand(DateTimeOffset Period, SelectedSessionIdentity Session) : ICommand<bool>;
public sealed record RefreshFatigueSessionCommand(DateTimeOffset Period, SelectedSessionIdentity Session) : ICommand<bool>;

internal sealed class SelectedSessionRefreshCommandHandler(ISelectedSessionRefreshDomainService refresh,
    ISelectedSessionDomainService sessions, IPremiumServiceDomainService premiums, IPremiumServiceUsageDomainService usage,
    IItemUsageDomainService items, ICharacterFatigueDomainService fatigue,
    Dfo.Server.Abstractions.World.ILostLandDungeonDomainService lostLand,
    ICommandWorkUnits units, ICommandProjections projections, IGameCommandOutbox outbox,
    TimeProvider clock, ILogger<SelectedSessionRefreshCommandHandler> logger)
    : ICommandHandler<CaptureSelectedSessionRefreshCommand, SelectedSessionRefreshScan>,
      ICommandHandler<RefreshQuotaSessionCommand, bool>, ICommandHandler<RefreshPremiumSessionCommand, bool>,
      ICommandHandler<RefreshItemUsageSessionCommand, bool>, ICommandHandler<RefreshFatigueSessionCommand, bool>
{
    public ValueTask<SelectedSessionRefreshScan> HandleAsync(CommandExecutionContext context, CaptureSelectedSessionRefreshCommand command, CancellationToken token) =>
        ValueTask.FromResult(refresh.Capture(command.Topic));
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, RefreshQuotaSessionCommand command, CancellationToken token)
    {
        var topic = command.Topic; var session = command.Session;
        if (topic is SelectedSessionRefreshTopic.Premium or SelectedSessionRefreshTopic.ItemUsage or SelectedSessionRefreshTopic.Fatigue)
            throw new InvalidOperationException("A quota refresh requires a quota topic.");
        if (!refresh.IsCurrent(topic, command.Period, session)) return false;
        SelectedSessionRefreshState state = topic == SelectedSessionRefreshTopic.LostLand
            ? new QuotaCountRefreshState(command.Period,
                (await lostLand.ReadQuotaAsync(session.CharacterId, token).ConfigureAwait(false)).Remaining)
            : new PeriodRefreshState(command.Period);
        if (!refresh.IsCurrent(topic, command.Period, session) || !refresh.NeedsRefresh(topic, session, state)) return false;
        var family = topic switch
        {
            SelectedSessionRefreshTopic.Altar => UsageQuotaFamily.AltarDungeon,
            SelectedSessionRefreshTopic.Dimension => UsageQuotaFamily.DimensionDungeon,
            SelectedSessionRefreshTopic.GoldSilverHorn => UsageQuotaFamily.GoldSilverHorn,
            SelectedSessionRefreshTopic.ReviveCoin => UsageQuotaFamily.ReviveCoin,
            SelectedSessionRefreshTopic.Mirror => UsageQuotaFamily.MirrorDungeon,
            SelectedSessionRefreshTopic.Heras => UsageQuotaFamily.HerasDungeon,
            SelectedSessionRefreshTopic.LostLand => UsageQuotaFamily.LostLand,
            SelectedSessionRefreshTopic.Tournament => UsageQuotaFamily.TournamentDungeon,
            _ => throw new InvalidOperationException("Unknown quota refresh topic."),
        };
        var result = await projections.ProjectAsync(new NotificationAudience.TheseSessions([session]),
            new UsageQuotaViewChanged(session.CharacterId, family), token).ConfigureAwait(false);
        return result.Attempts.Any(attempt => attempt.CharacterId == session.CharacterId && attempt.Outcome == NotificationOutcome.Registered && attempt.RegistrationAccepted)
            && refresh.MarkRegistered(topic, command.Period, session, state);
    }
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, RefreshPremiumSessionCommand command, CancellationToken token)
    {
        const SelectedSessionRefreshTopic topic = SelectedSessionRefreshTopic.Premium;
        var session = command.Session;
        if (!refresh.IsCurrent(topic, command.Period, session)) return false;
        Exception? rejected = null;
        var snapshot = await units.ReadOnlyAsync(context, new("premium.snapshot", []), async (unit, ct) =>
        {
            try { return await PremiumSnapshotReader.ReadAsync(sessions, premiums, usage, clock, session, ct).ConfigureAwait(false); }
            catch (Exception error) when (error is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(error))
            { rejected = error; unit.RequireRollback(error); return new PremiumSnapshotResult(new([]), [], clock.GetUtcNow(), false); }
        }, token).ConfigureAwait(false);
        if (rejected is not null) { logger.LogWarning(rejected, "Premium daily projection failed character={CharacterId}", session.CharacterId); return false; }
        if (!snapshot.IsCurrent || !refresh.IsCurrent(topic, command.Period, session)) return false;
        var active = string.Join(";", snapshot.Entitlements.Premiums
            .Where(x => x.ExpiresAtUnixSeconds > snapshot.PreparedAt.ToUnixTimeSeconds()).OrderBy(x => x.PremiumType)
            .Select(x => x.PremiumType == (int)PremiumServiceType.BlackDiamond ? $"{x.PremiumType}:default" : $"{x.PremiumType}:{x.ExpiresAtUnixSeconds}"));
        var crystal = snapshot.Entitlements.Premiums.SingleOrDefault(x => x.PremiumType == (int)PremiumServiceType.CrystalContract);
        var growth = snapshot.Entitlements.Premiums.SingleOrDefault(x => x.PremiumType == (int)PremiumServiceType.GrowthContract);
        var state = new PremiumRefreshState(command.Period, active,
            crystal is not null && crystal.ExpiresAtUnixSeconds > snapshot.PreparedAt.ToUnixTimeSeconds(),
            growth is not null && growth.ExpiresAtUnixSeconds > snapshot.PreparedAt.ToUnixTimeSeconds());
        var previous = refresh.Previous(topic, session) as PremiumRefreshState;
        if (!refresh.NeedsRefresh(topic, session, state)) return false;
        var accepted = true;
        if (!state.CrystalActive && (previous?.CrystalActive == true || previous is null && crystal is not null))
            accepted &= await outbox.RegisterNotiWithOutcomeAsync(session.ConnectionId, new PremiumGrantedMessage(new((int)PremiumServiceType.CrystalContract, 0), snapshot.PreparedAt), token).ConfigureAwait(false);
        if (!state.GrowthActive && (previous?.GrowthActive == true || previous is null && growth is not null))
            accepted &= await outbox.RegisterNotiWithOutcomeAsync(session.ConnectionId, new PremiumGrantedMessage(new((int)PremiumServiceType.GrowthContract, 0), snapshot.PreparedAt), token).ConfigureAwait(false);
        accepted &= await outbox.RegisterCmdWithOutcomeAsync(session.ConnectionId, new PremiumServiceStateMessage(snapshot.Entitlements, snapshot.Usage, snapshot.PreparedAt), token).ConfigureAwait(false);
        return accepted && refresh.MarkRegistered(topic, command.Period, session, state);
    }
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, RefreshItemUsageSessionCommand command, CancellationToken token)
    {
        const SelectedSessionRefreshTopic topic = SelectedSessionRefreshTopic.ItemUsage;
        var session = command.Session; var state = new PeriodRefreshState(command.Period);
        if (!refresh.IsCurrent(topic, command.Period, session) || !refresh.NeedsRefresh(topic, session, state)) return false;
        Exception? rejected = null;
        var snapshot = await units.ReadOnlyAsync(context, new("item-usage.snapshot", []), async (unit, ct) =>
        {
            try { return await ItemUsageSnapshotReader.ReadAsync(items, sessions, clock, session, ct).ConfigureAwait(false); }
            catch (Exception error) when (error is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(error))
            { rejected = error; unit.RequireRollback(error); return new ItemUsageSnapshotResult([], false); }
        }, token).ConfigureAwait(false);
        if (rejected is not null) { logger.LogWarning(rejected, "Item usage projection failed character={CharacterId}", session.CharacterId); return false; }
        if (!snapshot.IsCurrent || !refresh.IsCurrent(topic, command.Period, session)) return false;
        var messages = ItemUsageCountMessage.FullSnapshot(snapshot.Items).Cast<IGameNotiMessage>()
            .Concat(ItemUsageCooldownProjection.Snapshot(snapshot.Items, snapshot.At));
        var accepted = true;
        foreach (var message in messages)
        {
            if (!refresh.IsCurrent(topic, command.Period, session)) return false;
            accepted &= await outbox.RegisterNotiWithOutcomeAsync(session.ConnectionId, message, token).ConfigureAwait(false);
        }
        return accepted && refresh.MarkRegistered(topic, command.Period, session, state);
    }
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, RefreshFatigueSessionCommand command, CancellationToken token)
    {
        const SelectedSessionRefreshTopic topic = SelectedSessionRefreshTopic.Fatigue;
        var session = command.Session;
        if (!refresh.IsCurrent(topic, command.Period, session)) return false;
        CharacterFatigueBalance balance;
        try { balance = await fatigue.BalanceOfAsync(session.CharacterId, token).ConfigureAwait(false); }
        catch (Exception error) when (error is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(error))
        { logger.LogWarning(error, "Fatigue projection failed character={CharacterId}", session.CharacterId); return false; }
        var state = new FatigueRefreshState(balance);
        if (!refresh.IsCurrent(topic, command.Period, session) || !refresh.NeedsRefresh(topic, session, state)) return false;
        var accepted = await outbox.RegisterNotiWithOutcomeAsync(session.ConnectionId, new FatigueNotification(balance), token).ConfigureAwait(false);
        return accepted && refresh.MarkRegistered(topic, command.Period, session, state);
    }
}
