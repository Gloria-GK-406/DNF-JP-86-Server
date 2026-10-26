using System.Reflection;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Channel.Connect;

namespace Dfo.Server.Application.Tests.Commands;

/// <summary>
/// What a command handler is allowed to be given.
/// </summary>
/// <remarks>
/// A command handler drives one business action, and everything it needs to do that reaches it as
/// a domain shape: a domain service, or a domain store. Instances and snapshots are not on this
/// list because they are not injected -- a domain service hands out the instance, and a snapshot
/// arrives as a field of the command itself.
/// <para>
/// The rule is decided from the type rather than from a list of approved type names. A list would
/// have to be maintained by hand, and a rule nobody maintains is the same as no rule: this
/// repository already has one such list, in <c>AGENTS.md</c>, which drifted until eight message
/// handlers broke it while every check stayed green.
/// </para>
/// </remarks>
public sealed class CommandHandlerArchitectureTests
{
    /// <summary>
    /// Types that are not domain shapes and are still allowed, each with the reason it is allowed.
    /// </summary>
    /// <remarks>
    /// This is the one hand-kept list, and it is meant to stay short. An entry belongs here only
    /// when it is either an ambient platform capability or an explicitly named domain boundary
    /// whose accepted ubiquitous-language name intentionally does not use the canonical suffix.
    /// </remarks>
    private static readonly IReadOnlyDictionary<Type, string> Exempt =
        new Dictionary<Type, string>
        {
            [typeof(Dfo.Server.Abstractions.World.IAltarSchedulerWakeSignal)] = "仅唤醒原祭坛调度器的具名纯信号；不提供等待、排程、世界读取或队列权限。",
            [typeof(Dfo.Server.Abstractions.World.IDungeonCardPresentationControl)] = "仅原翻牌生产者的固定两阶段与精确取消，不提供world或任意调度能力。",
            [typeof(TimeProvider)] =
                "环境时钟。它不是这个游戏知道的任何东西，注入它是为了让时间在测试里可控。",
            [typeof(Dfo.Server.Abstractions.Effects.IReplayStateProjectionSink)] = "仅Joust首次成功在独立当前态读取后登记Inventory与Joust快照；不嵌套派发或授raw。",
            [typeof(Dfo.Server.Abstractions.Sessions.ISelectedSessionSnapshotQuery)] = "仅Joust按原采样点取得广播的精确选中集；只读、不调度或修改状态。",
            [typeof(ICommandSelectedSessionCleanup)] = "只执行已批准选角与回选的固定精确会话清理，不授正常工作或通用恢复。",
            [typeof(Dfo.Server.Protocol.Game.SelectCharacter.ISelectedCharacterStateMessageFactory)] = "既有纯协议帧工厂及完整预校验，不读写世界。",
            [typeof(Dfo.Server.Protocol.Game.SelectCharacter.SelectCharacterDeliveryOptions)] = "既有协议尾帧发送配置。",
            [typeof(Dfo.Server.Protocol.Game.StateSynchronization.LotteryCounterExperimentOptions)] = "既有彩票实验协议开关，默认关闭。",
            [typeof(ICommandWorkUnits)] = "当前CMD的显式事务边界，不派发其它CMD。",
            [typeof(Dfo.Server.Abstractions.Networking.ICommandMessageOutbox)] = "只登记当前CMD的消息，不等待传输。",
            [typeof(Dfo.Server.Abstractions.Networking.ICommandConnectionClosures)] = "只登记精确连接关闭。",
            [typeof(Dfo.Server.Abstractions.DomainEvents.ICommandProjections)] = "只登记即时投影，不发布领域事实。",
            [typeof(Dfo.Server.Protocol.Game.IGameCommandOutbox)] = "Protocol所有的CMD/NOTI强类型登记。",
            [typeof(Dfo.Server.Abstractions.Execution.IOutboundOrdering)] = "显式收件人的登记顺序组。",
            [typeof(Dfo.Server.Abstractions.Inventory.IInventorySlotMappingRuleService)] = "固定协议槽位到业务位置的地址换算。",
            [typeof(IPartyService)] =
                "队伍聚合的权威边界按领域语言明确命名为 PartyService；用户要求该名称不追加 Domain 后缀。",
        };

