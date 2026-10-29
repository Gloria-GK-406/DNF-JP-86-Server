using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
namespace Dfo.Server.Application.Tests.World;

internal static class TowerTestAlgorithms
{
    internal static BossTowerProjectionDriver Boss(ShopFixture f) => new(f.Services.GetRequiredService<IBossTowerDomainService>(), new Mutations(f),
        f.Services.GetRequiredService<ISelectedSessionDomainService>(), f.Services.GetRequiredService<ISelectedCharacterCurrentViewDomainService>(),
        f.Services.GetRequiredService<ICharacterStateProjectionDomainService>(), f.Services.GetRequiredService<ICharacterDomainService>(),
        f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>(), f.Services.GetRequiredService<IGameMessageSender>(),
        f.Services.GetRequiredService<TownAreaProjectionDelivery>(), f.Services.GetRequiredService<TimeProvider>(),
        f.Services.GetRequiredService<INetworkConnectionCloser>(), NullLogger.Instance,
        f.Services.GetRequiredService<IOnlineUserQueryDomainService>());
    internal static IllusionTowerProjectionDriver Illusion(ShopFixture f) => new(f.Services.GetRequiredService<IIllusionTowerDomainService>(), new Mutations(f),
        f.Services.GetRequiredService<ISelectedSessionDomainService>(), f.Services.GetRequiredService<ISelectedCharacterCurrentViewDomainService>(),
        f.Services.GetRequiredService<ICharacterStateProjectionDomainService>(), f.Services.GetRequiredService<ICharacterDomainService>(),
        f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>(), f.Services.GetRequiredService<IGameMessageSender>(),
        f.Services.GetRequiredService<TownAreaProjectionDelivery>(), f.Services.GetRequiredService<TimeProvider>(),
        f.Services.GetRequiredService<INetworkConnectionCloser>(), NullLogger.Instance,
        f.Services.GetRequiredService<IOnlineUserQueryDomainService>());
    internal static BossTowerPartyDriver BossParty(ShopFixture f) => new(f.Services.GetRequiredService<IBossTowerDomainService>(),
        f.Services.GetRequiredService<IPartyService>(), f.Services.GetRequiredService<ISelectedSessionDomainService>(), new Mutations(f),
        f.Services.GetRequiredService<PartyNotificationDelivery>(), Boss(f), f.Services.GetRequiredService<INetworkConnectionCloser>(),
        f.Services.GetRequiredService<ITownAreaDomainService>(), NullLogger.Instance, f.Services.GetRequiredService<IDungeonRunDomainService>());
    internal static IllusionTowerPartyDriver IllusionParty(ShopFixture f) => new(f.Services.GetRequiredService<IIllusionTowerDomainService>(),
        f.Services.GetRequiredService<IPartyService>(), f.Services.GetRequiredService<ISelectedSessionDomainService>(), new Mutations(f),
        f.Services.GetRequiredService<PartyNotificationDelivery>(), Illusion(f), f.Services.GetRequiredService<INetworkConnectionCloser>(),
        f.Services.GetRequiredService<ITownAreaDomainService>(), NullLogger.Instance, f.Services.GetRequiredService<IDungeonRunDomainService>());
    internal sealed class Mutations(ShopFixture fixture) : ITowerAlgorithmMutations
    {
        private async ValueTask<T> Write<T>(Func<ValueTask<T>> action)
        {
            await using var lease = fixture.Services.GetRequiredService<DomainTransactionScope>().Begin();
            var result = await action(); await lease.CommitAsync(default); return result;
        }
        public ValueTask<BossTowerActionResult> BossLeave(TowerActionContext context) => Write(() =>
        {
            var towers = fixture.Services.GetRequiredService<IBossTowerDomainService>();
            return TestEffectFootprints.Run(towers.Find(context.Session)!.Members.Select(member => member.Session), cap => towers.LeaveAsync(context, false, cap, default));
        });
        public ValueTask<TowerActionResult> IllusionLeave(TowerActionContext context) => Write(() =>
        {
            var towers = fixture.Services.GetRequiredService<IIllusionTowerDomainService>();
            return TestEffectFootprints.Run(towers.Find(context.Session)!.Members.Select(member => member.Session), cap => towers.LeaveAsync(context, false, cap, default));
        });
        public ValueTask<BossTowerActionResult> BossSynchronize(TowerActionContext context, SelectedSessionIdentity leader) => Write(() => fixture.Services.GetRequiredService<IBossTowerDomainService>().SynchronizeLeaderAsync(context, leader, default));
        public ValueTask<TowerActionResult> IllusionSynchronize(TowerActionContext context, SelectedSessionIdentity leader) => Write(() => fixture.Services.GetRequiredService<IIllusionTowerDomainService>().SynchronizeLeaderAsync(context, leader, default));
        public ValueTask<BossTowerActionResult> BossStart(TowerActionContext context) => Write(() => fixture.Services.GetRequiredService<IBossTowerDomainService>().StartAsync(context, default));
    }
}
