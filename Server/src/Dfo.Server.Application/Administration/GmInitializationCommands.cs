using Dfo.Server.Data.Inventory;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Initialization;
namespace Dfo.Server.Application.Administration;
public sealed record QueryGmInitializationCommand(string Secret) : ICommand<InitializationView>;
internal sealed class QueryGmInitializationCommandHandler(IGmInitializationDomainService settings) : ICommandHandler<QueryGmInitializationCommand, InitializationView>
{ public ValueTask<InitializationView> HandleAsync(CommandExecutionContext c, QueryGmInitializationCommand q, CancellationToken ct) => settings.ReadAsync(q.Secret, ct); }
public sealed record SaveGmAccountInitializationCommand(string Secret, SaveInitializationRequest<AccountInitialization> Request) : ICommand<object>;
internal sealed class SaveGmAccountInitializationCommandHandler(IGmInitializationDomainService settings) : ICommandHandler<SaveGmAccountInitializationCommand, object>
{ public ValueTask<object> HandleAsync(CommandExecutionContext c, SaveGmAccountInitializationCommand q, CancellationToken ct) => settings.SaveAsync(q.Secret, q.Request, null, ct); }
public sealed record SaveGmCharacterInitializationCommand(string Secret, SaveInitializationRequest<CharacterInitialization> Request) : ICommand<object>;
internal sealed class SaveGmCharacterInitializationCommandHandler(IGmInitializationDomainService settings) : ICommandHandler<SaveGmCharacterInitializationCommand, object>
{ public ValueTask<object> HandleAsync(CommandExecutionContext c, SaveGmCharacterInitializationCommand q, CancellationToken ct) => settings.SaveAsync(q.Secret, null, q.Request, ct); }
public sealed record SearchGmInitializationItemsCommand(string Secret, ItemCatalogFilter Filter) : ICommand<ItemCatalogPage>;
internal sealed class SearchGmInitializationItemsCommandHandler(IGmInitializationDomainService settings) : ICommandHandler<SearchGmInitializationItemsCommand, ItemCatalogPage>
{ public ValueTask<ItemCatalogPage> HandleAsync(CommandExecutionContext c, SearchGmInitializationItemsCommand q, CancellationToken ct) => settings.SearchAsync(q.Secret, q.Filter, ct); }
public sealed record ReadGmInitializationItemCommand(string Secret, int ItemId) : ICommand<ItemCatalogDetails>;
internal sealed class ReadGmInitializationItemCommandHandler(IGmInitializationDomainService settings) : ICommandHandler<ReadGmInitializationItemCommand, ItemCatalogDetails>
{ public ValueTask<ItemCatalogDetails> HandleAsync(CommandExecutionContext c, ReadGmInitializationItemCommand q, CancellationToken ct) => settings.ItemAsync(q.Secret, q.ItemId, ct); }
