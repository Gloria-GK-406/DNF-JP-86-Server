using System.Buffers.Binary;
using System.Security.Cryptography;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game.SelectCharacter;

public sealed class SelectCharacterAcceptedTranscriptTests
{
    // Rebased once more. Of the previous 62, **60 hash identically**; the two that changed are
    // USERINFO subtype 0 (#1) and subtype 1 (#13), and nothing else. Both changed for the same
    // reason: their tails were the older client's field-by-field layouts, and A21 puts a fixed
    // block there instead.
    //
    // These are not re-recordings of whatever the encoder now emits. The A21 server that produced
    // the reference capture writes both blocks as constants with named patch points, and the two
    // constants reproduce the captured bytes exactly -- subtype 0's last 64 bytes and subtype 1's
    // last 187 both compare byte for byte against the reference session, with the only difference
    // being subtype 0's channel slot at +59 holding the value the reference carries. The lengths
    // reconcile too: subtype 0 is 157 bytes plus name and appearance on both sides, and subtype 1
    // is 330 once the reference's three equipment entries and three extra skills are taken out.
    // See patterns/data/traces/a21-entry-batch-diff-20260825.md.
    private static readonly string[] AcceptedKnightFrameSha256 =
    [
        "55C07ECCFA9443FC8EFF74A0857ED4BF0F7EBB2ACEACC7836D7C9B8104B21853",
        "486451B3F7C6A4F913C62CD875328E561955AE243D1FA4DE835561797CDD703D",
        "B3433E48A2C46585B306871332720618B73D1AC7C959D4B651276F99BFB01C9B",
        "B9187663D16574D9D0D634D0281C9027D85A79C69918A236332220403837A33A",
        "FEC06C17813CC431712E2D5C0FAE4A7FCC8B83E78DE731746FFDD95E05A84E08",
        "605BD7E94284CE36353DACA0B5CE8ABFE2589C9486F5F9CA7782623371E97C6D",
        "A32C2225DD2B12B93644BC3056BA2FC81A80BF29F46F0B6333E65DAA9601E661",
        "2CCB71D03BF0DBD51353A4E6F37EDCD925588C3B806D5C50F5D41442B37716E4",
        "3174E2CEBCB1C0CA1FD79D73F09D3F5C401253EEFA595DA6782AF25C9C7E8864",
        "266D44BEC79069CB6E2C1C47BA816600000AC5505EBA503EF35FD68144E630C7",
        "1499344A804A55F6F2B29343C63E6DD5A24F14B2C3B2D73A434D374AC89CC5A0",
        "CCE7EE6E8DA05E06E572F390A56041629DA0ADF665F3301506E0D82FDA08F724",
        "D86BC0EF19406D5B6E0A2A3925B6A551778813BF82D04509EF3B82E0E4E385AE",
        "3160D61D534CABED25EBCD39701C3D14AF7D550F68076181591C11186198DC1E",
        "75CC35AB4AC6C1E55DB7363C39B33AF2B579B7293360E262B16B5EBE48E030C1",
        "7093589D2FDAD5B2D0AFAADED850B0E091132E4D7EFBD60DF378E50C3393C72F",
        "3BDA3308A823C62FAF2758C0A2B96CCB1D61E41087CECE202724FEE071CC2F6E",
        "E0D5185CF4EFF8181ACE2EA25273DB6E22DEFB87012689EBFE410867929C9278",
        // Frame 18 is the avatar container's ITEM_LIST -- the one frame in this list belonging to a
        // container the client reads a length-prefixed appearance block in. It has now been three
        // lengths: 163, then 121, now 151. None of them is a correction of a typo; each is a
        // different answer to "who carries the block, and how long is it".
        //
        // 163 keyed the block on the item's kind and gave it the older client's thirty-byte jewel
        // socket. That put a block on an avatar sitting in the main bag too, which pushed every
        // following entry out of that list's fixed-101 stride (STATUS_STACK_BUFFER_OVERRUN).
        // 121 removed the block outright, which fixed the main bag and left this container's
        // entries short of what the client reads -- it then took the four bytes after the record,
        // which are the receive buffer's padding, as a length and copied 0x30303030 bytes onto its
        // stack (STATUS_ACCESS_VIOLATION). Both were seen against a live client.
        // 151 keys it on the container, which is how the client keys it, and sends the A21 socket
        // length: eighteen of the thirty stored bytes, being the first three of five six-byte
        // slots. 121 + 4 + 18 + 4 + 4 = 151.
        //
        // It is the only frame that moved: this list is 62 entries before and after, the other 61
        // hash identically, and the length went 121 -> 151. Established by comparing element-wise,
        // not inferred from the change being small.
        "D4C7DEED02A813ED7592A9E9AFF8A624D6B971BC56B2B7A708EAC8FB57D6588A",
        "1C67C38E1FA5D968E76AD2F7BD13DBE13791EE3A8DD67FF551E4C6E2396B8DF7",
        "B1B2D24BB6B58BB0C1A57A3ED29692F1274C7458A6D036BA4BDB04ABCCE8446D",
        "F8F6FACBE72D43BA979F2CCB375828AD2198498C628300E2B6A59D010830BA64",
        "A68641D0196480066F627806FE20BC824CCABC6F061CA09F0279F75991EB9E7C",
        "BA4FDD5A5E4470EB51610334CB50FDEC0688CAC5A5A2BE928D474A089AF3315D",
        "C6DE9F968AADE725F29CC74992E2B02C80AF56B58259CEDBCB4AF09ECCEAC015",
        "D6ED5C85B3F61FC62C57F039B0B3D6D671F1553C92DA904FBED57526D3715DBE",
        "1E056EBA3E1E1D3C46F3D761CF2103A4E8CE5184B093B34281C27FF838AC35B8",
        "A08345CC1AF51817F0676CD7B738DD4C91D3A88A8016A7E872C79B85D9F9473A",
        "D3FDCCFDEC31BA9FF8D9DB03777822EE21994C0FB8162A2106A257E3C78003C6",
        "A60533D86AE76DB4DC92B792C93144A87B9F7C41135D2FA9D50922E90434F32E",
        "C0702AF1BB0F608F369C16F3C1A04BEB8CAE1EF5833B2136C502BF97F975D1F8",
        "1C18B6C53C707A1726AC3A181575C97A47294421C43BF50161302EA8A07AE9E4",
        "5FC962E013A18EBB8C30190BE5C209DF20C3B66414197CCC6D1BEB6DE82929DB",
        "E8A1FCB7C3C070BF24A3B21B486DDC625766209C007935C2924FB0D0B32FC606",
        "8D16A2778C9E88F3679F7EB0A22A17198CE5E0742CE322D8E8DBBD1A83B18918",
        "5A64D5148AA2533F58E89315C7CE44BE3B4A001539571EDD90E237B73AA2D065",
        "241A3E548B117531A1EC9C3BB7817B9C170541F30E947236A2183E2B2DF48C58",
        "842F0380897E18C0ACAB21EFEEF11EF0819EE8B42C61CB252694FAC374477A1A",
        "AD2E7B23D26445BE988B32D51DE2170919261E893F688CA43D0D79DD72AD9251",
        "D94374797A1E19E12254E29B82E61FF6DCCED28D161513DD1BD30675407E0288",
        "FCF7224DE64D04B579AC5CFB85789C8BE4CB281C743D6FBA53464A81CE0E61C9",
        "86A11CBC63E82DC2EC8F223C57E276F28C96E88FE1AEEB63BFC4D08B058C32E2",
        "E83581CFD6FFBD92ED142F205AC353C80ABDF3328B00D84640E543A071D2C535",
        "50CCCF47E618F7AF022466E6D85ECE08E5D2BDBFF20BFF74C7FF8C7D899168AC",
        "B1ABCB72E7FD6D171C4B1330D59E8A731F9E1D122F9D378F2EA126BB76560561",
        "7F2629F5B68855367A92CACB33A8479E7E87B2C443CF665D17A3C40782AEBFE1",
        "8603826808E971B3602A1EDD1F5E07D29B7BB174746B5BDC220BEAD9A0B509D7",
        "95E129E2524D878686C794D015E9605AC8C0862ED7513210069E054D3F0FD3D0",
        "A227C34D0C869176C5A79AE0AF33CF32D53E93A7DC7B6C39977A3B7B7BECD342",
        "CA6111294B4CDF0F4ED89B05BF707FD96B2A3D1741A9E5127192070CFA65B3E5",
        "F8E09759C7AD90E56A4EC27137298F326B4AD859F492B0E8B430A6953F136D95",
        "729EB01F0119D19B42E031D2579AEABA2519875957B1FF99168F4F2E0D2E608A",
        "F0652C8229C305BDEB307586AF3CE437C908C7C07CE0D06EA0B255AEE97F288A",
        "7BEEE6A50869FD5F236B3A92BC09A167B006732928E46DBEFA27721AAEBEF0A2",
        "2DB980B18DF6151D9708CE8CFF6C8006E42F0323C53A24AC254822D0328ECF79",
        "CF4CA4D1745B9DB84D0072D6F573F0CFA722025B41890877BB24870FDF4442EF",
        "CE257D4161AF9C6CEB85B0D439EDD146884395E544550963D72828A6609EE818",
        "6188D9275785550EDC0AAABFC5B86DB7381EC42731E32525A9C9B357BF9DA46A",
        "1BDFA4559D736FD501F8D0F1E0F55CB5A002C65521A0F8C9CFF6C66FAE81DFA2",
        "EEAD9EED6EDB5F5F459383BC790338E0A68580FF9A5912B44B17884EB9BAFC0A",
        "F9A45174905BAB69C5A069044A6563D0C29F186088EE933AA2197D3ED7BD00E6",
        "F3BDDC10EE2DD1DEE773A611FBF41C7D22E4F1C6274F35A93260438218A4990B",
    ];

