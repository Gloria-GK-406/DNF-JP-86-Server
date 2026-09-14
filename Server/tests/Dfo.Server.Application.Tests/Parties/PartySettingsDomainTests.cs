using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Parties;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Parties;

public sealed class PartySettingsDomainTests
{
    [Fact]
    public void SettingsAndTitleAreDetachedStructuralValues()
    {
        var source = SettingsBytes(capacity: 3, PartyCategory.Cycle, target: 0x1234);
        var titleSource = new byte[] { 0x41, 0x00, 0xFF };
        var settings = new PartySettings(source);
        var title = new PartyTitle(titleSource);

        source[2] = 1;
        source[9] = 0x7F;
        titleSource[0] = 0;
        var exportedSettings = settings.ToWireBytes();
        var exportedTitle = title.ToBytes();
        exportedSettings[10] = 0;
        exportedTitle[1] = 1;

        Assert.Equal((byte)3, settings.Capacity);
        Assert.Equal(PartyCategory.Cycle, settings.Category);
        Assert.Equal((ushort)0x1234, settings.ModeTargetRaw);
        Assert.Equal(SettingsBytes(3, PartyCategory.Cycle, 0x1234), settings.ToWireBytes());
        Assert.Equal(new byte[] { 0x41, 0x00, 0xFF }, title.ToBytes());
        Assert.Equal(new PartySettings(SettingsBytes(3, PartyCategory.Cycle, 0x1234)), settings);
        Assert.Equal(new PartyTitle([0x41, 0x00, 0xFF]), title);
        Assert.Throws<ArgumentException>(() => new PartySettings(new byte[11]));
    }

    [Fact]
    public void RulesOwnTheDefaultAndEverySharedValidityDecision()
    {
        IPartyModeRuleService rules = new PartyModeRuleService();

        Assert.Equal(new byte[] { 0, 0, 4, 0, 0, 0, 0, 5, 0, 2, 0, 0 },
            rules.DefaultSettings.ToWireBytes());
        Assert.Equal(
            PartyModeRuleFailure.None,
            rules.Validate(new PartySettings(SettingsBytes(4, PartyCategory.Story, 7)), 4));
        Assert.Equal(
            PartyModeRuleFailure.None,
            rules.Validate(new PartySettings(SettingsBytes(4, PartyCategory.Cycle, 7)), 4));
        Assert.Equal(
            PartyModeRuleFailure.None,
            rules.Validate(new PartySettings(SettingsBytes(4, PartyCategory.Normal, 0)), 4));
        foreach (var capacity in Enumerable.Range(1, 4).Select(value => (byte)value))
        {
            Assert.Equal(
                PartyModeRuleFailure.None,
                rules.Validate(
                    new PartySettings(SettingsBytes(capacity, PartyCategory.Normal, 0)),
                    capacity));
        }

        Assert.Equal(
            PartyModeRuleFailure.InvalidModeTarget,
            rules.Validate(new PartySettings(SettingsBytes(4, PartyCategory.Normal, 7)), 1));

        Assert.Equal(
            PartyModeRuleFailure.UnknownCategory,
            rules.Validate(new PartySettings(SettingsBytes(4, (PartyCategory)3, 7)), 1));
        Assert.Equal(
            PartyModeRuleFailure.InvalidCapacity,
            rules.Validate(new PartySettings(SettingsBytes(0, PartyCategory.Normal, 7)), 0));
        Assert.Equal(
            PartyModeRuleFailure.InvalidCapacity,
            rules.Validate(new PartySettings(SettingsBytes(5, PartyCategory.Normal, 7)), 1));
        Assert.Equal(
            PartyModeRuleFailure.CapacityBelowMemberCount,
            rules.Validate(new PartySettings(SettingsBytes(1, PartyCategory.Normal, 7)), 2));
    }

