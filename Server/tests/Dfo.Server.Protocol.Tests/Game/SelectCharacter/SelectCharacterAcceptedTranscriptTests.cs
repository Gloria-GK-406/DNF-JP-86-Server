using System.Buffers.Binary;
using System.Security.Cryptography;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game.SelectCharacter;

public sealed class SelectCharacterAcceptedTranscriptTests
{
    private static readonly string[] AcceptedKnightFrameSha256 =
    [
        "AC420C04DF298472E96D96204A5AC3F65A8B16883533D4F4962ABA57AB9F87E1",
        "5923B252F78DE8F1C9A0F5537392B9999867166D202D61CCEC1B06067A6EBD58",
        "15002D7D5C10A4593635BCA926791701C29C41E5F62364B6DDFBC23A46F21620",
        "1C67C38E1FA5D968E76AD2F7BD13DBE13791EE3A8DD67FF551E4C6E2396B8DF7",
        "B1B2D24BB6B58BB0C1A57A3ED29692F1274C7458A6D036BA4BDB04ABCCE8446D",
        "F8F6FACBE72D43BA979F2CCB375828AD2198498C628300E2B6A59D010830BA64",
        "D86BC0EF19406D5B6E0A2A3925B6A551778813BF82D04509EF3B82E0E4E385AE",
        "F5AA6DBFAB6B07A98632E756BE8858F40063B5BDD2E10C3C227BE7C2040FF818",
        "332D2B2897F49C03317ABC759697FC85A02C64325103F467CFB5586DD134E57B",
        "1499344A804A55F6F2B29343C63E6DD5A24F14B2C3B2D73A434D374AC89CC5A0",
        "CCE7EE6E8DA05E06E572F390A56041629DA0ADF665F3301506E0D82FDA08F724",
        "3174E2CEBCB1C0CA1FD79D73F09D3F5C401253EEFA595DA6782AF25C9C7E8864",
        "266D44BEC79069CB6E2C1C47BA816600000AC5505EBA503EF35FD68144E630C7",
        "D6ED5C85B3F61FC62C57F039B0B3D6D671F1553C92DA904FBED57526D3715DBE",
        "1E056EBA3E1E1D3C46F3D761CF2103A4E8CE5184B093B34281C27FF838AC35B8",
        "A08345CC1AF51817F0676CD7B738DD4C91D3A88A8016A7E872C79B85D9F9473A",
        "D3FDCCFDEC31BA9FF8D9DB03777822EE21994C0FB8162A2106A257E3C78003C6",
        "3BDA3308A823C62FAF2758C0A2B96CCB1D61E41087CECE202724FEE071CC2F6E",
        "A60533D86AE76DB4DC92B792C93144A87B9F7C41135D2FA9D50922E90434F32E",
        "C0702AF1BB0F608F369C16F3C1A04BEB8CAE1EF5833B2136C502BF97F975D1F8",
        "B3433E48A2C46585B306871332720618B73D1AC7C959D4B651276F99BFB01C9B",
        "B9187663D16574D9D0D634D0281C9027D85A79C69918A236332220403837A33A",
        "FEC06C17813CC431712E2D5C0FAE4A7FCC8B83E78DE731746FFDD95E05A84E08",
        "605BD7E94284CE36353DACA0B5CE8ABFE2589C9486F5F9CA7782623371E97C6D",
        "A32C2225DD2B12B93644BC3056BA2FC81A80BF29F46F0B6333E65DAA9601E661",
        "2CCB71D03BF0DBD51353A4E6F37EDCD925588C3B806D5C50F5D41442B37716E4",
        "1C18B6C53C707A1726AC3A181575C97A47294421C43BF50161302EA8A07AE9E4",
        "5FC962E013A18EBB8C30190BE5C209DF20C3B66414197CCC6D1BEB6DE82929DB",
        "E8A1FCB7C3C070BF24A3B21B486DDC625766209C007935C2924FB0D0B32FC606",
        "AC5565CF257F0BF8CCC34C3AAFDD737392440A536A0180A9C0F3B4A740E2DAFC",
        "8D16A2778C9E88F3679F7EB0A22A17198CE5E0742CE322D8E8DBBD1A83B18918",
        "5A64D5148AA2533F58E89315C7CE44BE3B4A001539571EDD90E237B73AA2D065",
        "241A3E548B117531A1EC9C3BB7817B9C170541F30E947236A2183E2B2DF48C58",
        "842F0380897E18C0ACAB21EFEEF11EF0819EE8B42C61CB252694FAC374477A1A",
        "901C158023743BA5FACA68BB42894729E9446CA5D340E80A70F19A308632A63F",
        "D94374797A1E19E12254E29B82E61FF6DCCED28D161513DD1BD30675407E0288",
        "FCF7224DE64D04B579AC5CFB85789C8BE4CB281C743D6FBA53464A81CE0E61C9",
        "719047521FB6DFE1D97F9D7070DF29068D720EA4FD14A27BBE23354C2DA653FE",
        "86A11CBC63E82DC2EC8F223C57E276F28C96E88FE1AEEB63BFC4D08B058C32E2",
        "732ACA9A5EB86363C0AAAB10F62C585317C692CC5A9DB3F54DD86C84FEBE411F",
        "69B7C98F57A762698CE0ACCF46D4B57196ED865A98AC22D859C03C3EEA4397D6",
        "50CCCF47E618F7AF022466E6D85ECE08E5D2BDBFF20BFF74C7FF8C7D899168AC",
        "CD18E7A7D18230B1CF45779E8DC502E544350F626AD6FF458C90840BCD631AA7",
        "B1ABCB72E7FD6D171C4B1330D59E8A731F9E1D122F9D378F2EA126BB76560561",
        "7F2629F5B68855367A92CACB33A8479E7E87B2C443CF665D17A3C40782AEBFE1",
        "8603826808E971B3602A1EDD1F5E07D29B7BB174746B5BDC220BEAD9A0B509D7",
        "95E129E2524D878686C794D015E9605AC8C0862ED7513210069E054D3F0FD3D0",
        "A227C34D0C869176C5A79AE0AF33CF32D53E93A7DC7B6C39977A3B7B7BECD342",
        "CA6111294B4CDF0F4ED89B05BF707FD96B2A3D1741A9E5127192070CFA65B3E5",
        "F8E09759C7AD90E56A4EC27137298F326B4AD859F492B0E8B430A6953F136D95",
        "729EB01F0119D19B42E031D2579AEABA2519875957B1FF99168F4F2E0D2E608A",
        "F0652C8229C305BDEB307586AF3CE437C908C7C07CE0D06EA0B255AEE97F288A",
        "7FBC76D7D1284294CD227CC31D6E210F037DCF59358C7FA2FDC3DA4BF23C87AF",
        "F5AA6DBFAB6B07A98632E756BE8858F40063B5BDD2E10C3C227BE7C2040FF818",
        "1BDFA4559D736FD501F8D0F1E0F55CB5A002C65521A0F8C9CFF6C66FAE81DFA2",
        "EEAD9EED6EDB5F5F459383BC790338E0A68580FF9A5912B44B17884EB9BAFC0A",
        "F9A45174905BAB69C5A069044A6563D0C29F186088EE933AA2197D3ED7BD00E6",
        "F3BDDC10EE2DD1DEE773A611FBF41C7D22E4F1C6274F35A93260438218A4990B",
    ];