    // The A21 reference entry sequence, frame for frame, checked against a recording of this
    // server's own batch laid beside the reference session's. That comparison ended at sixty-six
    // frames on each side with an empty multiset difference, and this list is the same order.
    //
    // Four corrections separate it from the list that stood here before, each from that comparison:
    //   * 0x0381 keeps its number. It was moved to 0x03EB on the grounds that A21 registers no
    //     handler at 0x0381 -- true, and backwards: the reference sends these exact eight bytes
    //     there twice, and 0x03EB is where a handler does exist.
    //   * a sixth 0x000D, the guild-medal container A21 added.
    //   * CMD 0x01BA, which the reference pushes unprompted between the town-ready frame and CERA.
    //   * CMD 0x036F and the empty 0x000E are gone: the reference sends neither -- 0x036F nowhere
    //     in its 458 frames, and its one 0x000E is the daily refill's own delta, sent from the
    //     handler rather than composed here.
    // 62 - 2 + 2 = 62.
    private static readonly (byte Selector, ushort Type)[] AcceptedStaticIdentities =
    [
        (1, 0x0004), (0, 0x0002), (0, 0x0166), (0, 0x0166),
        (0, 0x0166), (0, 0x0166), (0, 0x0166), (0, 0x0167),
        (0, 0x0015), (0, 0x0164), (0, 0x0245), (0, 0x0013),
        (0, 0x0069), (0, 0x0002), (0, 0x0003), (0, 0x00CA),
        (0, 0x0005), (0, 0x000D), (0, 0x000D), (0, 0x000D),
        (0, 0x000D), (0, 0x000D), (0, 0x000D), (0, 0x0465),
        (0, 0x021E), (0, 0x0286), (0, 0x00AD), (0, 0x01C7),
        (0, 0x006C), (0, 0x0187), (0, 0x01B9), (0, 0x015F),
        (0, 0x00AC), (0, 0x00AE), (0, 0x017B), (0, 0x0381),
        (0, 0x0381), (0, 0x021F), (0, 0x03C1), (0, 0x00FB),
        (0, 0x00CD), (0, 0x00B1), (0, 0x036A), (0, 0x03D8),
        (0, 0x025B), (0, 0x0331), (0, 0x01EB), (0, 0x0061),
        (0, 0x0158), (0, 0x02D5), (0, 0x02D5), (0, 0x02D5),
        (0, 0x01A8), (0, 0x0009), (0, 0x0344), (0, 0x007C),
        (0, 0x0002), (1, 0x01BA), (0, 0x0035), (0, 0x0111),
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

        Assert.Equal(62, identities.Length);
        Assert.Equal(((byte)1, (ushort)0x0004), identities[0]);
        Assert.Equal(AcceptedStaticIdentities, identities);
    }

