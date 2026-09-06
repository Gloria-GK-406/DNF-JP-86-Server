# 发现：种类那一半的连带链（控制器自查，spec 阶段）

取证时间：本轮 spec 阶段开始时，工作区 HEAD 565d33fe。
每条都给了 `文件:行` 与原文，可复核。

## 1. 「把 1 改成 8」不足以修好，它只是把拒绝从一道守卫挪到另一道

这是本次调查最重要的一条，它改变了本刀的体量估计。

**存盘侧对 kind 8 的行有三条硬要求**，`InventoryEntryAssembly.SaveAsync`
（`Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs:149-156`）：

```csharp
var avatars = lease.Inventory.Containers.SelectMany(value => value.Items)
    .Where(value => value.Core.ItemKind == 8).ToArray();
if (avatars.Any(value => value.Core.Value <= 0 || value.AvatarDetail is null
                         || value.AvatarDetail.JewelSocket.Length != 30))
{
    throw new InvalidDataException(
        "A dirty avatar lease requires its exact 30-byte detail row.");
}
```

**而发放路径造不出这种行。** `ItemContainers.LandAsync` 在
`Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs:366` 调
`StackableItemCore.Create(itemKind, itemId, opened.Count)`，该方法
（同文件 `:31-39`）从一个全零的 `Empty` 出发，只写四个字段：

```csharp
public static InventoryItemCoreState Create(byte itemKind, int itemId, int count) =>
    Empty with
    {
        ItemKind = itemKind,
        ItemId = itemId,
        Value = count,
        Marker16 = -1,
        RandomOptionChangedIndex = byte.MaxValue,
    };
```

**没有 `AvatarDetail`，而且 `Value` 写的是数量。**
对时装来说 `Value` 的语义是 AvatarUid（`Docs/GM工具_新背包表结构与ItemCore语义.md:9`：
「`ItemCore.Value` 对时装是 `AvatarUid`」）。

于是两种情形对照：

| 发放一件时装之后再存盘 | 今天（kind 1） | 只把 1 改成 8 |
|---|---|---|
| 触发的守卫 | `SaveAsync` 的 `worldClass == Avatar && ItemKind != 8` 分类守卫（`:173-178`） | `SaveAsync` 的 `AvatarDetail is null` 检查（`:150-156`） |
| 结果 | 抛 `InvalidDataException`，回滚 | 抛 `InvalidDataException`，回滚 |

**两种都失败。** 差别只在抛哪一句。
（`Core.Value <= 0` 这一条反而会被绕过——数量 1 使 `Value = 1`，
即「AvatarUid = 1」，那是个假身份，还会撞上世界表的时装唯一性守卫。）

## 2. 真正的根在发放路径的形状，不在那个 1

`StackableItemCore` 的类型级 `<remarks>`（`ItemContainers.cs:13-17`）自己说明了为什么：

> A split makes a second pile out of one. The pile it makes carries only what a pile is -- kind,
> identifier, count -- **because everything else a core can hold describes an individual piece of
> gear, and gear does not split.**

即：**发放路径只会造「堆」形状的 core，而时装是「单件 + 自己的身份 + 30 字节珠宝槽」形状。**
8／1 不一致是这个形状缺口的症状，不是病本身。

**这对本契约的影响**：SC-2 要求「时装经发放路径入库后下一次存盘被自己守卫拒绝并回滚」
这一失败模式不再存在。只翻种类满足不了 SC-2。要满足它，发放路径必须能为时装
造出带身份与细节的行——这正是所有者下一步「GM 发一件时装做实机」要走的通路。

## 3. 快捷栏那一条：修好之后是一条既有规则终于生效

`ItemSlotRuleService.AllowsQuickSlot`
（`Server/src/Dfo.Server.Application/Inventory/ItemSlotRuleService.cs:51-53`）：

```csharp
internal static bool AllowsQuickSlot(byte itemKind) =>
    itemKind is not (KindCreature or KindCreatureEquipment or KindCreatureConsumable
        or KindAvatar or KindAvatarEmblem);
```

`KindAvatar = 8`（同文件 `:28`）。该类的 `<remarks>`（`:16-18`）写着：

> The exclusions are the kinds whose home is another list entirely -- creatures and their gear,
> **avatars and their emblems**.