    [Fact]
    public void ProductionGraphRegistersOneSharedModeRuleService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPartyMemberStatusDomainService>(
            new StubPartyMemberStatusDomainService());
        new ApplicationModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<IPartyModeRuleService>(),
            provider.GetRequiredService<IPartyModeRuleService>());
    }

    [Fact]
    public void ExactLeaderCreatesThenUpdatesOnePartyWithoutChangingItsIdentityOrMembers()
    {
        var fixture = new PartySettingsFixture();
        var leader = fixture.Admit(1, 101);
        var createSettings = Settings(4, PartyCategory.Story, 31);
        var created = fixture.Parties.CreateOrUpdateSettingsExact(
            leader, createSettings, new PartyTitle([0x61]));

        Assert.True(created.Succeeded);
        Assert.Equal(PartySettingsChangeKind.Created, created.Notifications.SettingsChange);
        var first = Assert.IsType<PartySnapshot>(created.Notifications.Snapshot);
        Assert.Equal(leader.WireUserId, first.LeaderWireUserId);
        Assert.Equal(createSettings, first.Settings);
        Assert.Equal(new PartyTitle([0x61]), first.Title);
        Assert.Single(first.Members);

        var updateSettings = Settings(2, PartyCategory.Cycle, 44);
        var updated = fixture.Parties.CreateOrUpdateSettingsExact(
            leader, updateSettings, new PartyTitle([0x62, 0x63]));
        var second = Assert.IsType<PartySnapshot>(updated.Notifications.Snapshot);
        Assert.Equal(PartySettingsChangeKind.Updated, updated.Notifications.SettingsChange);

        Assert.Equal(first.PartyId, second.PartyId);
        Assert.Equal(first.Generation, second.Generation);
        Assert.Equal(first.Revision + 1, second.Revision);
        Assert.Equal(
            first.Members.Select(value => (value.Character, value.Slot, value.Endpoint, value.HpPercent)),
            second.Members.Select(value => (value.Character, value.Slot, value.Endpoint, value.HpPercent)));
        Assert.Equal(updateSettings, second.Settings);
        Assert.Equal(new PartyTitle([0x62, 0x63]), second.Title);
        AssertSnapshotEqual(second, fixture.Parties.FindParty(leader));
    }

    [Fact]
    public void ServerDisablesReturnUserPartyWithoutChangingTheOtherSubmittedSettings()
    {
        var fixture = new PartySettingsFixture();
        var leader = fixture.Admit(1, 101);
        var submittedBytes = SettingsBytes(3, PartyCategory.Cycle, 0x1234);
        submittedBytes[1] = 1;
        var submitted = new PartySettings(submittedBytes);

        var result = fixture.Parties.CreateOrUpdateSettingsExact(
            leader, submitted, new PartyTitle([0x61]));

        Assert.True(result.Succeeded);
        var snapshot = Assert.IsType<PartySnapshot>(result.Notifications.Snapshot);
        var expectedBytes = (byte[])submittedBytes.Clone();
        expectedBytes[1] = 0;
        Assert.Equal(expectedBytes, snapshot.Settings.ToWireBytes());
        Assert.Equal(expectedBytes, fixture.Parties.FindParty(leader)!.Settings.ToWireBytes());
    }

    [Fact]
    public void InvitationsCreateANormalPartyAndItsCapacityControlsAdmission()
    {
        var fixture = new PartySettingsFixture();
        var leader = fixture.Admit(1, 101);
        var second = fixture.Admit(2, 202);
        var third = fixture.Admit(3, 303);

        fixture.Form(leader, second);
        var formed = Assert.IsType<PartySnapshot>(fixture.Parties.FindParty(leader));
        Assert.Equal(PartyCategory.Normal, formed.Settings.Category);
        Assert.Equal((byte)4, formed.Settings.Capacity);

        Assert.True(fixture.Parties.CreateOrUpdateSettingsExact(
            leader,
            Settings(2, PartyCategory.Normal, 0),
            PartyTitle.Empty).Succeeded);
        Assert.Equal(
            PartyFailureReason.PartyFull,
            fixture.Parties.RequestInvite(leader, third.WireUserId, Profile(1)).Failure);
    }

    [Fact]
    public void CapacityChangedAfterInvitationIsRecheckedOnAcceptWithoutConsumingTheInvite()
    {
        var fixture = new PartySettingsFixture();
        var leader = fixture.Admit(1, 101);
        var second = fixture.Admit(2, 202);
        var third = fixture.Admit(3, 303);
        fixture.Form(leader, second);
        Assert.True(fixture.Parties.RequestInvite(leader, third.WireUserId, Profile(1)).Succeeded);
        Assert.True(fixture.Parties.CreateOrUpdateSettingsExact(
            leader,
            Settings(2, PartyCategory.Normal, 0),
            PartyTitle.Empty).Succeeded);
        var full = Assert.IsType<PartySnapshot>(fixture.Parties.FindParty(leader));

        Assert.Equal(
            PartyFailureReason.PartyFull,
            fixture.Parties.AcceptInvite(third, leader.WireUserId).Failure);
        AssertSnapshotEqual(full, fixture.Parties.FindParty(leader));
        Assert.Null(fixture.Parties.FindParty(third));

        Assert.True(fixture.Parties.CreateOrUpdateSettingsExact(
            leader,
            Settings(3, PartyCategory.Normal, 0),
            PartyTitle.Empty).Succeeded);
        Assert.True(fixture.Parties.AcceptInvite(third, leader.WireUserId).Succeeded);
    }

    [Fact]
    public void DefaultAdmissionFailureCannotPartiallyCreateOrConsumeAnInvitation()
    {
        var rules = new MutableDefaultRules(Settings(4, PartyCategory.Normal, 0));
        var fixture = new PartySettingsFixture(rules);
        var leader = fixture.Admit(1, 101);
        var invitee = fixture.Admit(2, 202);
        Assert.True(fixture.Parties.RequestInvite(leader, invitee.WireUserId, Profile(1)).Succeeded);

        rules.DefaultSettings = Settings(1, PartyCategory.Normal, 0);
        Assert.Equal(
            PartyFailureReason.PartyFull,
            fixture.Parties.AcceptInvite(invitee, leader.WireUserId).Failure);
        Assert.Null(fixture.Parties.FindParty(leader));
        Assert.Null(fixture.Parties.FindParty(invitee));

        rules.DefaultSettings = Settings(4, PartyCategory.Normal, 0);
        Assert.True(fixture.Parties.AcceptInvite(invitee, leader.WireUserId).Succeeded);
    }

    [Fact]
    public void RejectedSettingsLeaveSnapshotRevisionAndInvitationsUntouched()
    {
        var fixture = new PartySettingsFixture();
        var leader = fixture.Admit(1, 101);
        var member = fixture.Admit(2, 202);
        var pending = fixture.Admit(3, 303);
        fixture.Form(leader, member);
        Assert.True(fixture.Parties.RequestInvite(leader, pending.WireUserId, Profile(8)).Succeeded);
        var before = Assert.IsType<PartySnapshot>(fixture.Parties.FindParty(leader));

        Assert.Equal(
            PartyFailureReason.NotPartyLeader,
            fixture.Parties.CreateOrUpdateSettingsExact(
                member, Settings(4, PartyCategory.Story, 1), PartyTitle.Empty).Failure);
        Assert.Equal(
            PartyFailureReason.InvalidPartyCapacity,
            fixture.Parties.CreateOrUpdateSettingsExact(
                leader, Settings(0, PartyCategory.Normal, 1), PartyTitle.Empty).Failure);
        Assert.Equal(
            PartyFailureReason.PartyCapacityBelowMemberCount,
            fixture.Parties.CreateOrUpdateSettingsExact(
                leader, Settings(1, PartyCategory.Normal, 1), PartyTitle.Empty).Failure);
        Assert.Equal(
            PartyFailureReason.UnknownPartyCategory,
            fixture.Parties.CreateOrUpdateSettingsExact(
                leader, Settings(4, (PartyCategory)9, 1), PartyTitle.Empty).Failure);
        Assert.Equal(
            PartyFailureReason.InvalidPartyModeTarget,
            fixture.Parties.CreateOrUpdateSettingsExact(
                leader, Settings(4, PartyCategory.Normal, 1), PartyTitle.Empty).Failure);

        AssertSnapshotEqual(before, fixture.Parties.FindParty(member));
        Assert.True(fixture.Parties.AcceptInvite(pending, leader.WireUserId).Succeeded);
        var afterAccept = Assert.IsType<PartySnapshot>(fixture.Parties.FindParty(member));

        var replacement = fixture.Replace(leader, 2);
        Assert.Equal(
            PartyFailureReason.CallerNotCurrent,
            fixture.Parties.CreateOrUpdateSettingsExact(
                leader, Settings(4, PartyCategory.Normal, 1), PartyTitle.Empty).Failure);
        AssertSnapshotEqual(afterAccept, fixture.Parties.FindParty(member));
        AssertSnapshotEqual(afterAccept, fixture.Parties.FindParty(replacement));
    }

    [Fact]
    public async Task ConcurrentUpdatesAreLinearizedIntoUniqueRevisionsAndDetachedResults()
    {
        var fixture = new PartySettingsFixture();
        var leader = fixture.Admit(1, 101);
        Assert.True(fixture.Parties.CreateOrUpdateSettingsExact(
            leader, Settings(4, PartyCategory.Normal, 0), PartyTitle.Empty).Succeeded);
        using var start = new Barrier(3);

        var first = Task.Run(() =>
        {
            start.SignalAndWait();
            return fixture.Parties.CreateOrUpdateSettingsExact(
                leader, Settings(4, PartyCategory.Story, 11), new PartyTitle([0x31]));
        });
        var second = Task.Run(() =>
        {
            start.SignalAndWait();
            return fixture.Parties.CreateOrUpdateSettingsExact(
                leader, Settings(4, PartyCategory.Cycle, 22), new PartyTitle([0x32]));
        });
        start.SignalAndWait();
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.All(results, result => Assert.True(result.Succeeded));
        var snapshots = results.Select(result =>
            Assert.IsType<PartySnapshot>(result.Notifications.Snapshot)).ToArray();
        Assert.Equal([2L, 3L], snapshots.Select(value => value.Revision).Order().ToArray());
        var final = Assert.IsType<PartySnapshot>(fixture.Parties.FindParty(leader));
        AssertSnapshotEqual(snapshots.Single(value => value.Revision == 3), final);
        Assert.NotEqual(snapshots[0].Settings, snapshots[1].Settings);
    }

    private static void AssertSnapshotEqual(PartySnapshot expected, PartySnapshot? actualValue)
    {
        var actual = Assert.IsType<PartySnapshot>(actualValue);
        Assert.Equal(expected.PartyId, actual.PartyId);
        Assert.Equal(expected.Generation, actual.Generation);
        Assert.Equal(expected.Revision, actual.Revision);
        Assert.Equal(expected.LeaderWireUserId, actual.LeaderWireUserId);
        Assert.Equal(expected.Settings, actual.Settings);
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(
            expected.Members.Select(value => (value.Character, value.Slot, value.Endpoint, value.HpPercent)),
            actual.Members.Select(value => (value.Character, value.Slot, value.Endpoint, value.HpPercent)));
    }

    private static byte[] SettingsBytes(byte capacity, PartyCategory category, ushort target)
    {
        var bytes = new byte[] { 8, 0, capacity, 6, 5, 4, 3, 2, 1, (byte)category, 0, 0 };
        bytes[10] = (byte)target;
        bytes[11] = (byte)(target >> 8);
        return bytes;
    }

    private static PartySettings Settings(byte capacity, PartyCategory category, ushort target) =>
        new(SettingsBytes(capacity, category, target));

    private static PartyInviteDisplayState Profile(int reviveCoinCount) =>
        new(reviveCoinCount, 188, 0, 0);

    private sealed class PartySettingsFixture
    {
        private int nextConnection;

        public PartySettingsFixture(IPartyModeRuleService? rules = null)
        {
            Parties = new PartyServiceTestAdapter(
                Sessions,
                Presence,
                new StubPartyMemberStatusDomainService(),
                rules ?? new PartyModeRuleService(),
                TimeProvider.System);
        }

        public SelectedSessionDomainService Sessions { get; } = new();
        public SessionPresenceDomainService Presence { get; } = new();
        public PartyServiceTestAdapter Parties { get; }

        public SelectedSessionIdentity Admit(int account, int character, long generation = 1)
        {
            var session = Session(account, character, generation);
            Sessions.Select(session);
            Presence.Admit(new SessionPresence(
                session.CharacterId, session.ListenerPort, 1, 1, true, session,
                ChannelId: 11));
            return session;
        }

        public SelectedSessionIdentity Replace(SelectedSessionIdentity old, long generation)
        {
            var replacement = Session(old.AccountId.Value, old.CharacterId.Value, generation);
            Sessions.Select(replacement);
            Presence.Admit(new SessionPresence(
                replacement.CharacterId, replacement.ListenerPort, 1, 1, true, replacement,
                ChannelId: 11));
            return replacement;
        }

        public PartyOperationResult Form(
            SelectedSessionIdentity leader,
            SelectedSessionIdentity member)
        {
            Assert.True(Parties.RequestInvite(
                leader, member.WireUserId, Profile(1)).Succeeded);
            var result = Parties.AcceptInvite(member, leader.WireUserId);
            Assert.True(result.Succeeded);
            return result;
        }

        private SelectedSessionIdentity Session(int account, int character, long generation) =>
            SelectedSessionIdentity.Create(
                new AccountId(account),
                new CharacterId(character),
                new NetworkConnectionId($"Game:party-settings-{++nextConnection}"),
                generation,
                10011);
    }

    private sealed class MutableDefaultRules(PartySettings defaultSettings)
        : IPartyModeRuleService
    {
        private readonly PartyModeRuleService inner = new();

        public byte MaximumCapacity => inner.MaximumCapacity;
        public PartySettings DefaultSettings { get; set; } = defaultSettings;

        public PartySettings Normalize(PartySettings settings) => inner.Normalize(settings);

        public PartyModeRuleFailure Validate(PartySettings settings, int memberCount) =>
            inner.Validate(settings, memberCount);

        public PartyModeRuleFailure ValidateAdmission(
            PartySettings settings,
            int currentMemberCount) =>
            inner.ValidateAdmission(settings, currentMemberCount);
    }
}