    private interface IPretendOutbox;
    private interface IPretendSnapshotQuery;
    private interface IPretendCardPresentationControl { void Cancel(); }
    private interface IPretendAltarSchedulerWakeSignal { void Wake(); }

    private static readonly Assembly Abstractions = typeof(ICharacterDomainService).Assembly;

    /// <summary>
    /// The sweep actually finds handlers.
    /// </summary>
    /// <remarks>
    /// Without this, a reflection query that stopped matching -- a renamed contract, a moved
    /// assembly -- would turn every check below into an assertion about an empty sequence, and an
    /// empty sequence has no violations in it.
    /// </remarks>
    [Fact]
    public void TheSweepFindsTheCommandHandlers() =>
        Assert.NotEmpty(ProductionCommandHandlers());

    [Fact]
    public void ProductionCommandHandlers_OnlyTakeDomainShapes()
    {
        var violations = ProductionCommandHandlers()
            .SelectMany(handler => handler.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => (Handler: handler, Parameter: parameter)))
            .Where(candidate => !IsPermitted(candidate.Parameter.ParameterType))
            .Select(candidate =>
                $"{candidate.Handler.Name}({candidate.Parameter.ParameterType.Name} "
                    + $"{candidate.Parameter.Name})")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    /// <summary>
    /// A command handler that dispatched another command would hide half a use case behind the
    /// first one, so the pipeline could neither order nor report it.
    /// </summary>
    /// <remarks>
    /// The whitelist above already refuses <see cref="ICommandBus"/>, because it is neither a
    /// domain service nor a domain store. This stays as its own check because it is its own rule:
    /// it would still hold if the vocabulary changed.
    /// </remarks>
    [Fact]
    public void ProductionCommandHandlers_DoNotDependOnCommandBus()
    {
        var violations = ProductionCommandHandlers()
            .Where(type => type.GetConstructors().Any(constructor =>
                constructor.GetParameters().Any(parameter =>
                    parameter.ParameterType == typeof(ICommandBus))))
            .Select(type => type.FullName)
            .ToArray();

        Assert.Empty(violations);
    }

    /// <summary>
    /// The rule refuses what it is meant to refuse.
    /// </summary>
    /// <remarks>
    /// Without this, a rule that accepted everything would pass the check above just as quietly as
    /// a repository with no violations. The types below stand for the four ways the injection
    /// surface has actually gone wrong here: reaching a single table, reaching a storage seam,
    /// reaching a container, and dispatching another command; the two senders stand for a command
    /// calling a sender itself (AC-011).
    /// </remarks>
    [Theory]
    [InlineData(typeof(Dfo.Server.Abstractions.Persistence.ICharacterTable))]
    [InlineData(typeof(Dfo.Server.Abstractions.Sessions.ISelectedSessionPositionPersistence))]
    [InlineData(typeof(ICommandBus))]
    [InlineData(typeof(Dfo.Server.Abstractions.Networking.INetworkMessageSender))]
    [InlineData(typeof(Dfo.Server.Protocol.Game.IGameMessageSender))]
    [InlineData(typeof(Dfo.Server.Abstractions.Networking.INetworkConnectionCloser))]
    [InlineData(typeof(Dfo.Server.Abstractions.Execution.IGameplayQueue))]
    [InlineData(typeof(Dfo.Server.Application.Execution.GameplayExecutor))]
    [InlineData(typeof(IServiceProvider))]
    [InlineData(typeof(IPretendOutbox))]
    [InlineData(typeof(IPretendSnapshotQuery))]
    [InlineData(typeof(IPretendCardPresentationControl))]
    [InlineData(typeof(Dfo.Server.Protocol.Game.CardReward.DungeonCardPresentationCoordinator))]
    [InlineData(typeof(IPretendAltarSchedulerWakeSignal))]
    [InlineData(typeof(Dfo.Server.Protocol.Game.Altar.AltarWakeSignal))]
    [InlineData(typeof(Dfo.Server.Protocol.Game.Altar.AltarProjectionDelivery))]
    [InlineData(typeof(string))]
    public void ATypeOutsideTheVocabularyIsRefused(Type type) => Assert.False(IsPermitted(type));

