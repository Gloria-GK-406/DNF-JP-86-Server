using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application;
using Dfo.Server.Infrastructure;
using Dfo.Server.Persistence;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game.DungeonClear;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game.DungeonClear;

/// <summary>
/// That the frames a run's ending depends on are listened for under the numbers the client sends.
/// </summary>
/// <remarks>
/// <b>This exists because of a gap a reviewer found by exploiting it.</b> On 2026-08-29 the
/// registration of the score-scroll decoder was changed from <c>0x0045</c> to <c>0x0099</c> -- the
/// defect being fixed put back in full -- and all seven test assemblies stayed green. Every test
/// around it built its decoder directly or checked the answer's own type, so nothing said which
/// number the server actually listens on. A protocol handler that is registered under the wrong
/// key is indistinguishable from one that was never written, and it is invisible until a live
/// session.
/// <para>
/// The numbers here are transcribed from what the client sent, not read from the constants under
/// test. Reading the constant would make this test agree with any value the constant took.
/// </para>
/// <para>
/// Only the decoder registrations. Resolving the handlers themselves would need the networking
/// module's own seams, which this sparse graph deliberately does not build; what covers a handler
/// that cannot be constructed is <c>ChannelHostStartupTests</c>, which boots the packaged host.
/// </para>
/// </remarks>
public sealed class EndOfRunWireKeyTests
{
    /// <summary>CMD, which is the family every client-sent frame carries.</summary>
    private const byte Cmd = 0x01;

    /// <summary>
    /// The types as observed, each with the session that showed it.
    /// </summary>
    /// <remarks>
    /// <c>0x0075</c> and <c>0x0045</c> from this server's own log of 2026-08-29
    /// (<c>type=0x0075 seq=126 body(39B)</c> and <c>type=0x0045 seq=131 body(0B)</c>), both
    /// arriving unhandled. <c>0x0048</c> from the reference capture of 2026-08-27, sent twice with
    /// the town state following the second.
    /// </remarks>
    public static TheoryData<ushort, Type> ObservedKeys => new()
    {
        { 0x0075, typeof(BossDieCheckDecoder) },
        { 0x0045, typeof(ScoreScrollStateDecoder) },
        { 0x0048, typeof(EplpCommandDecoder) },
    };

    [Theory]
    [MemberData(nameof(ObservedKeys))]
    public void The_ending_listens_on_the_numbers_the_client_sends(ushort type, Type decoder)
    {
        using var provider = BuildProductionGraph();

        var registration = Assert.Single(
            provider.GetServices<GameMessageDecoderRegistration>(),
            candidate => candidate.DecoderType == decoder);

        Assert.Equal(Cmd, registration.Selector);
        Assert.Equal(type, registration.Type);
    }

    /// <summary>
    /// No two decoders claim the same Game wire key.
    /// </summary>
    /// <remarks>
    /// The two families share one number space and disagree about most of it, so a collision is a
    /// real possibility rather than a theoretical one -- <c>0x0045</c> is <c>SCORE_SCROLL_STATE</c>
    /// in one direction and <c>SERVEREVENT</c> in the other. A second claim on one key would give
    /// the pipeline two decoders for one frame and no rule for choosing.
    /// </remarks>
    [Fact]
    public void No_two_decoders_claim_the_same_wire_key()
    {
        using var provider = BuildProductionGraph();
        var registrations = provider.GetServices<GameMessageDecoderRegistration>().ToArray();

        Assert.NotEmpty(registrations);
        Assert.Equal(
            registrations.Length,
            registrations.Select(one => (one.Selector, one.Type)).Distinct().Count());
    }

    /// <remarks>
    /// The same sparse graph <c>CommandRegistrationTests</c> builds, and for the same reasons: the
    /// archive and the database open lazily, and logging is registered because every real host has
    /// it before any module runs.
    /// </remarks>
    private static ServiceProvider BuildProductionGraph()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().Build();
        new ApplicationModule().ConfigureServices(services, configuration);
        new InfrastructureModule().ConfigureServices(services, configuration);
        new PersistenceModule().ConfigureServices(services, configuration);
        new ProtocolModule().ConfigureServices(services, configuration);
        return services.BuildServiceProvider();
    }
}