    private static readonly (byte Command, ushort Type)[] AcceptedStaticIdentities =
    [
        (1, 0x0004), (0, 0x000D), (0, 0x000D), (0, 0x000D),
        (0, 0x000D), (0, 0x000D), (0, 0x0069), (0, 0x0002),
        (0, 0x0002), (0, 0x0245), (0, 0x0013), (0, 0x0015),
        (0, 0x0164), (0, 0x0286), (0, 0x00AD), (0, 0x01C7),
        (0, 0x006C), (0, 0x0005), (0, 0x0187), (0, 0x01B9),
        (0, 0x0166), (0, 0x0166), (0, 0x0166), (0, 0x0166),
        (0, 0x0166), (0, 0x0167), (0, 0x015F), (0, 0x00AC),
        (0, 0x00AE), (0, 0x02DA), (0, 0x017B), (0, 0x0381),
        (0, 0x0381), (0, 0x021F), (0, 0x0357), (0, 0x00FB),
        (0, 0x00CD), (0, 0x019F), (0, 0x00B1), (0, 0x0300),
        (1, 0x0312), (0, 0x03D8), (0, 0x019D), (0, 0x025B),
        (0, 0x0331), (0, 0x01EB), (0, 0x0061), (0, 0x0158),
        (0, 0x02D5), (0, 0x02D5), (0, 0x02D5), (0, 0x01A8),
        (0, 0x007C), (0, 0x0002), (0, 0x0035), (0, 0x0111),
        (0, 0x0016), (0, 0x0077),
    ];

