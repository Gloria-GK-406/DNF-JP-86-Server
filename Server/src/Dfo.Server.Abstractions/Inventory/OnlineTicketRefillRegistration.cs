namespace Dfo.Server.Abstractions.Inventory;

/// <summary>A registered refill capability and its place in the post-publication selection phase.</summary>
public sealed record OnlineTicketRefillRegistration(string TaskId, int SelectionOrder,
    string SelectionWorkUnitName, IOnlineTicketRefillDomainService Service);
