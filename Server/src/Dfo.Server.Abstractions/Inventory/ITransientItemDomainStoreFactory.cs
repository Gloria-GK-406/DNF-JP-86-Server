namespace Dfo.Server.Abstractions.Inventory;

/// <summary>Creates isolated transaction-aware storage whose contents live only as long as its owner.</summary>
public interface ITransientItemDomainStoreFactory
{
    IItemDomainStore Create();
}