    [Fact]
    public void AcceptedEntryTranscript_ConditionalMessagesSkipOnlyThemselvesAndSupportStateIsOnce()
    {
        var frames = CaptureCurrentTranscript(SelectCharacterPayloadTestData.CreateComplete());
        var identities = Identities(frames);
        var expected = AcceptedStaticIdentities
            .Where((_, index) => index + 1 != 11)
            .ToArray();

        Assert.Equal(expected, identities);
        // 0x019F left the entry sequence: A21 answers it on request instead. Pinned at zero rather
        // than dropped, so re-adding it unsolicited turns this red instead of passing quietly.
        Assert.Equal(0, identities.Count(value => value == ((byte)0, (ushort)0x019F)));
        // In A21 the subtype-1 record is followed by USER_STATE, not by the skill block.
        var subtype1Index = Array.IndexOf(identities, ((byte)0, (ushort)0x0002), 8);
        Assert.Equal(((byte)0, (ushort)0x0003), identities[subtype1Index + 1]);
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
        // A21 puts the quest list ahead of the skill block, so what follows the combo frame is the
        // creature list. The combo frame staying immediately after skills is what this pins.
        Assert.Equal(((byte)0, (ushort)0x0069), identities[skillIndex + 2]);
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

        // Two things moved this frame's bytes, not one.
        //
        // The entry grew from 84 to 101, and those seventeen bytes are a trailing zero run. That
        // alone would leave every field in front of them untouched.
        //
        // But the two option-count bytes moved as well -- entry offsets 26 and 58, which used to be
        // written as the literal 2 and 3 and are now the number of populated option slots. This
        // fixture's item carries no chronicle or random option, so both are zero here. That is a
        // value change inside the region the span comparison covers, so the comparison below is not
        // evidence that nothing in front of the run changed; it is evidence that the delta and the
        // snapshot still encode an item the same way as each other, which is what it is for.
        Assert.Equal(
            "F9BE81C62166A8D13FDD70545AAC31351973CFCF6A84A0421BBFAF9E0E8A27FC",
            Sha256(deltaFrame));
        // Still the same claim -- the delta writes an item exactly as the snapshot does -- at A21's
        // entry length. The snapshot's first real item follows three virtual slots either way.
        Assert.Equal(
            snapshotBody.AsSpan(5 + (3 * 101), 101).ToArray(),
            deltaBody.AsSpan(3, 101).ToArray());
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
            .Where((_, index) => index + 1 is not (11 or 62))
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

    [Fact]
    public void RegisteredGameMessages_HaveExactlyOneCmdOrNotiClassification()
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(
            services,
            new ConfigurationBuilder().Build());
        var registrations = services
            .Where(value => value.ServiceType == typeof(ServerMessageEncoderRegistration))
            .Select(value => Assert.IsType<ServerMessageEncoderRegistration>(
                value.ImplementationInstance))
            .Where(value => value.Endpoint == NetworkEndpointKind.Game)
            .ToArray();
        var messageTypes = typeof(IGameServerMessage).Assembly.GetTypes()
            .Where(type => !type.IsAbstract
                && !type.IsInterface
                && typeof(IGameServerMessage).IsAssignableFrom(type))
            .OrderBy(type => type.FullName)
            .ToArray();

        Assert.NotEmpty(registrations);
        Assert.Equal(
            messageTypes,
            registrations.Select(registration => registration.MessageType)
                .OrderBy(type => type.FullName)
                .ToArray());
        Assert.All(registrations, registration =>
        {
            Assert.True(typeof(IGameServerMessage).IsAssignableFrom(registration.MessageType));
            var isCmd = typeof(IGameCmdMessage).IsAssignableFrom(registration.MessageType);
            var isNoti = typeof(IGameNotiMessage).IsAssignableFrom(registration.MessageType);
            Assert.True(isCmd ^ isNoti,
                $"{registration.MessageType.Name} must be exactly one of CMD or NOTI.");
        });
    }