**规则本来就打算排除时装。今天排不掉，只因为写入方把时装标成了 kind 1。**
所以「时装不能被拖进快捷栏 3..8」不是本刀新增的规则，是本刀让既有规则第一次真的生效。
**这仍然是一处可观察行为变化**，要进变化清单。

## 4. 一处**不会**变的连带（已推演，待实现阶段用判据坐实）

`ItemArrivalRuleService.BandsFor`（`ItemArrivalRuleService.cs:118-137`）第一个分支：

```csharp
if (placement.ItemKind == EquipmentKind || !ItemSlotRuleService.AllowsQuickSlot(placement.ItemKind))
{
    yield return own;
    yield break;
}
```

`EquipmentKind = 1`（同文件 `:96`）。

- 今天 kind = 1：命中 `== EquipmentKind` → 只给自己的带，`break`。
- 改后 kind = 8：`8 != 1`，但 `!AllowsQuickSlot(8) == !false == true` → 同样只给自己的带，`break`。

**两条路走到同一个结果，落点不变。** 这是一条要写进清单的「查过、没变」，
不是一条可以不查的。

## 5. 本文件**没有**回答的

- 时装类型名 → 槽号 0..11 的映射（另两路调查在做）。
- `InventoryEntryAssembly.cs:373` 的 `WithAvatarDetail` 在装载侧的连带。
- 发放路径若要为时装造身份，UID 从哪来（旧文档写的
  `character_avatar_uid_sequence` 已随上一轮 `avatar_uid` 退役而作废，
  今天的身份是 `items.item_uid`）。

---

# 追加：量具的落点（plan 阶段 cause investigation）

## 7. 拒绝那条日志已经记了客户端点名的槽号，缺的只是模板号

`Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs:492-498`
（`MoveItemSpaceHandler.ResolveAsync` 的拒绝分支）：

```csharp
logger.LogInformation(
    "Move item space {ConnectionId} character={CharacterId}: {From}->{To} refused as {Status}",
    context.Connection.ConnectionId,
    session.CharacterId.Value,
    message.SourceSlotIndex,
    message.DestinationSlotIndex,
    result.Status);
```

`{To}` 就是 `message.DestinationSlotIndex`——**客户端点名的那一格，已经在记了**。

**缺的是模板号。** 同一方法的成功分支（`:480-489`）记 `result.ItemId`，拒绝分支不记。
而 `ItemMoveResult.Refused(...)` 在 `PutOnAsync` 的两个拒绝点
（`:325` `NotWearable`、`:327` `ItemCannotOccupySlot`）都只传了一个 `ItemMoveStatus`，
**没有带上 `piece.ItemId`**——尽管那一刻 `piece` 就在手上（`:317` 读出来的）。

所以量具要做的是两件小事：让拒绝结果带上模板号，并把它写进那条已有的日志。
**不新建日志通路、不新建类型、不动协议。**

## 8. 注入面不受机检判据约束——`MoveItemSpaceHandler` 已经注入了 `ILogger`

`MoveItemSpaceSlice.cs:437-441`：

```csharp
public sealed class MoveItemSpaceHandler(
    ISelectedSessionDomainService selectedSessions,
    ICommandBus commandBus,
    IGameMessageSender sender,
    ILogger<MoveItemSpaceHandler> logger) : ClientMessageHandler<MoveItemSpaceRequest>
```

`CommandHandlerArchitectureTests` 的具名豁免清单里只有 `TimeProvider`
（`Server/tests/Dfo.Server.Application.Tests/Commands/CommandHandlerArchitectureTests.cs:33-36`），
但它管的是**命令处理器**（`ICommandHandler<>`），不是**协议消息处理器**
（`ClientMessageHandler<>`）。二者是两回事，本仓 `AGENTS.md` 也分两节记。
**因此量具不触碰那条判据，也不需要扩充任何豁免清单。**

`ILogger` 在 Protocol 切片里是既有形状而非特例：`Server/src/Dfo.Server.Protocol` 下
至少 12 个切片文件注入它（含 `CardRewardHandlers`、`CreateCharacterSlice`、
`DungeonEntrySlice`、`DungeonClearSlice` 等）。
