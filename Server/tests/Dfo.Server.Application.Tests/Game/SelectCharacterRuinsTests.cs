using System.Buffers.Binary;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Protocol.Game.DarkElfRuins;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Game;

public sealed partial class SelectCharacterLifecycleTests
{
    [Fact]
    public async Task Login_primes_all_ruins_regions_before_the_client_first_requests_selection()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var ruins = ProtocolAlgorithmFixture.Decorate(ProtocolAlgorithmFixture.Default<IDarkElfRuinsDomainService>(),
                (method, args) => method.Name == nameof(IDarkElfRuinsDomainService.SelectionAsync)
                    ? (true, ValueTask.FromResult(new DarkElfRuinsSelection(5, 2, 2, 2, [5007, 5017, 5027])))
                    : (false, null));
            var f = await Fixture.CreateAsync(ruins: ruins);
            await f.Handler.HandleAsync(f.Context, f.Command, default);

            // No ENTER_SELECT_DUNGEON request: the first local selection cache must already
            // have the current character's data, including the unopened weekday regions.
            var priming = Assert.Single(f.Sender.DirectMessages, x => x.Message is RuinsLicenseNotification);
            Assert.Equal(f.ConnectionId, priming.Target);
            var body = ((RuinsLicenseNotification)priming.Message).EncodeBody();
            Assert.Equal(3, BinaryPrimitives.ReadUInt16LittleEndian(body));
            for (var i = 0; i < 3; i++)
            {
                Assert.Equal(new[] { 5007, 5017, 5027 }[i], BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(2 + i * 12)));
                Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(6 + i * 12)));
                Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(10 + i * 12)));
            }
        });
    }
}