    [Fact]
    public void AcceptedEntryTranscript_AcknowledgementIsFirstAndPreservesReviewedIdentityOrder()
    {
        var source = SelectCharacterPayloadTestData.CreateComplete();
        var allRowsApplicable = source with
        {
            Character = source.Character with
            {
                Identity = source.Character.Identity with { Job = 12 },
            },
        };

        var identities = Identities(CaptureCurrentTranscript(allRowsApplicable));

        Assert.Equal(58, identities.Length);
        Assert.Equal(((byte)1, (ushort)0x0004), identities[0]);
        Assert.Equal(AcceptedStaticIdentities, identities);
    }

    [Fact]
    public void AcceptedEntryTranscript_ConditionalMessagesSkipOnlyThemselvesAndSupportStateIsOnce()
    {
        var frames = CaptureCurrentTranscript(SelectCharacterPayloadTestData.CreateComplete());
        var identities = Identities(frames);
        var expected = AcceptedStaticIdentities
            .Where((_, index) => index + 1 != 10)
            .ToArray();

        Assert.Equal(expected, identities);
        Assert.Equal(1, identities.Count(value => value == ((byte)0, (ushort)0x019F)));
        var subtype1Index = Array.IndexOf(identities, ((byte)0, (ushort)0x0002), 8);
        Assert.Equal(((byte)0, (ushort)0x0013), identities[subtype1Index + 1]);
        Assert.Equal(((byte)0, (ushort)0x0077), identities[^1]);
    }

    [Fact]
    public void AcceptedEntryTranscript_DarkKnightStateOccursOnceImmediatelyAfterSkills()
    {
        var source = SelectCharacterPayloadTestData.CreateComplete();
        var result = source with
        {
            Character = source.Character with
            {
                Identity = source.Character.Identity with { Job = 9 },
            },
        };

        var identities = Identities(CaptureCurrentTranscript(result));
        var skillIndex = Array.IndexOf(identities, ((byte)0, (ushort)0x0013));

        Assert.Equal(((byte)0, (ushort)0x01C0), identities[skillIndex + 1]);
        Assert.Equal(1, identities.Count(value => value == ((byte)0, (ushort)0x01C0)));
        Assert.Equal(((byte)0, (ushort)0x0015), identities[skillIndex + 2]);
    }

    [Fact]
    public void AcceptedEntryTranscript_KnightAndResolvedPetWelcomeEmitAllStaticMessages()
    {
        var source = SelectCharacterPayloadTestData.CreateComplete();
        var result = source with
        {
            Character = source.Character with
            {
                Identity = source.Character.Identity with { Job = 12 },
            },
        };

        var identities = Identities(CaptureCurrentTranscript(result));

        Assert.Equal(AcceptedStaticIdentities, identities);
        Assert.DoesNotContain(((byte)0, (ushort)0x01C0), identities);
    }

    [Fact]
    public void AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline()
    {
        var source = SelectCharacterPayloadTestData.CreateComplete();
        var result = source with
        {
            Character = source.Character with
            {
                Identity = source.Character.Identity with { Job = 12 },
            },
        };

        var hashes = CaptureCurrentTranscript(result).Select(Sha256).ToArray();

        Assert.Equal(AcceptedKnightFrameSha256, hashes);
    }

    [Fact]
    public void AcceptedEntryTranscript_DarkKnightFrameBytesMatchAcceptedBaseline()
    {
        var source = SelectCharacterPayloadTestData.CreateComplete();
        var result = source with
        {
            Character = source.Character with
            {
                Identity = source.Character.Identity with { Job = 9 },
            },
        };
        var frame = CaptureCurrentTranscript(result).Single(
            value => BinaryPrimitives.ReadUInt16LittleEndian(value.AsSpan(1, 2)) == 0x01C0);

        Assert.Equal(
            "53E1FD5ECF783E20C92F1B2B0549010A934265656D4A6A3AB8D24DFC957D0C65",
            Sha256(frame));
    }