    [Theory]
    [InlineData(typeof(Dfo.Server.Abstractions.World.IDungeonCardPresentationControl))]
    [InlineData(typeof(ICharacterDomainService))]
    [InlineData(typeof(IPartyService))]
    [InlineData(typeof(Dfo.Server.Abstractions.Inventory.IItemDomainStore))]
    [InlineData(typeof(TimeProvider))]
    [InlineData(typeof(Dfo.Server.Abstractions.Sessions.ISelectedSessionSnapshotQuery))]
    [InlineData(typeof(Dfo.Server.Abstractions.Effects.IReplayStateProjectionSink))]
    [InlineData(typeof(ICommandSelectedSessionCleanup))]
    [InlineData(typeof(Dfo.Server.Protocol.Game.SelectCharacter.ISelectedCharacterStateMessageFactory))]
    [InlineData(typeof(Dfo.Server.Protocol.Game.SelectCharacter.SelectCharacterDeliveryOptions))]
    [InlineData(typeof(Dfo.Server.Protocol.Game.StateSynchronization.LotteryCounterExperimentOptions))]
    [InlineData(typeof(ICommandWorkUnits))]
    [InlineData(typeof(Dfo.Server.Abstractions.World.IAltarSchedulerWakeSignal))]
    [InlineData(typeof(Dfo.Server.Abstractions.Networking.ICommandMessageOutbox))]
    [InlineData(typeof(Dfo.Server.Abstractions.Networking.ICommandConnectionClosures))]
    [InlineData(typeof(Dfo.Server.Abstractions.DomainEvents.ICommandProjections))]
    [InlineData(typeof(Dfo.Server.Protocol.Game.IGameCommandOutbox))]
    [InlineData(typeof(Dfo.Server.Abstractions.Execution.IOutboundOrdering))]
    [InlineData(typeof(Dfo.Server.Abstractions.Inventory.IInventorySlotMappingRuleService))]
    [InlineData(typeof(Microsoft.Extensions.Logging.ILogger<CommandHandlerArchitectureTests>))]
    public void ADomainShapeOrANamedExemptionIsPermitted(Type type) => Assert.True(IsPermitted(type));

    /// <summary>Every exemption states why it is one.</summary>
    [Fact]
    public void EveryExemptionCarriesItsReason() =>
        Assert.All(Exempt, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value)));

    private static IEnumerable<Type> ProductionCommandHandlers() =>
        new[] { typeof(ChannelConnectHandler).Assembly, typeof(Dfo.Server.Application.Authentication.RegisterAccountCommand).Assembly }
            .Distinct().SelectMany(assembly => assembly.GetTypes())
            .Where(type => !type.IsAbstract && type.GetInterfaces().Any(contract =>
                contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)));

    private static bool IsPermitted(Type type) => Exempt.ContainsKey(type) || IsDomainShape(type)
        || type == typeof(Microsoft.Extensions.Logging.ILogger)
        || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Microsoft.Extensions.Logging.ILogger<>);

    /// <summary>
    /// A domain shape is an interface the seam layer publishes whose name says which of the two it
    /// is.
    /// </summary>
    /// <remarks>
    /// Both halves matter. The suffix alone would let a type inside <c>Application</c> or
    /// <c>Protocol</c> claim to be a seam by naming itself one; the assembly alone would admit
    /// every table gateway and registry the seam layer also declares.
    /// </remarks>
    private static bool IsDomainShape(Type type) =>
        type.IsInterface
        && type.Assembly == Abstractions
        && (type.Name.EndsWith("DomainService", StringComparison.Ordinal)
            || type.Name.EndsWith("DomainStore", StringComparison.Ordinal));
}
