using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.HttpOperations;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Initialization;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Administration;

public sealed record QueryGmInitializationCommand(string Secret) : HttpOperationCommand;
public sealed record SaveGmAccountInitializationCommand(string Secret, SaveInitializationRequest<AccountInitialization> Request) : HttpOperationCommand;
public sealed record SaveGmCharacterInitializationCommand(string Secret, SaveInitializationRequest<CharacterInitialization> Request) : HttpOperationCommand;
public sealed record SearchGmInitializationItemsCommand(string Secret, ItemCatalogFilter Filter) : HttpOperationCommand;
public sealed record ReadGmInitializationItemCommand(string Secret, int Id) : HttpOperationCommand;

public sealed record QueryGmDailyRefillCommand(string Secret) : HttpOperationCommand;
public sealed record ChangeGmDailyRefillCommand(string Secret, DailyRefillChange Change) : HttpOperationCommand;
public sealed record SearchGmDailyRefillItemsCommand(string Secret, ItemCatalogFilter Filter) : HttpOperationCommand;
public sealed record ReadGmDailyRefillItemCommand(string Secret, int Id) : HttpOperationCommand;

public sealed record QueryGmEventsCommand(string Secret) : HttpOperationCommand;
public sealed record QueryGmWeeklyAttendanceSettingsCommand(string Secret) : HttpOperationCommand;
public sealed record ChangeGmEventsCommand(string Secret, EventChange Change) : HttpOperationCommand;

public sealed record QueryGmJoustSettingsCommand(string Secret) : HttpOperationCommand;
public sealed record QueryGmJoustCurrentRoundCommand(string Secret) : HttpOperationCommand;
public sealed record ChangeGmJoustSettingsCommand(string Secret, JoustSettingsChange Change) : HttpOperationCommand;
public sealed record ChangeGmJoustChampionCommand(string Secret, JoustChampionChange Change) : HttpOperationCommand;

internal sealed class GmInitializationCommandHandler(IGmInitializationDomainService initialization, ICommandWorkUnits units)
    : ICommandHandler<QueryGmInitializationCommand, HttpRouteReply>, ICommandHandler<SaveGmAccountInitializationCommand, HttpRouteReply>,
      ICommandHandler<SaveGmCharacterInitializationCommand, HttpRouteReply>, ICommandHandler<SearchGmInitializationItemsCommand, HttpRouteReply>,
      ICommandHandler<ReadGmInitializationItemCommand, HttpRouteReply>
{
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, QueryGmInitializationCommand command, CancellationToken token) =>
        GmSettingsWork.Write(units, context, "query-gm-initialization", t => initialization.ReadAsync(command.Secret, t), token);
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, SaveGmAccountInitializationCommand command, CancellationToken token) =>
        GmSettingsWork.Write(units, context, "save-gm-account-initialization", t => initialization.SaveAsync(command.Secret, command.Request, null, t), token);
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, SaveGmCharacterInitializationCommand command, CancellationToken token) =>
        GmSettingsWork.Write(units, context, "save-gm-character-initialization", t => initialization.SaveAsync(command.Secret, null, command.Request, t), token);
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, SearchGmInitializationItemsCommand command, CancellationToken token) =>
        GmSettingsWork.Write(units, context, "search-gm-initialization-items", t => initialization.SearchAsync(command.Secret, command.Filter, t), token);
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, ReadGmInitializationItemCommand command, CancellationToken token) =>
        GmSettingsWork.Write(units, context, "read-gm-initialization-item", t => initialization.ItemAsync(command.Secret, command.Id, t), token);
}

