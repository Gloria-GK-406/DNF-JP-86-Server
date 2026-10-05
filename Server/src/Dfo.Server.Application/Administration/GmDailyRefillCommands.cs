using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Inventory;
namespace Dfo.Server.Application.Administration;

public sealed record QueryGmDailyRefillCommand(string Secret) : ICommand<DailyRefillList>;
internal sealed class QueryGmDailyRefillCommandHandler(IGmDailyRefillDomainService settings) : ICommandHandler<QueryGmDailyRefillCommand, DailyRefillList>
{ public ValueTask<DailyRefillList> HandleAsync(CommandExecutionContext c, QueryGmDailyRefillCommand q, CancellationToken ct) => settings.ListAsync(q.Secret, ct); }
public sealed record ChangeGmDailyRefillCommand(string Secret, DailyRefillChange Change) : ICommand<DailyRefillList>;
internal sealed class ChangeGmDailyRefillCommandHandler(IGmDailyRefillDomainService settings) : ICommandHandler<ChangeGmDailyRefillCommand, DailyRefillList>
{ public ValueTask<DailyRefillList> HandleAsync(CommandExecutionContext c, ChangeGmDailyRefillCommand q, CancellationToken ct) => settings.ChangeAsync(q.Secret, q.Change, ct); }
public sealed record SearchGmDailyRefillItemsCommand(string Secret, ItemCatalogFilter Filter) : ICommand<ItemCatalogPage>;
internal sealed class SearchGmDailyRefillItemsCommandHandler(IGmDailyRefillDomainService settings) : ICommandHandler<SearchGmDailyRefillItemsCommand, ItemCatalogPage>
{ public ValueTask<ItemCatalogPage> HandleAsync(CommandExecutionContext c, SearchGmDailyRefillItemsCommand q, CancellationToken ct) => settings.SearchAsync(q.Secret, q.Filter, ct); }
public sealed record ReadGmDailyRefillItemCommand(string Secret, int ItemId) : ICommand<ItemCatalogDetails>;
internal sealed class ReadGmDailyRefillItemCommandHandler(IGmDailyRefillDomainService settings) : ICommandHandler<ReadGmDailyRefillItemCommand, ItemCatalogDetails>
{ public ValueTask<ItemCatalogDetails> HandleAsync(CommandExecutionContext c, ReadGmDailyRefillItemCommand q, CancellationToken ct) => settings.ItemAsync(q.Secret, q.ItemId, ct); }