    [Fact]
    public void InventoryDelta_UsesTheSnapshotItemEncodingAndMatchesAcceptedFrameBytes()
    {
        var source = SelectCharacterPayloadTestData.CreateComplete();
        var main = source.CharacterInventory.Containers.Single(
            value => value.ContainerType == InventoryContainerType.Main);
        var snapshot = Assert.IsType<InventorySnapshotMessage>(
            SelectedCharacterStateMessageFactory.Create(source)
                .First(value => value is InventorySnapshotMessage));
        var delta = new InventoryDeltaMessage(
            InventoryContainerType.Main,
            [main.Items[0]],
            source.World.PreparedAt);

        var deltaFrame = Assert.Single(EncodeThroughRegisteredEncoders([delta]));
        var snapshotBody = snapshot.EncodeBody();
        var deltaBody = delta.EncodeBody();

        Assert.Equal(
            "572A9A560AFDB030BC7617F3D39B000456172700B302F8D6740068DBA3533951",
            Sha256(deltaFrame));
        Assert.Equal(
            snapshotBody.AsSpan(5 + (3 * 84), 84).ToArray(),
            deltaBody.AsSpan(3, 84).ToArray());
    }

    [Fact]
    public void AcceptedEntryTranscript_InapplicablePetWelcomeOmitsOnlyTheFinalMessage()
    {
        var source = SelectCharacterPayloadTestData.CreateComplete();
        var nullWelcome = source with
        {
            World = source.World with
            {
                Content = source.World.Content with { PetWelcome = null },
            },
        };
        var emptyWelcome = source with
        {
            World = source.World with
            {
                Content = source.World.Content with
                {
                    PetWelcome = source.World.Content.PetWelcome! with { Lines = [] },
                },
            },
        };
        var expected = AcceptedStaticIdentities
            .Where((_, index) => index + 1 is not (10 or 58))
            .ToArray();

        Assert.Equal(expected, Identities(CaptureCurrentTranscript(nullWelcome)));
        Assert.Equal(expected, Identities(CaptureCurrentTranscript(emptyWelcome)));
    }

    [Fact]
    public void ConcreteStateSynchronizationMessages_EachHaveExactlyOneGameEncoderRegistration()
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(
            services,
            new ConfigurationBuilder().Build());
        var registrations = services
            .Where(value => value.ServiceType == typeof(ServerMessageEncoderRegistration))
            .Select(value => Assert.IsType<ServerMessageEncoderRegistration>(value.ImplementationInstance))
            .Where(value => IsStateSynchronizationMessage(value.MessageType))
            .ToArray();
        var messageTypes = typeof(GameStateSynchronizationMessage).Assembly.GetTypes()
            .Where(value => !value.IsAbstract
                && IsStateSynchronizationMessage(value))
            .OrderBy(value => value.FullName)
            .ToArray();

        Assert.Equal(messageTypes, registrations.Select(value => value.MessageType)
            .OrderBy(value => value.FullName).ToArray());
        Assert.All(registrations, value => Assert.Equal(NetworkEndpointKind.Game, value.Endpoint));
        Assert.All(messageTypes, messageType =>
            Assert.Single(registrations, value => value.MessageType == messageType));
    }

    private static bool IsStateSynchronizationMessage(Type type) =>
        typeof(GameStateSynchronizationMessage).IsAssignableFrom(type)
        || typeof(SupplementalSynchronizationMessage).IsAssignableFrom(type);

    private static IReadOnlyList<byte[]> CaptureCurrentTranscript(
        Dfo.Server.Data.Game.SelectCharacterResult result) =>
        EncodeThroughRegisteredEncoders(SelectedCharacterStateMessageFactory.Create(result));

    private static IReadOnlyList<byte[]> EncodeThroughRegisteredEncoders(
        IReadOnlyList<GameStateSynchronizationMessage> messages)
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var registrations = services
            .Where(value => value.ServiceType == typeof(ServerMessageEncoderRegistration))
            .Select(value => Assert.IsType<ServerMessageEncoderRegistration>(value.ImplementationInstance))
            .ToArray();
        var frames = new List<byte[]>();
        foreach (var message in messages)
        {
            var registration = Assert.Single(
                registrations, value => value.MessageType == message.GetType());
            var encoder = Assert.IsAssignableFrom<IServerMessageEncoder>(
                provider.GetRequiredService(registration.EncoderType));
            frames.AddRange(encoder.Encode(message));
        }

        return frames;
    }

    private static string Sha256(byte[] frame) =>
        Convert.ToHexString(SHA256.HashData(frame));

    private static (byte Command, ushort Type)[] Identities(IReadOnlyList<byte[]> frames) =>
        frames.Select(frame =>
        {
            Assert.Equal(frame.Length, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(3, 4)));
            return (frame[0], BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1, 2)));
        }).ToArray();
}