internal sealed class GmDailyRefillCommandHandler(IGmDailyRefillDomainService daily, ICommandWorkUnits units)
    : ICommandHandler<QueryGmDailyRefillCommand, HttpRouteReply>, ICommandHandler<ChangeGmDailyRefillCommand, HttpRouteReply>,
      ICommandHandler<SearchGmDailyRefillItemsCommand, HttpRouteReply>, ICommandHandler<ReadGmDailyRefillItemCommand, HttpRouteReply>
{
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, QueryGmDailyRefillCommand command, CancellationToken token) =>
        GmSettingsWork.Write(units, context, "query-gm-daily-refill", t => daily.ListAsync(command.Secret, t), token);
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, ChangeGmDailyRefillCommand command, CancellationToken token) =>
        GmSettingsWork.Write(units, context, "change-gm-daily-refill", t => daily.ChangeAsync(command.Secret, command.Change, t), token);
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, SearchGmDailyRefillItemsCommand command, CancellationToken token) =>
        GmSettingsWork.Write(units, context, "search-gm-daily-refill-items", t => daily.SearchAsync(command.Secret, command.Filter, t), token);
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, ReadGmDailyRefillItemCommand command, CancellationToken token) =>
        GmSettingsWork.Write(units, context, "read-gm-daily-refill-item", t => daily.ItemAsync(command.Secret, command.Id, t), token);
}

/// <summary>The four reads are registered with clean read cancellation; the changes are ordinary writes.</summary>
internal sealed class GmEventsCommandHandler(IGmEventsDomainService events, IGmJoustDomainService joust, ICommandWorkUnits units)
    : ICommandHandler<QueryGmEventsCommand, HttpRouteReply>, ICommandHandler<QueryGmWeeklyAttendanceSettingsCommand, HttpRouteReply>,
      ICommandHandler<ChangeGmEventsCommand, HttpRouteReply>, ICommandHandler<QueryGmJoustSettingsCommand, HttpRouteReply>,
      ICommandHandler<QueryGmJoustCurrentRoundCommand, HttpRouteReply>, ICommandHandler<ChangeGmJoustSettingsCommand, HttpRouteReply>,
      ICommandHandler<ChangeGmJoustChampionCommand, HttpRouteReply>
{
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, QueryGmEventsCommand command, CancellationToken token) =>
        GmSettingsWork.Read(units, context, "query-gm-events", t => events.ListAsync(command.Secret, t), token);
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, QueryGmWeeklyAttendanceSettingsCommand command, CancellationToken token) =>
        GmSettingsWork.Read(units, context, "query-gm-weekly-attendance-settings", t => events.WeeklyAttendanceSettingsAsync(command.Secret, t), token);
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, ChangeGmEventsCommand command, CancellationToken token) =>
        GmSettingsWork.Write(units, context, "change-gm-events", t => events.ChangeAsync(command.Secret, command.Change, t), token);
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, QueryGmJoustSettingsCommand command, CancellationToken token) =>
        GmSettingsWork.Read(units, context, "query-gm-joust-settings", t => joust.SettingsAsync(command.Secret, t), token);
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, QueryGmJoustCurrentRoundCommand command, CancellationToken token) =>
        GmSettingsWork.Read(units, context, "query-gm-joust-current-round", t => joust.CurrentRoundAsync(command.Secret, t), token);
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, ChangeGmJoustSettingsCommand command, CancellationToken token) =>
        GmSettingsWork.Write(units, context, "change-gm-joust-settings", t => joust.SaveAsync(command.Secret, command.Change, t), token);
    public ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, ChangeGmJoustChampionCommand command, CancellationToken token) =>
        GmSettingsWork.Write(units, context, "change-gm-joust-champion", t => joust.SetChampionAsync(command.Secret, command.Change, t), token);
}

internal static class GmSettingsWork
{
    internal static ValueTask<HttpRouteReply> Write<T>(ICommandWorkUnits units, CommandExecutionContext context, string name,
        Func<CancellationToken, ValueTask<T>> action, CancellationToken token) where T : notnull
    {
        HttpOperationWork.DemandOrigin(context, GameplayCallSource.Gm);
        return HttpOperationWork.ReplyAsync(context, async () => await HttpOperationWork.WriteAsync(units, context, name, action, token).ConfigureAwait(false));
    }
    internal static ValueTask<HttpRouteReply> Read<T>(ICommandWorkUnits units, CommandExecutionContext context, string name,
        Func<CancellationToken, ValueTask<T>> action, CancellationToken token) where T : notnull
    {
        HttpOperationWork.DemandOrigin(context, GameplayCallSource.Gm);
        return HttpOperationWork.ReplyAsync(context, async () => await HttpOperationWork.ReadAsync(units, context, name, action, token).ConfigureAwait(false));
    }
}
