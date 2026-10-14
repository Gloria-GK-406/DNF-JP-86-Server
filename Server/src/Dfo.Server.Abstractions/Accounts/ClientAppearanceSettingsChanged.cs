using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Accounts;

/// <summary>The selected character's appearance options changed; consumers read the current blob.</summary>
public sealed record ClientAppearanceSettingsChanged(AccountId AccountId, CharacterId CharacterId) : IDomainEvent;
