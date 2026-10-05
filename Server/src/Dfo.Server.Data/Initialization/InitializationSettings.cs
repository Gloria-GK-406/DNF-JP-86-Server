using System.Text.Json.Serialization;
namespace Dfo.Server.Data.Initialization;

public sealed record InitialContract([property: JsonRequired] int Type, [property: JsonRequired] int Days);
public sealed record InitialItem([property: JsonRequired] int ItemId, [property: JsonRequired] int Count);
public sealed record AccountInitialization([property: JsonRequired] int Cera, [property: JsonRequired] int TokenCera, [property: JsonRequired] int HappyTokenCera, [property: JsonRequired] int AccountCargoLevel, [property: JsonRequired] InitialContract[] Contracts);
public sealed record CharacterInitialization([property: JsonRequired] int Level, [property: JsonRequired] int PersonalCargoLevel, [property: JsonRequired] InitialItem[] Items);
public sealed record InitializationDocument<T>(long Revision, T Input, DateTimeOffset? UpdatedAt = null, string? UpdatedBy = null);
public sealed record InitializationStoredDocument(long Revision, string Input, DateTimeOffset UpdatedAt, string UpdatedBy);
public sealed record InitializationChoice(int Value, string Label);
public sealed record InitializationOptions(int MaximumLevel, InitializationChoice[] Contracts, InitializationChoice[] PersonalCargo, InitializationChoice[] AccountCargo);
public sealed record InitializationView(InitializationDocument<AccountInitialization> Account, InitializationDocument<CharacterInitialization> Character, InitializationOptions Options);
public sealed record SaveInitializationRequest<T>([property: JsonRequired] long ExpectedRevision, [property: JsonRequired] T Input);