    [Fact]
    public void RegisteredGameClientDecoders_AreCmdOnly()
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(
            services,
            new ConfigurationBuilder().Build());
        var registrations = services
            .Where(value => value.ServiceType == typeof(GameMessageDecoderRegistration))
            .Select(value => Assert.IsType<GameMessageDecoderRegistration>(
                value.ImplementationInstance))
            .ToArray();

        Assert.NotEmpty(registrations);
        Assert.All(registrations, registration =>
            Assert.Equal((byte)GameMessageClass.Cmd, registration.Selector));
        Assert.Equal(
            registrations.Length,
            registrations.Select(registration => (registration.Selector, registration.Type))
                .Distinct()
                .Count());
    }

    private static bool IsStateSynchronizationMessage(Type type) =>
        typeof(GameStateSynchronizationMessage).IsAssignableFrom(type)
        || typeof(SupplementalSynchronizationMessage).IsAssignableFrom(type);

    private static IReadOnlyList<byte[]> CaptureCurrentTranscript(
        Dfo.Server.Data.Game.SelectCharacterResult result) =>
        EncodeThroughRegisteredEncoders(SelectedCharacterStateMessageFactory.Create(result));

    private static IReadOnlyList<byte[]> EncodeThroughRegisteredEncoders(
        IReadOnlyList<IGameServerMessage> messages)
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

    private static (byte Selector, ushort Type)[] Identities(IReadOnlyList<byte[]> frames) =>
        frames.Select(frame =>
        {
            Assert.Equal(frame.Length, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(3, 4)));
            return (frame[0], BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1, 2)));
        }).ToArray();
}
