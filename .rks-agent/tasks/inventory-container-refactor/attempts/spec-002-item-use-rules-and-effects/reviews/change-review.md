---
kind: rks-review
decision: approved
---

# 变更评审：道具使用的判定、效果与诚实的拒绝

评审对象：工作区未提交改动，基线提交 `a0149f5`。
判据：同目录 `TaskContract.md`(rev 1) 与 `spec.md`(RQ-001..012 / AC-001..018 / EV-001..006)、
`protocol/patterns/data/probes/cmd-reason-messages-20260818.md`、
`protocol/patterns/data/probes/pvf-item-use-taxonomy-20260818.md`。

---

## 批次 1 —— 行为对照

### 1.1 `0x0013` 拒绝分支：旧 5 处 `return null` 与新具名原因逐条对应，条件一字未改

`Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs:271-341`
（新 `MoveItemSpaceCommandHandler.PlanAsync`）与旧 `MoveItemSpaceHandler.PlanAsync` 逐条比对：

| 旧的拒绝点 | 旧条件 | 新原因 | 新条件 |
|---|---|---|---|
| `return null` #1 | `placement is null \|\| !CanOccupyMainSlot(placement, targetSlot)` | `ItemCannotOccupySlot` | 完全相同 |
| 三元 `: null` | `!(placement.StackLimit > 1 && displaced.Count + moveCount <= placement.StackLimit)` | `MergeExceedsStackLimit` | 完全相同 |
| `return null` #2 | `moveCount != moving.Count` | `PartialSwapNotAllowed` | 完全相同 |
| `return null` #3 | `displacedPlacement is null \|\| !CanOccupyMainSlot(displacedPlacement, moving.SlotIndex)` | `DisplacedCannotOccupySlot` | 完全相同 |
| `ApplyMoveAsync` 返回 false | 同 | `SlotsChanged` | 完全相同 |

`moveCount` 的推导（`requestedCount <= 0 || requestedCount > moving.Count ? moving.Count : requestedCount`）、
`Relocate/Split/Merge/Swap` 的选择、反向请求的 `(moving, displaced, targetSlot)` 三元组解构，
均逐字未变。判定顺序也未变：session → list → 同槽 → container → 读槽 → 两槽皆空 → Plan → ApplyMove。
**无条件被改宽或改窄。**

### 1.2 成功应答的字节、长度、字段顺序、时序未变

- `MoveItemSpacePayloadCodec.Encode`（`MoveItemSpaceSlice.cs:110-126`）与
  `UseStackablePayloadCodec.Encode`（`UseStackableSlice.cs:96-131`）**逐字未改**：
  `0x0013` 成功 11 / 失败 4，`0x002C` 成功 12 / 失败 11。
- `Unchanged` 路径：新 `MoveItemSpaceCommandHandler` 返回 `new ItemMoveResult(Unchanged, 0, 0)`，
  handler 以 `Success(message, result.MoveValue=0, message.DestinationSlotIndex)` 应答
  —— 与旧 `Success(message, 0, message.DestinationSlotIndex)` 完全一致。
- `Moved` 路径：`MoveValue = planned.Plan.MoveCount`，`DestinationSlotIndex = message.DestinationSlotIndex`
  （反向请求下 `plan.TargetSlotIndex` 是 `SourceSlotIndex`，但回帧仍回请求自己的 destination，
  与旧一致）。
- 时序：两个 handler 的 `HandleMessageAsync` 仍是「算出一个 response → 一次
  `SendExplicitAsync([response], [])`」，每请求恰好一帧。命令总线（`CommandBus.cs`）不吞异常、
  不额外发帧，`CommandExecutionKind.Direct` 在 `CommandExecutionPolicyBehavior` 里直接 `next()`。
- 两份 codec 测试的改动只是常量改名（`InvalidOperation`→`Silent` 同为 `0x02`，
  `InvalidItem`→`NotConsumable` 同为 `23`），期望字节数组一字未改。

### 1.3 `0x002C`「装备 / 归档不认识」的覆盖面：新集合是旧集合的超集，仅一处退化输入例外

旧 `UseStackableHandler` 用三个条件覆盖：
`item.ItemKind == 1`（容器行上记录的 kind）、`placements.GetAsync(id) is null`、
`placement.ItemKind == 1`。

新代码由 `IItemUsabilityDomainService` 的 `NotAUsableItem` 覆盖，其判据是
`PvfItemUseRuleDefinitionSource.GetAsync` 返回 null，即 **id 不在 `stackable/stackable.lst` 里**
（`PvfItemUseRuleDefinitionSource.cs:50-58`）。

核对旧判据的实际取值域（`PvfEntryLifecycleContentSource.ResolveItemMetadataFor`
`:109-139` 与 `ResolveStackableItemKind` `:141-159`、`ResolveSlotRange` `:161-174`）：

- id ∈ stackable.lst ⇒ placement 必非 null（`ResolveSlotRange` 对任何 stackable kind 都给出
  非空区间），且 `ItemKind` 只可能是 2/3/4/7/9/10/11，**永远不是 1**。
- id ∈ equipment.lst ⇒ kind = 1 ⇒ 旧拒。新代码：不在 stackable.lst ⇒ 也拒。
- id 两处皆无 ⇒ placement null ⇒ 旧拒。新代码：也拒。

所以「新拒集合 ⊇ 旧拒集合」，新增的是不可读规则（RQ-003 要求的）。
**无「旧拒新放」的真实输入**，唯一的理论例外见 1.4。

### 1.4（non-blocking）容器行自带的 `ItemKind == 1` 检查被移除

位置：`Server/src/Dfo.Server.Protocol/Game/UseStackable/UseStackableSlice.cs:239-241`
（新 `UseStackableCommandHandler`，条件只剩 `item is null || command.ItemCode > 0 && item.ItemId != command.ItemCode`），
对照旧版同处的 `|| item.ItemKind == EquipmentItemKind`。

缺陷：若存储里存在一条「id 在 stackable.lst 内、但行上 `ItemKind` 被写成 1」的记录，
旧代码拒绝，新代码会走到判定并可能放行（继而按 stackable 规则消耗）。
后果：仅在容器行的 kind 与归档推导不一致时出现；由于所有写入路径都用
`ResolveStackableItemKind` 推导，而该函数对 stackable id 从不返回 1，正常运行下不可达。
**non-blocking**（数据损坏才触发，且触发后果是「消耗一个本不该消耗的堆叠」而非破坏装备）。

### 1.5（non-blocking）`SlotDisagrees` 由 `23` 改为静默，属未被 RQ-010 明写的语义变更

位置：`Server/src/Dfo.Server.Protocol/Game/UseStackable/UseStackableSlice.cs:325-334`
（`ErrorCodeFor` 无 `SlotDisagrees` 分支，落到 `_ => Silent`）。

旧行为：槽为空、或槽里不是客户端指名的那个 id ⇒ 回 `23`「非消耗品，无法使用」。
新行为：静默（`2`）。

RQ-010 只列了「槽中不是可使用的道具→`23`」和「服务端故障或读写间竞态→静默码」，
`SlotDisagrees` 落在两者的措辞缝隙里。从 `cmd-reason-messages` 对 `0x0013` 的同类指导
（「两个槽都是空的 → 静默：这是客户端与服务端认知不一致，不是玩家能理解的事」）看，
静默是自洽的选择；但这是相对基线的一次可观察行为变更，
应在 EV-006 的「因本成果而改语义」清单里逐条留痕，而不是隐含在重构里。
**non-blocking**（选择本身有据，缺的是记录）。

### 1.6 其余对照

- `NoSession` / `NoBoundContainer` 由 `0x04`/`23` 改为静默 `2`：RQ-011、RQ-010 明确要求，符合。
- 旧的 `MoveItemSpaceResponse.InvalidOperation` / `Rejected` / `UseStackableResponse.InvalidItem`
  三个常量已全部删除，全仓无残留引用（grep 已确认）。

**批次 1 结论：无 blocking 发现。**两条 non-blocking（1.4、1.5）。

---

## 批次 2 —— 选码

### 2.1 `0x002C` `UseStackableHandler.ErrorCodeFor`（`UseStackableSlice.cs:349-358`）

| 分支 | 码 | 客户端表（0x002C，探针） | 是否显示 | 文案与原因是否相符 |
|---|---:|---|---|---|
| `NotAUsableItem` | 23 | `0x31E`「非消耗品， 无法使用」聊天行 | 是 | 相符（RQ-010 明写） |
| `LevelTooLow` | 14 | `0x31C`「Lv过低， 无法使用该物品」聊天行 | 是 | 相符 |
| `JobMismatch` | 8 | `0x31B`「该物品与当前职业不符， 无法使用」聊天行 | 是 | 相符 |
| `OnCooldown` | 19 | `0x31D`「目前无法使用物品」聊天行 | 是 | 相符（探针指导 §2 明确合并冷却与场所到 19） |
| `PlaceNotAllowed` | 19 | 同上 | 是 | 相符 |
| `ListNotServed` | 23 | 同 `NotAUsableItem` | 是 | 近似，见 2.3 |
| `_`（`NoSession`/`SlotDisagrees`/`NoBoundContainer`/`SlotChanged`） | 2 | 表外 | 否 | 静默，见 2.4 |

未使用 `7`（控偶师 APC）、`17`（0x002C 上无文案）、`95`（疲劳，本服无模型）、
`250`/`253`（和平的呐喊）——与 Non-Goals 一致。

### 2.2 `0x0013` `MoveItemSpaceHandler.ErrorCodeFor`（`MoveItemSpaceSlice.cs:422-430`）

| 分支 | 码 | 客户端表（0x0013，探针） | 是否显示 | 相符性 |
|---|---:|---|---|---|
| `ListNotServed` | 23 | `0x89BD`「无法移动该物品」系统提示 | 是 | 相符（探针「按拒绝原因选码」表第 1 行明确建议 23） |
| `ItemCannotOccupySlot` | 23 | 同上 | 是 | 相符（探针表第 2 行） |
| `DisplacedCannotOccupySlot` | 23 | 同上 | 是 | 相符（探针表第 4 行） |
| `MergeExceedsStackLimit` | 17 | `0x89BE`「物品栏已有同类物品」系统提示 | 是 | 相符（探针表第 3 行，RQ-011 明写 17） |
| `PartialSwapNotAllowed` | 23 | 同 23 | 是 | 近似，见 2.3 |
| `_`（`NoSession`/`NoBoundContainer`/`BothSlotsEmpty`/`SlotsChanged`） | 2 | 表外 | 否 | 静默，符合 RQ-011 |

`4` 在整份 slice 里已无任何引用（`MoveItemSpaceSlice.cs:57-72` 的 XML 注释也明写它被刻意留空），
满足 RQ-011「`4` 只用于目标容器确无空位」——本服到不了该条件，故永不回 `4`。

### 2.3（non-blocking）两处「近似而非精确」的文案匹配

- `ItemUseStatus.ListNotServed → 23`（`UseStackableSlice.cs:351`）：
  原因是「请求指名了本服不服务的 list」，客户端说的是「非消耗品，无法使用」。
  这不是同一件事；但基线行为相同（旧代码 `session is null || ListType != Main → 23`），
  且真实客户端的使用请求只发 list 0，触发面为零。RQ-010 未列此分支。
- `ItemMoveStatus.PartialSwapNotAllowed → 23`（`MoveItemSpaceSlice.cs:426`）：
  原因是「交换时只给了部分堆叠」，客户端 CN 说「无法移动该物品」（成立），
  JP 说「这是不可移动类型的物品」（把「这次移动不允许」说成「这类物品不可移动」）。
  RQ-011 与探针建议表都未列此分支；它与已列的两条 `23` 同属一类，选 23 有据。
  注意这相对基线是从静默（旧 `0x02`）变成了会弹提示——与 RQ-011 要求的另两条 `23` 同因。

两条都属「可接受的近似」，但同样应进 EV-006 的语义变更清单。**non-blocking。**

### 2.4 静默码 `2` 的三表核对（EV-005 要求的书面核对）

- `0x002C` 分支链（探针「`0x002C` 的失败码表」）：`7, 8, 14, 17, 19, 23, 95, 250, 253` —— **不含 2**。
- `0x0013` 分支链（探针「`0x0013` 的失败码表」）：`1, 4, 7, 8, 14, 17, 18, 19, 23, 94, 104, 114, 175, 180, 235`
  —— **不含 2**，且该表末行显式写着「`2` / 表外 — 无 — 静默」。
- 共用兜底表 `0x01B897C0`：`77, 105, 114, 115, 122, 123, 134, 136, 137, 208, 210, 214, 228, 229, 230, 237, 239`
  —— **不含 2**。

同时核对本变更实际会发出的每个非静默码都不落在兜底表里：
`8, 14, 17, 19, 23` 均不在上述 17 个值中。**成立。**

**批次 2 结论：无 blocking 发现。**一条 non-blocking（2.3，含两处）。

---

## 批次 3 —— 领域层与归档读取

本批次用到的归档核对（`Tool/PvfProbe`，`Script.pvf`）：

```
values stackable/stackable.lst "[minimum level]"      -> 34,532 文件，首 token 全为非负整数，另 5,862 个 (absent)
values stackable/stackable.lst "[cool time]"          -> 首 token 全为非负整数，其余 (absent)
values stackable/stackable.lst "[cooltime group]"     -> 首 token 全为非负整数，其余 (absent)
values stackable/stackable.lst "[usable job]"         -> 0 个 (absent)；[all] 33,879，
                                                         [creator mage] 21 / [prist] 5 / [account] 1 / [pall] 1
values stackable/stackable.lst "[action usable place]"-> (absent) 33,740；[village] 670 /
                                                         [seria room] 76 / [dungeon] 43 / [tournament] 2 / [dont open] 1
lst stackable/stackable.lst 8474 80                   -> [usable job] / [all] / [/usable job]；
                                                         [stackable type] [waste] 4；[cool time] 10000；[cooltime group] 0
```

`ResolveString` 返回的是不带反引号的 `[foo]` 形式，与 `PlaceTokens` / `ArchiveJobFamilyTokens`
的字典键一致——这是全部 token 匹配成立的前提，已由上面的 probe 输出直接证实。

### 3.1 `ItemUsabilityDomainService` 只读 —— 成立

`Server/src/Dfo.Server.Application/Inventory/ItemUsabilityDomainService.cs:29-64`。
五个依赖全部只读：`rules.GetAsync`（只写自身缓存字典）、`cooldowns.IsCoolingDown`、
`selectedSessions.FindByCharacter`、`runtimeSessions.TryGet`、`presence.Find`。
不触存储、不写冷却、不改会话。RQ-002 的「调用方不组装事实」也成立：
`CanUseAsync(characterId, itemId, ct)` 就是全部签名。**符合 (a)。**

### 3.2（blocking）`FindCharacter` 为 null 时答 `NotAUsableItem`，最终发出会显示的 `23`，不诚实

位置：`Server/src/Dfo.Server.Application/Inventory/ItemUsabilityDomainService.cs:50-52`
（`var character = FindCharacter(characterId); if (character is null) return ItemUsability.NotAUsableItem;`），
翻译处 `Server/src/Dfo.Server.Protocol/Game/UseStackable/UseStackableSlice.cs:352`
（`NotAUsableItem => NotConsumable`，即 `23`）。

缺陷：`FindCharacter` 返回 null 的真实含义是「服务端此刻取不到该角色的运行时状态」，
不是「归档没有这个可用道具」。它被折进 `NotAUsableItem`，于是客户端弹出
「非消耗品， 无法使用」——一句把服务端自身的缺口说成玩家拿错了东西的话。

具体输入：`FindCharacter` 走 `selectedSessions.FindByCharacter(characterId)` →
`runtimeSessions.TryGet(session, ...)`。两处目录的填充时机并不同步：
`Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterPhaseCommands.cs:93`
的 `selectedSessions.RegisterReplacing(session)` 早于 `:215-216` 的
`sessionEntry.Publish(...)`（内部才做 `runtimes.Publish`），而容器绑定发生在中间的
`:118` `inventoryEntry.PrepareLeaseAsync`。因此存在一个窗口：
`FindByConnection` 已能返回会话、容器已绑定（`NoBoundContainer` 不触发）、
但 `runtimeSessions.TryGet` 仍为 false。此窗口内到达的一帧 `0x002C` 会让玩家看到
「非消耗品， 无法使用」。断开清理时存在同样的反向窗口。

后果：直接违反 RQ-009（「或是客户端会显示且其文案与该原因相符的一个，或是静默码」）
与 Constraints「拒绝原因不得借用文案与之不符的客户端提示码」，
也违反 Spec「Why and Outcome」里「没有相符文案时，不显示任何提示，因为一句假话比沉默更坏」
这一条本成果的立身之本。这不是「窗口窄所以可以」的问题——本 Spec 的全部价值就是
不再用一句现成的错话兜服务端自己的缺口，而这里恰好又做了一次。
对照之下，同一条命令里另外三个同类情形（`NoSession`、`NoBoundContainer`、`SlotChanged`）
都被正确地映到了静默码，唯独这一个没有。

最小修法（供参考，不属评审义务）：`ItemUsability` 加一个不面向玩家的成员
（如 `CharacterUnavailable`），在 `UseStackableCommandHandler.StatusFor` 映到
一个落进 `_ => Silent` 的 `ItemUseStatus`。改动限于三个文件，不触协议帧。

**blocking。**

### 3.3（non-blocking）判定顺序：冷却先于取角色、等级先于职业

位置：`ItemUsabilityDomainService.cs:44-63`。

- **冷却先于取角色**：不构成缺陷。冷却记录只可能由一次成功使用写入，
  而那次使用必然已取到过角色。
- **冷却先于等级/职业**：存在一种「更该报告的原因被盖住」的输入——
  道具 A（无限制）与道具 B（`[minimum level] 50`）共享同一个非零 `[cooltime group]`。
  10 级角色用了 A，冷却期内问 B，得到 `OnCooldown` 即「目前无法使用物品」，
  而真实且永久的原因是等级不足（`14`「Lv过低」）。玩家被告知一个会过期的原因，
  于是等冷却结束再试一次，仍然不行。
- **等级先于职业**：两者都是永久原因，先报哪个都不误导，无缺陷。

后果限于一句可过期的提示替换了一句永久的提示，数量与状态均正确。**non-blocking。**

### 3.4（non-blocking）`PlaceOf` 在 presence 为空时默认 Village —— 当前恒定正确，但注释与事实不符

位置：`ItemUsabilityDomainService.cs:75-86`。

`presence.Find(characterId) is { StandingInTown: false } ? Dungeon : Village`。
全仓唯一的 `presence.Admit` 调用点是
`Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs:213-215`，
写死 `StandingInTown: true`。因此本服里 `PlaceOf` 恒为 `Village`：
`[village]` 的 670 个道具放行、`[dungeon]` 43 个与 `[seria room]` 76 个恒被拒绝
——与 Spec Assumptions「副本尚未实现，场所判定当前只能在城镇一侧取值」完全一致。

何时会错：副本实装后，若 presence 未被及时更新（记录仍是 `StandingInTown: true`
或记录缺失），站在副本里的角色会被判为 Village，于是**只声明 `[village]` 的道具在副本里被放行**
——这是放宽方向的错，比拒绝方向更难被发现。

另：该方法的 XML 注释称「The presence registry ... usually answers empty」，
与上述调用点不符（进城时确实会 Admit）。判定结果不受影响，但这句注释会误导后续改动。
**non-blocking。**

### 3.5 `ItemUseEffectDomainService`：(c) 真的做到了

位置：`Server/src/Dfo.Server.Application/Inventory/ItemUseEffectDomainService.cs:22-46`
（`PlanAsync`）与 `:48-83`（`ApplyAsync`）。

- `PlanAsync` 里「不消耗」= `if (rule.IsConsumedOnUse) effects.Add(new ConsumeFromContainerEffect(...))`
  —— 该 if 决定的是**是否把这一项放进集合**，不是消耗时是否跳过。
- `ApplyAsync` 里没有任何关于 `IsConsumedOnUse` 的判断，只有
  `foreach (var consumption in effects.OfType<ConsumeFromContainerEffect>())`。
- 判据检验（「以后再加一个效果，不消耗类道具会不会又要特判」）：
  新效果只需在 `PlanAsync` 里按自己的条件 `Add`，在 `ApplyAsync` 里加一段自己的
  `OfType<T>()` 循环；不消耗类道具在这两处都不需要出现。**(c) 成立。**
- (b) 也成立：`IItemUseEffect` 的两个实现只改容器与冷却，`ItemEffectTarget` 自述目标，
  应答帧的构造留在 `UseStackableHandler`，不在效果里。

### 3.6 容器拒绝时冷却确实没写入

`ItemUseEffectDomainService.cs:62-72`：消耗循环里 `if (applied is null) return null;` 在
冷却循环（`:74-75`）之前，且两个循环之间无别的写入。容器一旦拒绝，`cooldowns.Start` 不被调用。
**成立。**

### 3.7（non-blocking）`remaining` 在「多条消耗」下会报错对象

位置：`ItemUseEffectDomainService.cs:60-79`。

`var remaining = item.Count;` 起始，每条 `ConsumeFromContainerEffect` 用
`applied.RemainingCount` 覆盖，最后返回 `new ItemUseApplication(item.ItemId, remaining)`。

- 「无消耗」：`remaining = item.Count = N`，正确（AC-007）。
- 「单条消耗」：`remaining = N-1`，正确（AC-008）。
- 「多条消耗」：若两条消耗指向**不同的** `StorageEntryId`，返回的 `remaining` 是最后一条那个
  条目的剩余数，却与 `item.ItemId` 配成一对——两个字段来自不同的槽。
  当前 `PlanAsync` 最多产生一条消耗，故不可达；且 `0x002C` 的成功应答不带剩余数量，
  该值只进日志。**non-blocking**（未来加效果时的陷阱，不是今天的错）。

### 3.8 `CharacterItemCooldownDomainService`：三个疑点均不成立

位置：`Server/src/Dfo.Server.Application/Inventory/CharacterItemCooldownDomainService.cs`。

- **`Clear` 遍历 Keys 同时 TryRemove 是否漏删**：不漏。
  `ConcurrentDictionary<TKey,TValue>.Keys` 属性返回的是**快照**
  （取全部锁后拷进新 `List<TKey>`，包成 `ReadOnlyCollection`），不是活动枚举器，
  因此边遍历边 `TryRemove` 既不会抛也不会跳项。快照之后新增的条目不会被删，
  但那属于「清理之后又用了一次」，本就不该删。
- **到期条目不清扫是否无界增长**：不是。
  `Clear` 由 `Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs:130-136`
  在会话清理时调用，与 `containers.Clear` 共用同一个守卫（`sessions.FindByCharacter(...) is null`）。
  会话内的上界是该角色触碰过的不同冷却键数。
- **复合键是否真避免组号与道具 id 碰撞**：避免了。
  `ItemCooldownKey(bool IsShared, int Value)` 是 record struct，两个字段都进相等性；
  `ForGroup(70)` = `(true, 70)`，`ForItem(70)` = `(false, 70)`，不相等。
  字典键 `(int CharacterId, ItemCooldownKey Key)` 再叠一层角色维度。**符合 RQ-006。**

### 3.9 `PvfItemUseRuleDefinitionSource`：`Reset()` 与标签乱序

位置：`Server/src/Dfo.Server.Infrastructure/Content/PvfItemUseRuleDefinitionSource.cs:76-91`
（`Read` 依次读六个标签）、`:120-127` `FirstInt32`、`:129-141` `FirstString`、
`:151-166` `AllStrings`。

三个读取函数**各自第一行都是 `reader.Reset()`**，六次读取因此互不依赖顺序。
probe 出的 8474 实际顺序（`[usable job]` 在 `[stackable type]` 之前、
`[minimum level]` 在 `[stack limit]` 之前）证实了「顺序不固定」是真的，也证实了处理是对的。

`TryReadSectionValue` 遇 Identifier 即停（`Server/src/Dfo.Pvf/Tokens/PvfTokens.cs:135-145`），
配合 `[usable job]` / `[all]` / `[/usable job]` 的实际布局：开标签是 Identifier（被 `TryMovePast` 吃掉），
`[all]` 是 QuotedString（收进列表），闭标签 `[/usable job]` 是另一个 Identifier（终止）。
**成立。**

### 3.10 「读不出=拒绝」对 job/place 成立

- `[usable job]`：`ProjectJobs`（`:96-109`）对 `null` 答 `Any`（未声明），
  对 `["[all]"]` 答 `Any`，其余逐 token 走 `ArchiveJobFamilyTokens.Resolve`；
  不认识的 token 变 `Unrecognized`，`ItemJobRestriction.IsValid` 为 false，
  `ItemUseRule.IsReadable` 为 false，判定答 `NotAUsableItem`。
  归档里的 `[creator mage]` 21 / `[prist]` 5 / `[account]` 1 / `[pall]` 1 共 **28 个道具**
  因此被拒——与本次 probe 的计数逐个吻合。
- `[action usable place]`：`ProjectPlaces`（`:112-121`）同理；`[dont open]` 1 个被拒。
- 空声明（tag 在、值为空）：`AllStrings` 返回 `[]`（非 null），
  `IsValid` 因 `Count > 0` 不成立而为 false，判为拒绝。
  这正是实施者自查修掉的那处，方向正确。当前归档不存在空声明
  （`[usable job]` 的 `(absent)` 计数为 0，34,532 个文件的首 token 都有值），故此前影响面确为零。

反方向（把「未声明」误判成「读不出」）：`AllStrings` 只在
`!tag.IsResolved || !reader.TryMovePast(tag)` 时返回 null，两者都确实是「未声明」，
不会把有值的声明读成 null。**成立。**

### 3.11（non-blocking）「读不出=拒绝」对四个非 section 标签不成立，但当前归档不可达

位置：`PvfItemUseRuleDefinitionSource.cs:120-127`（`FirstInt32`）与 `:129-141`（`FirstString`）。

`FirstInt32` 把三种情况折成同一个答案 `0`：标签未声明、标签声明了但本文件没有、
标签声明了但 section 里没有 Int32。第三种是 RQ-003 说的「声明了却读不出可用数值」，
按契约应判为不可使用，实际却退化成「无限制」：
`[cool time]` 变无冷却、`[cooltime group]` 变用道具自身 id 作键、`[minimum level]` 变 0 级可用。
外层的 `Math.Max(0, ...)` 又把负值一并抹成 0。
`FirstString` 同理：`[stackable type]` 读不出值时返回 null，
`IsConsumedOnUse` 因 `stackableType is null || !StartsWith("[unlimited")` 判为 **true**（会消耗），
即「读不出」被当成「消耗」而不是拒绝——恰是本 Spec 要消灭的那类销毁。

具体输入下的后果：一个声明了 `[minimum level]` 却没写数值的 `.stk` 会对所有等级放行。

可达性：本次 probe 已核对——`[minimum level]` / `[cool time]` / `[cooltime group]`
在全部 34,532 个文件里，标签后的首 token **要么是非负整数、要么标签不存在**，
没有任何非整数或负数取值；`[stackable type]` 按归档探针每个文件都有值。
因此本条在当前归档上**不可达**。**non-blocking**（契约与实现的口径差，不是今天的错误行为）。

### 3.12 两处共享抽取搬迁前后语义一致

- `ArchiveJobFamilyTokens`（`Server/src/Dfo.Server.Infrastructure/Content/ArchiveJobFamilyTokens.cs`）
  从 `PvfQuestDefinitionSource.JobTokens` 抽出：16 个键值逐条对照，
  键、值、插入顺序、`StringComparer.OrdinalIgnoreCase` 全部一致。
  - `JobTokens.TryGetValue(token, out var requirement)` 改为 `ArchiveJobFamilyTokens.TryResolve(...)`
    （`Server/src/Dfo.Server.Infrastructure/Content/PvfQuestDefinitionSource.cs:554`）：
    **失败仍在调用点 `throw Invalid(questId, ...)`**，抛出行为一字未改；
    新增的 `Resolve`（返回 `Unrecognized`）是另一个方法，只给道具路径用。
    两个调用方对「不认识」的处理差异是刻意的，且各自保持了原样。
  - `ProjectTargetCharacterRestriction` 里的整表子串扫描（`PvfQuestDefinitionSource.cs:503-507`）：
    `JobTokens.Where(...)` 改为 `ArchiveJobFamilyTokens.All.Where(...)`。
    `All` 返回的就是同一个 `Dictionary` 实例，无删除操作，故枚举顺序等于插入顺序等于旧版顺序；
    `.Where(...).Select(...).Distinct().ToArray()` 之后的元素与次序完全相同。**一致。**
- `CharacterJobFamilies.Resolve`（`Server/src/Dfo.Server.Application/Characters/CharacterJobFamilies.cs`）
  从 `WorldContentEntryRuleService.QuestJobRequirementFor` 抽出：13 个 case 逐条对照，
  含 `8 or 10`、`9 or 11` 的合并与 `_ => Unrecognized`，**一字未改**；
  调用点由 `QuestJobRequirementFor(subject.Job)` 改为 `CharacterJobFamilies.Resolve(subject.Job)`。**一致。**

**批次 3 结论：1 条 blocking（3.2），4 条 non-blocking（3.3、3.4、3.7、3.11）。**

---

## 批次 4 —— 测试质量

### 4.1 `ItemRefusalCodeTests` 的码表不是「自己抄自己」

位置：`Server/tests/Dfo.Server.Protocol.Tests/Game/ItemRefusalCodeTests.cs:20-42`。

四张数组全部是 `cmd-reason-messages-20260818.md` 的**人工转写**，没有一项由被测代码产生，
因此不构成恒真。逐条对照探针文档：

| 测试数组 | 探针来源 | 是否一致 |
|---|---|---|
| `UseCodesTheClientMatches` `[7,8,14,17,19,23,95,250,253]` | 0x002C 失败码表 9 行 | 一致 |
| `UseCodesTheClientDraws` `[7,8,14,19,23,95,250,253]` | 同上去掉 17（该表标注「无」） | 一致 |
| `MoveCodesTheClientMatches` `[1,4,7,8,14,17,18,19,23,94,104,114,175,180,235]` | 0x0013 失败码表 15 行 | 一致 |
| `MoveCodesTheClientDraws` 去掉 18、180 | 18 只写日志；180 另开 UI 且内容未读到 | 一致且偏保守 |
| `SharedFallbackCodes` 17 个值 | 兜底表 `0x01B897C0` | 一致 |

`The_use_refusals_the_client_can_name_stay_apart` 与
`The_cooldown_and_the_place_share_the_one_sentence_that_fits_them` 用的是**字面量** `8/14/17/19/23`
而非生产常量，因此改动 `UseStackableResponse.JobMismatch` 的取值会让它们变红。
`The_servers_own_problems_say_nothing` 两侧都用 `…Response.Silent`，单看是恒真，
但 `The_silent_use_code_is_in_none_of_the_clients_tables` /
`The_silent_move_code_is_in_none_of_the_clients_tables` 独立地把该常量与三张转写表比对
（把 `Silent` 改成 `4` 或 `18` 都会让其中之一变红），两者合起来不留空档。**无发现。**

### 4.2（non-blocking）遍历枚举的两条断言在「具名拒绝退化为静默」时仍会通过

位置：`ItemRefusalCodeTests.cs:66-92`
（`Every_use_refusal_is_answered_with_a_code` / `Every_move_refusal_is_answered_with_a_code`）。

断言形如 `code == Silent || CodesTheClientDraws.Contains(code)`。
由于 `ErrorCodeFor` 是带 `_ => Silent` 兜底的全覆盖 switch，**任何**具名分支被删掉后
该原因都会落到 `Silent`，断言仍然成立。

具体输入下的后果：把 `UseStackableSlice.cs:355` 的
`ItemUseStatus.OnCooldown => CurrentlyUnusable` 删掉，冷却中的道具从此静默——
玩家连点无反应且看不到任何提示，而这两条用例仍绿。
（该分支另有 `The_cooldown_and_the_place_share_...` 兜住，所以实际不会漏；
但对**没有被单独钉住**的原因就会漏，见 4.3。）

这两条用例真正能抓的是「选了一个既不显示又不静默的码」——
例如把 `OnCooldown` 映到 `18` 或映到兜底表里的 `122`，都会让它们变红。
它们不是恒真，但比方法名承诺的弱得多；方法名与 XML 注释说的是「每个拒绝都会被应答」，
而用例其实一帧都没发，「有应答帧」是由 handler 用例的 `sender.Sent.Single()` 与
codec 用例的字节断言分别承担的。**non-blocking**（命名与注释的承诺超出了断言范围）。

### 4.3（non-blocking）RQ-011 有一条原因→码没有任何用例钉住

RQ-011 列了七条映射。逐条查覆盖：

| RQ-011 条目 | 钉住它的用例 |
|---|---|
| 列表不服务→23 | `MoveItemSpaceHandlerTests.A_list_other_than_the_main_one_is_refused` |
| 物品不能占用目标槽→23 | `A_slot_outside_the_items_band_is_refused` + `ItemRefusalCodeTests` |
| **交换时被顶出的物品不能占用腾出的槽→23** | **无** |
| 合并会超出堆叠上限→17 | `A_merge_past_the_stack_limit_says_the_pile_is_full` + `ItemRefusalCodeTests` |
| 两槽皆空→静默 | `Two_empty_slots_are_refused_without_a_message` + `ItemRefusalCodeTests` |
| 读写间槽位改变→静默 | `ItemRefusalCodeTests.The_servers_own_problems_say_nothing` |
| 无会话或无绑定容器→静默 | 同上 |

`ItemMoveStatus.DisplacedCannotOccupySlot`（以及未进 RQ-011 的
`PartialSwapNotAllowed`）只被 4.2 那条弱断言覆盖。把
`MoveItemSpaceSlice.cs:427` 的 `DisplacedCannotOccupySlot => NotMovable` 删掉，
全套 834 个用例仍绿，而「交换时被顶出的物品放不下」从此对玩家静默。
EV-004 要求「两条协议各拒绝路径所回之码的自动化测试」，这一条缺席。**non-blocking。**

### 4.4（non-blocking）`FixedRuntimeSessions` 的六个 `null!` 无害，但它恒答 true，让一条真实分支从未跑到

位置：`Server/tests/Dfo.Server.Application.Tests/Inventory/ItemUseFixtures.cs:76-118`。

六个 `null!` 本身不遮蔽路径：`ItemUsabilityDomainService` 只读
`SelectCharacterResult.Character`，其余八个成员一旦被读会直接 NRE 而不是悄悄给错值
——这正是注释声明的意图，成立。

真正的空档在 `TryGet`（`:105-113`）：它**无条件**返回 `true` 且 `value = result`。
配合 `ItemUsabilityDomainServiceTests.OneSession.FindByCharacter` 也恒返回一个会话，
`ItemUsabilityDomainService.FindCharacter` 的 null 分支
（`ItemUsabilityDomainService.cs:50-52`）在全套用例里**一次都没有被执行过**。
那正是 3.2 那条 blocking 缺陷所在的分支。**non-blocking**（测试空档；缺陷本身已在 3.2 计为 blocking）。

### 4.5（non-blocking）`CommandRegistrationTests` 抓得住「漏注册依赖」，抓不住「漏注册命令」

位置：`Server/tests/Dfo.Server.Protocol.Tests/Game/CommandRegistrationTests.cs:23-35`。

- **依赖漏注册**：`scope.ServiceProvider.GetService(registration.InvokerType)` 在激活
  handler 时会因无法解析构造参数抛 `InvalidOperationException`，用例变红。有效。
- **命令本身漏注册**：若把 `ProtocolModule.cs:137-140` 的
  `AddCommandHandler<UseStackableCommand, ItemUseResult, UseStackableCommandHandler>(...)` 删掉，
  `provider.GetServices<CommandHandlerRegistration>()` 里就没有这一项，
  循环体不会为它执行，`Assert.NotEmpty` 仍因其余命令而通过。
  而 `UseStackableHandlerTests` / `MoveItemSpaceHandlerTests` 用的是
  `ItemUseFixtures.DirectCommandBus`（`ItemUseFixtures.cs:120-146`）这个假总线，
  不经过生产注册表。**全套用例仍绿，而真机第一次按键会在
  `CommandBus.SendAsync` → `registry.GetRegistration(...)` 处抛。**

后果：本次新增的两条命令注册没有任何回归保护，正是这个用例声称要关闭的那类缺口的另一半。
最小补法：在该用例里追加对两个命令类型的显式断言
（`Assert.Contains(registrations, r => r.CommandType == typeof(UseStackableCommand))`）。
**non-blocking。**

### 4.6（non-blocking）「离场清空冷却」的接线没有任何用例

位置：`Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs:130-136`
新增的 `cooldowns.Clear(session.CharacterId);`。

- `ItemUsabilityDomainServiceTests.Leaving_clears_the_characters_cooldowns` 直接调用
  `fixture.Cooldowns.Clear(Character)`，测的是领域服务自己的 `Clear`，不是清理服务会调它。
- `SessionGenerationOwnershipTests` 的五处改动只是把
  `new CharacterItemCooldownDomainService(TimeProvider.System)` 塞进构造参数，
  清理之后从不检查它。

后果：删掉 `SelectCharacterLifecycleServices.cs:134` 那一行，全套用例仍绿；
`ICharacterItemCooldownDomainService` 的 XML 注释承诺的「a relog clears every cooldown」
不再成立，冷却会跨重登存活，条目也不再回收。
（`CharacterItemCooldownDomainService` 的类注释把「character leaves」列为两个回收时机之一，
所以这不是无关紧要的接线。）**non-blocking。**

### 4.7 既有测试的语义变更逐条判断

| 用例 | 变更 | 是否由本成果要求 |
|---|---|---|
| `MoveItemSpaceHandlerTests.A_slot_outside_the_items_band_is_refused` | `InvalidOperation`(2) → `NotMovable`(23) | 是，RQ-011「物品不能占用目标槽→23」 |
| `…A_list_other_than_the_main_one_is_refused` | `InvalidOperation`(2) → `NotMovable`(23) | 是，RQ-011「列表不服务→23」 |
| `…Two_empty_slots_are_refused` → `…_without_a_message` | `Rejected`(4) → `Silent`(2) | 是，RQ-011 与 AC-015 明写 |
| `UseStackableHandlerTests.A_list_other_than_the_main_one_is_refused` | `InvalidItem` → `NotConsumable` | 纯常量改名，取值仍为 23 |
| `…Equipment_is_refused` → `…_as_not_consumable`，item id `1000` → `10000` | 期望码仍为 23；`KindFromInventory` 与 `FakeInventory.ItemKind` 一并删除 | 是。拒绝机制由「行 kind==1 / placement kind==1」改成「id 不在 stackable 索引里」，`10000` 是归档里最低的装备 id，与 `PvfItemUseRuleDefinitionSourceTests.Real_archive_answers_nothing_for_an_item_it_does_not_list_as_stackable` 用的是同一个 id。不是为了变绿改的期望值——期望值一字未改，改的是输入使之贴近真实归档 |
| `…A_slot_holding_a_different_item_is_refused` / `An_empty_slot_is_refused` → `…_without_a_message` | `InvalidItem`(23) → `Silent`(2) | **不是 RQ-010 明写的**。RQ-010 只说「槽中不是可使用的道具→23」与「服务端故障或读写间竞态→静默」，`SlotDisagrees` 落在措辞缝隙里。选择本身与探针对 0x0013 同类情形的指导自洽（见 1.5），但这是本成果**自行扩大**的语义变更，必须进 EV-006 清单 |
| `CharacterContainerDomainServiceTests.The_item_protocols_reach_storage_only_through_a_container` → `The_item_commands_…`，反射目标由 handler 改为 command handler | 容器确实下沉到 command handler，旧断言必然变红 | 是。且同时**新增**了 `The_item_protocol_handlers_decide_nothing_about_items`，把「handler 既不拿存储也不拿容器、且必须拿 `ICommandBus`」钉住——净覆盖是增强而非削弱，不是为变绿而改 |
| `SessionGenerationOwnershipTests` 五处 | 构造参数补一个冷却服务 | 是，编译所需；但未补断言（见 4.6） |
| 两份 codec 测试 | 常量改名，期望字节数组未动 | 是，纯改名 |

### 4.8 新增用例中确实断言了真东西的部分（抽查）

- `An_unlimited_item_is_used_without_leaving_the_pile`：断言 `inventory.Count == 1` 且 `ErrorCode` 为 null
  ——数量与成功状态两头都钉住，是 RQ-005/AC-007 的直接证据。
- `A_refused_use_does_not_start_the_cooldown`：两次调用都断言 `LevelTooLow`。
  这条不是摆设——判定里冷却检查排在等级之前（`ItemUsabilityDomainService.cs:47-55`），
  所以一旦判定阶段写了冷却，第二次的答案会变成 `CurrentlyUnusable`(19)，用例立刻变红。
- `A_merge_past_the_stack_limit_says_the_pile_is_full` / `A_merge_within_the_stack_limit_still_happens`：
  成对出现，一条钉拒绝、一条钉「拒绝不是对所有同类堆叠都成立」，
  并同时断言了 `inventory.Rows()`，不是只看错误码。
- `PvfItemUseRuleDefinitionSourceTests` 的期望值**可在归档里独立核对**，本次已逐条核对：

  | 用例 | 期望 | PvfProbe 实测 |
  |---|---|---|
  | `Real_archive_reads_the_tutorial_potion` | 8474：消耗 / 10000 / 组 0 / 等级 1 / `[all]` / 无场所 | `lst … 8474` 全部吻合 |
  | `Real_archive_reads_an_item_that_survives_its_own_use` | 51：不消耗 / 3000 / 组 70 / `[village][dungeon]` | `lst … 51` 读到 `[unlimited waste] 0`、3000、70、`[village] [dungeon]`，含次序 |
  | `Real_archive_reads_a_job_restricted_item` | 4041：`[swordman]` 单家族 | `lst … 4041` 读到 `[usable job] [swordman] [/usable job]` |
  | `A_declaration_naming_something_the_server_does_not_model_is_unreadable` | `[prist]` / `[creator mage]` / `[dont open]` 均不可读 | `values` 实测计数 5 / 21 / 1，与用例注释一致 |
  | `An_exact_all_declaration_allows_every_job` | 33,879 个文件声明 `[all]` | `values` 实测 33,879，逐位吻合 |

**批次 4 结论：无 blocking 发现。**五条 non-blocking（4.2、4.3、4.4、4.5、4.6），
外加 4.7 中 `SlotDisagrees` 一行的语义变更需要进 EV-006 清单（与 1.5 同一件事，不重复计）。

---

## 自动化基线的独立复核

本评审自己跑了一遍全量（`Server/86JP.Server.slnx`，`-m:1`，
`PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` 均指向 `D:/project/DNF/DNFClient-debug/Script.pvf`）：

| 程序集 | 通过 | 跳过 | 失败 |
|---|---:|---:|---:|
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Application.Tests | 363 | 0 | 0 |
| Dfo.Server.Host.Tests | 4 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 163 | 2 | 0 |
| Dfo.Server.Networking.Tests | 46 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 198 | 0 | 0 |
| **合计** | **834** | **2** | **0** |

与实施者申报的 834 / 2 / 0 一致；两条跳过是既有的缺档案用例
（`PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`、
`PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`），
与本成果无关。**EV-006 的「既有测试基线全绿」成立。**

本评审未做变异验证，因此未对任何文件做过临时改动；工作区除本文件外未被触碰。

---

## 结论

**decision: changes_required**

### 阻断项（1 条）

- **3.2** `ItemUsabilityDomainService.cs:50-52` —— 取不到角色运行时状态这一**服务端自身故障**
  被折进 `NotAUsableItem`，最终对玩家弹出 `23`「非消耗品， 无法使用」。
  违反 RQ-009 与 Constraints「拒绝原因不得借用文案与之不符的客户端提示码」，
  也违反本 Spec 的立身之本「一句假话比沉默更坏」。
  同一条命令里另外三个同类情形（`NoSession` / `NoBoundContainer` / `SlotChanged`）
  都已正确映到静默码，唯独这一个没有；且该分支在全套用例里从未被执行过（见 4.4）。

### 非阻断项（11 条）

| 编号 | 位置 | 摘要 |
|---|---|---|
| 1.4 | `UseStackableSlice.cs:239-241` | 容器行自带的 `ItemKind == 1` 检查被移除；仅在数据损坏时可达 |
| 1.5 | `UseStackableSlice.cs:325-334` | `SlotDisagrees` 由 `23` 改为静默，非 RQ-010 明写，需进 EV-006 语义变更清单 |
| 2.3 | `UseStackableSlice.cs:351`、`MoveItemSpaceSlice.cs:426` | `ListNotServed`→23（0x002C）与 `PartialSwapNotAllowed`→23 属「近似而非精确」的文案匹配 |
| 3.3 | `ItemUsabilityDomainService.cs:44-63` | 共享冷却组下，`OnCooldown` 会盖住更该报告的 `LevelTooLow` |
| 3.4 | `ItemUsabilityDomainService.cs:75-86` | `PlaceOf` 默认 Village 当前恒正确，但副本实装后是放宽方向的错；XML 注释与实际 `Admit` 调用点不符 |
| 3.7 | `ItemUseEffectDomainService.cs:60-79` | 多条消耗时 `remaining` 与 `ItemId` 可能来自不同的槽（今日不可达） |
| 3.11 | `PvfItemUseRuleDefinitionSource.cs:120-141` | 「声明了却读不出」对四个非 section 标签退化为「无限制/会消耗」，与 RQ-003 口径不符；已 probe 证实当前归档不可达 |
| 4.2 | `ItemRefusalCodeTests.cs:66-92` | 遍历枚举的断言在「具名拒绝退化为静默」时仍通过 |
| 4.3 | 缺失用例 | RQ-011「交换时被顶出的物品不能占用腾出的槽→23」无任何用例钉住 |
| 4.5 | `CommandRegistrationTests.cs:23-35` | 抓得住漏注册依赖，抓不住漏注册命令本身 |
| 4.6 | 缺失用例 | `SelectCharacterLifecycleServices.cs:134` 的「离场清空冷却」接线无任何用例 |

### 用户三条设计的达成情况

| 设计 | 结论 |
|---|---|
| (a) 判定只读且自行取得所需事实，调用方不组装 | **达成**（3.1）。`CanUseAsync(characterId, itemId, ct)` 即全部签名，五个依赖全部只读 |
| (b) 状态变更全部在效果侧，协议帧不属于效果 | **达成**（3.5）。两个 `IItemUseEffect` 只改容器与冷却，应答帧构造留在 handler |
| (c) 「不消耗」= 效果集合里没有减数量这一项 | **达成**（3.5）。`PlanAsync` 的 if 决定是否入列，`ApplyAsync` 里没有任何 `IsConsumedOnUse` 判断；再加一个效果时不消耗类道具不需要出现在任何一处 |

### 通过的部分

- 批次 1：旧 `PlanAsync` 的每一处拒绝与新具名原因**一一对应，条件一字未改**；
  两条协议成功与失败应答的字节、长度、字段顺序、每请求一帧的时序**逐字未变**；
  `Unchanged` 与 `Moved` 两条路径回的 `MoveValue` / `DestinationSlotIndex` 与基线相同；
  `0x002C` 的「装备 / 归档不认识」覆盖面是旧集合的**超集**，无「旧拒新放」的真实输入。
- 批次 2：两张 `ErrorCodeFor` 的每个分支都能在 `cmd-reason-messages` 里找到对应文案；
  静默码 `2` 已逐表核对，不在 `0x002C` 分支链、`0x0013` 分支链、兜底表任何一张里；
  实际会发出的 `8 / 14 / 17 / 19 / 23` 也都不在兜底表里；`4` 已彻底不再使用。
- 批次 3：冷却的复合键、`Clear` 的快照语义、条目回收上界三个疑点均不成立（无缺陷）；
  归档读取的 `Reset()` 与标签乱序处理正确；
  「读不出=拒绝」对 `[usable job]` / `[action usable place]` 成立，
  28 个不建模职业取值与 1 个不建模场所取值确实被拒（probe 计数逐个吻合）；
  两处共享抽取（`ArchiveJobFamilyTokens` / `CharacterJobFamilies`）搬迁前后语义完全一致，
  含 `throw Invalid(...)` 与整表子串扫描的枚举次序。
- 批次 4：`ItemRefusalCodeTests` 的四张码表是探针文档的人工转写而非被测代码的产物，
  不构成恒真，且转写内容逐条核对无误；`PvfItemUseRuleDefinitionSourceTests`
  的每个期望值都能在归档里独立核对，本次已用 PvfProbe 逐条复核通过；
  既有用例的语义变更除 `SlotDisagrees` 一处外全部由 RQ-010/RQ-011 直接要求，
  接缝反射断言的迁移是净增强而非为变绿而改。

---

# 第 2 轮 —— 实施者的整改回应

本节由实施者写入，记录对第 1 轮发现的处置。**第 2 轮的评审结论仍待独立评审给出**，
本节不是结论。整改后自动化为 **841 通过 / 2 跳过 / 0 失败**（第 1 轮时为 834/2/0）。

## 阻断项

### 3.2 —— 已修

先自行核对了可达性主张：`SelectCharacterPhaseCommands.cs` 里
`selectedSessions.RegisterReplacing`（:93）→ `inventoryEntry.PrepareLeaseAsync`（:118，其中绑定容器）
→ 运行时状态由另一条命令 `PublishSelectedCharacterSessionCommandHandler` 发布，
三者之间确实存在「会话在、容器已绑、运行时未发布」的窗口。发现成立。

`ItemUsability` 新增 `CharacterStateUnavailable`，`ItemUsabilityDomainService` 的 `FindCharacter`
为 null 时返回它；`ItemUseStatus` 新增同名成员，`ErrorCodeFor` 将其映到静默码。
新增用例 `A_character_whose_state_is_not_published_yet_is_not_the_players_fault`，
用 `FixedRuntimeSessions.Unpublished` 走到该分支——这条分支此前从未被执行过（第 1 轮 4.4）。

## 非阻断项

| 编号 | 处置 |
|---|---|
| 1.4 | 不改。新拒集合是旧集合的超集，该退化输入只在容器行 kind 与归档推导不一致时可达，而所有写入路径都用同一个推导函数。记入 spec-result 的已知缺口。 |
| 1.5 / 2.3 | 不改选码，改为留痕。`SlotDisagrees` 静默、`ListNotServed` 与 `PartialSwapNotAllowed` 取 23，三条都进 EV-006 的「因本成果而改语义」清单。 |
| 3.3 | 已修。判定顺序改为「不可读 → 角色状态 → 等级 → 职业 → 场所 → 冷却」，永久原因先于会过期的原因。新增用例 `A_permanent_reason_is_reported_ahead_of_one_that_will_expire`。 |
| 3.4 | 已修注释。原注释称 presence 注册表「不会被填」与事实不符；改为说明默认值是放宽方向、以及它在副本实装那一刀失效。 |
| 3.7 | 已修。`remaining` 只取与 `item.StorageEntryId` 相同的那条消耗，不再把别的堆叠的余量与本道具的 id 配成一对。 |
| 3.11 | 已修，且扩到全部四个标量标签。`ItemUseRule` 增加 `HasUnreadableDeclaration`，`IsReadable` 纳入它；`FirstInt32` / `FirstString` 区分「标签不存在」与「标签在但读不出值」。这与 RQ-003 一致——该要求覆盖全部声明，不只 section 型。新增用例 `A_declared_but_unreadable_scalar_makes_the_whole_rule_unreadable`。 |
| 4.2 | 已修。两个 `ErrorCodeFor` 去掉 `_ => Silent` 兜底，逐个列出全部拒绝原因，其余抛 `ArgumentOutOfRangeException`。于是遍历枚举的两条断言变成真检查：新增一个没决定码的拒绝原因会让它们变红，而不是悄悄变成静默。 |
| 4.3 | 已修。`ItemRefusalCodeTests` 补上 `DisplacedCannotOccupySlot` 与 `PartialSwapNotAllowed` 的显式断言；`MoveItemSpaceHandlerTests` 补一条端到端用例 `A_swap_whose_other_end_cannot_land_says_the_item_will_not_move`，用新的 `TwoBandPlacements` 让交换在被顶出的那一端失败。 |
| 4.4 | 已修。`FixedRuntimeSessions` 增加未发布模式，`TryGet` 不再无条件答 true。 |
| 4.5 | 已修。`CommandRegistrationTests` 新增 `The_item_commands_are_registered`，显式断言两个命令类型在生产注册表里。 |
| 4.6 | 已修。`SessionGenerationOwnershipTests` 新增两条：`Cleanup_ClearsTheCharactersCooldowns`，以及 `Cleanup_LeavesTheCooldownsOfACharacterWhoAlreadyReturned`（角色已在别的连接重新进入时不得清掉它的记录，与容器注册那条同因）。 |

## 整改后的变异验证

第 1 轮明确指出「删掉它全套仍绿」的四处，逐一验证现在会红：

| 变异 | 变红的用例 |
|---|---|
| `FindCharacter` 为 null 折回 `NotAUsableItem` | `A_character_whose_state_is_not_published_yet_is_not_the_players_fault` |
| `DisplacedCannotOccupySlot` 改回静默 | `A_swap_whose_other_end_cannot_land_says_the_item_will_not_move` |
| 离场不再清空冷却 | `Cleanup_ClearsTheCharactersCooldowns` |
| `UseStackableCommand` 不注册 | `The_item_commands_are_registered` |

（附带发现：直接删掉 `cooldowns.Clear(...)` 那一行会让主构造函数的 `cooldowns` 参数变为未读，
在 `TreatWarningsAsErrors` 下直接编译失败；上表是把它换成一次无副作用的读取后测得的。）

变异验证由实施者自己做，只证明这些用例不是恒真；它不替代第 2 轮的独立判断。

---

# 第 2 轮 —— 独立评审结论

评审范围：只复核第 1 轮的处置与本轮新引入的改动，第 1 轮已通过的对照不重跑。
基线仍是 `a0149f5`。本轮用到的证据见各条内的命令与输出。

## R2-1 阻断项 3.2 的整改

### R2-1.1 修法本身正确，`CharacterStateUnavailable` 在每条路径上都被正确映射

逐层核对：

| 层 | 位置 | 映射 |
|---|---|---|
| 判定 | `ItemUsabilityDomainService.cs:41-43` | `FindCharacter` 为 null → `ItemUsability.CharacterStateUnavailable` |
| 命令 | `UseStackableSlice.cs` `StatusFor` | `ItemUsability.CharacterStateUnavailable` → `ItemUseStatus.CharacterStateUnavailable`（**显式列出**，不靠兜底） |
| 协议 | `UseStackableSlice.cs` `ErrorCodeFor` | `ItemUseStatus.CharacterStateUnavailable` → `UseStackableResponse.Silent`（`2`） |

`ItemUsability` 现有 6 个成员，`StatusFor` 显式覆盖 5 个（`Allowed` 在调用点被
`if (verdict is not ItemUsability.Allowed)` 挡住，到不了）。
`ItemUseStatus` 现有 12 个成员，`ErrorCodeFor` 显式覆盖 11 个 + `Used` 抛异常，全覆盖。
`ItemMoveStatus` 现有 11 个成员，`MoveItemSpaceHandler.ErrorCodeFor` 显式覆盖 9 个 +
`Moved`/`Unchanged` 抛异常，全覆盖。**三处都没有落到兜底的可达取值。**

静默码仍是 `2`，三张客户端表的核对结论不变（第 1 轮 §2.4），无需重做。
**3.2 已修复。**

### R2-1.2（non-blocking）同一个兜底隐患仍留在 `StatusFor` 里，且无任何用例守着它

位置：`Server/src/Dfo.Server.Protocol/Game/UseStackable/UseStackableSlice.cs`，
`UseStackableCommandHandler.StatusFor` 的最后一臂 `_ => ItemUseStatus.NotAUsableItem`。

4.2 的整改把两个 `ErrorCodeFor` 的 `_ => Silent` 换成了「逐条列举 + 其余抛异常」，
理由是「兜底会让下一个新增的拒绝原因悄悄变成静默」。这个理由对 `StatusFor` 同样成立，
而且后果更重：新增一个 `ItemUsability` 成员若忘了在 `StatusFor` 里列出，
它不会变成静默，而是变成 `NotAUsableItem` → 客户端码 `23`
→ 玩家看到「非消耗品， 无法使用」——**正是 3.2 那条阻断项的同一种不诚实**，
只是从判定层挪到了命令层。

覆盖情况：`grep -rn "ItemUsability" Server/tests/` 显示没有任何用例遍历
`Enum.GetValues<ItemUsability>()`；`ItemUsabilityDomainServiceTests` 逐条断言的是
具体输入下的具体答案，不会因为新增成员而变红。
所以 `ErrorCodeFor` 那两条「遍历枚举」的守卫在这一层没有对应物。

具体输入下的后果：只可能由未来的编码错误触发（今天 6 个成员全部被显式列出），
所以不阻断；但它是本轮明确认定为不可接受的那个模式的最后一处残留。
最小修法：把 `_` 换成 `ItemUsability.NotAUsableItem => ItemUseStatus.NotAUsableItem` 加
`ItemUsability.Allowed or _ => throw new ArgumentOutOfRangeException(...)`，
并在 `ItemRefusalCodeTests` 加一条遍历 `ItemUsability` 的用例。**non-blocking。**

### R2-1.3 新判定顺序没有引入新的「更该报告的原因被盖住」

新顺序（`ItemUsabilityDomainService.cs:33-62`）：
不可读 → 角色状态 → 等级 → 职业 → 场所 → 冷却。

逐对检查「排在前面的原因是否比被它盖住的原因更不值得报告」：

| 先报 | 盖住 | 判断 |
|---|---|---|
| 不可读（`NotAUsableItem`→23） | 其余全部 | 正确。规则读不出时后面的比较都没有依据，`rule.MinimumLevel` 等字段此时是兜底值，拿它们比较才是猜 |
| 角色状态（静默） | 等级/职业/场所/冷却 | 正确。取不到角色就无法评价等级与职业；此时报「等级不足」才是编造 |
| 等级（14） | 职业/场所/冷却 | 无害。等级与职业都是永久原因，先报哪个都不误导；场所与冷却在本服都不是玩家能立刻消除的 |
| 职业（8） | 场所/冷却 | 无害，同上。职业是永久且不可改的，优先级合理 |
| 场所（19） | 冷却（19） | **客户端不可分辨**：两者映到同一个码 `19`。日志里可分辨，玩家侧无差别 |
| 冷却（19） | —— | 最后 |

第 1 轮 3.3 举的那个输入（A 与 B 共享 `[cooltime group]`，B 要 50 级，角色 10 级，
冷却期内问 B）现在答 `LevelTooLow`→`14`「Lv过低」而不再是 `19`「目前无法使用物品」。
**已修复，且被新用例 `A_permanent_reason_is_reported_ahead_of_one_that_will_expire` 钉住。**

反方向的新风险我逐条查过，**没有发现**：
- 冷却移到最后不会漏报冷却——`Allowed` 只在冷却检查通过后才返回
  （`:58-61` 的三元把冷却作为最后一道，不是可跳过的分支）。
- 冷却检查现在多做了 `FindCharacter` 与 `PlaceOf` 两次查询才到达，但两者都是只读的
  内存查表，且 `FindCharacter` 的结果本来就是后三条判定所需，没有多余代价。
- AC-001（「刚使用过、十秒内再问 → 冷却中」）在新顺序下仍成立：该场景里角色状态可取、
  等级职业场所都通过，落到最后一道冷却。`An_item_used_moments_ago_is_still_cooling_down`
  仍绿即为证。

## R2-2 两处超出建议范围的改动

### R2-2.1 3.11 扩到全部四个标量标签 —— 与 RQ-003 一致，不是自作主张

RQ-003 的原文是「归档未声明某项限制时该限制不生效；**声明了却读不出可用数值时**，
该道具判为不可使用」。它说的是「某项限制」与「可用数值」，没有把范围限定在 section 型标签；
Constraints 里那条「归档声明了某项限制却读不出可用数值时，按拒绝处理，不猜数值」同样如此。
`[cool time]` / `[cooltime group]` / `[minimum level]` / `[stackable type]`
四者都是「限制」（前三个直接是限制，第四个决定是否消耗，读错会销毁道具），
把它们排除在外才需要额外理由。**扩大范围是对契约的正确执行，不是越界。**

`ItemUseRule.IsReadable` 现在是 `Jobs.IsValid && Places.IsValid && !HasUnreadableDeclaration`
（`ItemUseContent.cs`），判定侧 `if (rule is null || !rule.IsReadable)` 未变，
所以四个标量的不可读与 job/place 的不可读走同一条出口，无第二条路径。

### R2-2.2 `HasUnreadableDeclaration` 的默认值 `false` 不会让任何构造点绕过它

`ItemUseRule` 是 `sealed record`，`HasUnreadableDeclaration` 是**最后一个**位置参数且默认 `false`。
全仓构造点：

- 生产唯一构造点 `PvfItemUseRuleDefinitionSource.Read`（`:71-79`）**显式传入**
  `HasUnreadableDeclaration: unreadable`，不依赖默认值。
- 测试构造点集中在 `ItemUseFixtures.ItemUseRules`（`Potion` / `Unlimited` /
  `RequiringLevel` / `RequiringJob` / `UsableIn`），全部省略该参数取 `false`
  ——这是对的，这些夹具刻意造的是「可读的规则」。
- `PvfItemUseRuleDefinitionSourceTests` 用 `with { HasUnreadableDeclaration = true }`
  显式打开（见 R2-3.3）。

默认值的风险面是「新增一个构造点、忘了传、于是悄悄变成可读」。
由于生产只有一个构造点且它显式传参，今天不可达。
（若要彻底消除，把参数提为必填并让夹具显式传 `false` 即可；不作为发现。）

### R2-2.3 扩大后的读取对真实归档 **零误判** —— 已全档实测

这是本轮最需要证据而不是推理的一条，因此做了一次**受控的临时改动并已还原**：

- 改动：新增临时文件
  `Server/tests/Dfo.Server.Infrastructure.Tests/Content/ZzTempReviewScanTests.cs`，
  用生产的 `PvfItemUseRuleDefinitionSource` 遍历 `stackable/stackable.lst` 的全部条目，
  统计 `HasUnreadableDeclaration` / `Jobs.IsValid` / `Places.IsValid` / `IsConsumedOnUse`。
- 运行：`dotnet test Server/tests/Dfo.Server.Infrastructure.Tests/... --filter ZzTempReviewScanTests`
  （`PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` 均指向 `DNFClient-debug/Script.pvf`）。
- 还原：该文件已 `rm`，`git status --short` 回到 32 条，与本轮开始时一致。**工作区无残留。**

实测输出：

```
REVIEWSCAN total=34542 nullRule=0 notConsumed=84
REVIEWSCAN unreadableScalar=0 ids=[]
REVIEWSCAN invalidJobs=64
REVIEWSCAN invalidPlaces=113
```

结论：

- **`HasUnreadableDeclaration` 在 34,542 个条目上全部为 false —— 本次扩大对真实归档
  一个道具都没有误判。** 这直接回答了「会不会把本来能用的道具误判为不可读」：不会，
  没有具体道具可举。
- `notConsumed=84` 与归档探针的 84 个 `unlimited*` **精确吻合**，
  说明 `[stackable type]` 在全档都被正确读成字符串（若 `FirstString` 在某处读不出，
  这个数会小于 84 且对应道具会变成不可读——两个指标互为交叉验证）。
- `nullRule=0`：lst 里每个 id 都能解析出规则。

补充核对（`PvfProbe values`，用于确认扩大后的行为在归档上不可达而非只是没测到）：
`[cool time]` / `[cooltime group]` / `[minimum level]` 三个标签，凡出现处其后首个 token
全为非负整数，无一例外；`[stackable type]` 凡出现处首个 token 全为带括号的字符串，
无数值型。与 `unreadableScalar=0` 一致。

### R2-2.4（non-blocking）全档扫描顺带纠正第 1 轮的一个计数：因不可读被拒的道具是 177 个，不是 29 个

第 1 轮 §3.10 我根据 `PvfProbe values` 的**首 token** 计数，估计被拒的是
职业 28 个（`[creator mage]`21 / `[prist]`5 / `[account]`1 / `[pall]`1）+ 场所 1 个（`[dont open]`），
共 29 个。全档扫描的真实数字是 **`invalidJobs=64` + `invalidPlaces=113`**
——因为多值声明里的不建模 token 出现在第二、第三个位置，首 token 统计看不到。

抽查两个具体道具确认这不是误判而是真的不建模取值：

| id | 文件 | 声明 | 判定 |
|---|---|---|---|
| `10000003` | `event/summer/event_10000003.stk` | `[usable job] [creator mage]` | 职业不建模 → 拒绝，正确 |
| `2660698` | `cash/charcter_rename_card.stk`（角色更名卡） | `[action usable place] [village] [seria room] [dont use assult]` | 含不建模的 `[dont use assult]` → 整条场所声明不可读 → 拒绝 |

第二个是值得注意的形状：该卡的场所白名单里**包含 `[village]`**，角色也确实站在城镇，
但因为同一条声明里多了一个服务端读不懂的 token，整条被判不可读，于是这张卡在本服恒不可用。
这符合 Constraints「读不出按拒绝处理，不猜数值」的字面要求，也符合保守方向；
但它的代价是 177 个已归档道具（部分是付费道具）从「本来能用」变成「恒被拒绝」，
数量级比 Spec 行文暗示的 29 大近六倍。

这不是本轮整改引入的——`ProjectJobs` / `ProjectPlaces` 两轮之间一字未改，
第 1 轮就是这个行为，只是我当时的计数偏小。
**non-blocking**：行为与契约一致，无需改代码；但这个真实数字与
「一条声明里混入一个不建模 token 就整条作废」这个形状应当写进 spec-result 的已知影响，
而不是留在评审记录里。

### R2-2.5 4.2 抛 `ArgumentOutOfRangeException` —— 同意这个选择，但后果比「得不到应答」更重，应当记下来

先把事实查清楚，再给判断。

**异常的真实去向**（不是「请求悬着」）：
`UseStackableHandler.ResolveAsync` 抛出 → `HandleMessageAsync` →
`NetworkMessageDispatcher.DispatchAsync`（`Server/src/Dfo.Server.Application/Dispatching/NetworkMessageDispatcher.cs`，
不捕获）→ `GameNetworkPlugin.OnTcpReceived`
（`Server/src/Dfo.Server.Networking/Runtime/NetworkPlugins.cs:140-165`），那里
`catch` 只做 `logger.LogError(...)` 然后 `throw;`。该处自带的注释写明：
「Without this the frame simply stops being processed and **the connection drops**」。
所以后果是：**这一个玩家掉线 + 一条 error 日志**，`e.InvokeNext()` 被跳过；
不波及其他连接，不使宿主崩溃。

**可达性**：三处 `switch` 现在都是全覆盖（见 R2-1.1），今天没有任何取值会落到 `_`。
`Moved` / `Unchanged` / `Used` 三臂虽然会抛，但调用点各自有
`if (result.Status is ... )` 提前返回成功，到不了。
唯一到达路径是「有人新增枚举成员且没在 `switch` 里列出」。

**这条路径上有没有闸门**：有。`Every_use_refusal_is_answered_with_a_code` 与
`Every_move_refusal_is_answered_with_a_code`（`ItemRefusalCodeTests.cs:68-96`）
遍历枚举逐个调用 `ErrorCodeFor`，新增成员未映射时该调用抛出，用例直接变红。
这正是第 1 轮 4.2 指出的「弱断言」被补强的机制——两者是配套的：
**没有这两条遍历用例，抛异常就只是把静默换成掉线；有了它们，抛异常才是「编译后第一次跑测试就暴露」。**

**判断**：同意实施者的结论，抛比兜底好，理由是三条而不是一条——
(1) 兜底的 `_ => Silent` 是一个**没人做过的决定**被伪装成决定，而抛出坦白承认「这里还没决定」；
(2) 错误发生在 CI 而不是玩家面前，因为遍历用例先撞上它；
(3) 即使漏到线上，一次掉线 + 一条 error 日志比「玩家看不到任何提示、日志里也只有一行
`refused as X`」更容易定位——后者正是本 Spec 一直在消灭的那种沉默。

唯一需要修正的是描述：后果不是「请求得不到任何应答」，而是**该连接被断开**。
这一点值得写进 spec-result，因为它决定了「漏到线上」时运维看到的现象是掉线而不是卡顿。
**无发现（选择正确）。**

顺带核对：`No_move_refusal_borrows_the_sentence_about_space`（`ItemRefusalCodeTests.cs:102-115`）
因为 `Moved`/`Unchanged` 现在会抛，加了 `continue` 跳过这两个值。
表面上是覆盖收窄，实际是加强——原先只断言它们不等于 `4`，现在它们连码都不允许有。

## R2-3 六条新增用例是否真断言

### R2-3.1 四条端到端用例：真断言，且各自钉住第 1 轮指出的那个空档

| 用例 | 期望值来源 | 变红条件（即它真正钉住了什么） |
|---|---|---|
| `A_character_whose_state_is_not_published_yet_is_not_the_players_fault`（`ItemUsabilityDomainServiceTests.cs:259-272`） | 字面枚举成员 `ItemUsability.CharacterStateUnavailable` | `FindCharacter` 为 null 时折回 `NotAUsableItem` 即红。该分支此前从未被执行过（第 1 轮 4.4），现由 `FixedRuntimeSessions.Unpublished`（`TryGet` 返回 false 且 `value = null`）真正走到 |
| `A_permanent_reason_is_reported_ahead_of_one_that_will_expire`（`:279-293`） | 字面 `ItemUsability.LevelTooLow` | 判定顺序改回「冷却先于等级」即红。构造的输入两个原因**同时成立**（组 70 的冷却已起、角色 10 级、道具要 50 级），所以它测的确实是优先级而不是单条判定 |
| `A_swap_whose_other_end_cannot_land_says_the_item_will_not_move`（`MoveItemSpaceHandlerTests.cs:202-223`） | `MoveItemSpaceResponse.NotMovable`，另有 `ItemRefusalCodeTests.cs:129-130` 用**字面 23** 钉同一映射 | 我手工走了一遍：`TwoBandPlacements` 让 10000 落在槽 9–64、8474 落在 65–120；请求 65→100 时 moving=8474 可占 100，displaced=10000 不可占 65，恰好且只能命中 `DisplacedCannotOccupySlot`。同时断言 `inventory.Rows()` 未变，不是只看错误码 |
| `The_item_commands_are_registered`（`CommandRegistrationTests.cs:47-54`） | 字面类型 `typeof(UseStackableCommand)` / `typeof(MoveItemSpaceCommand)` | 删掉 `ProtocolModule` 里任一条 `AddCommandHandler` 即红。这正是第 1 轮 4.5 指出的、原用例抓不到的那一半 |

### R2-3.2 `SessionGenerationOwnershipTests` 新增的两条：真断言，且第二条钉的是更容易漏的那一半

- `Cleanup_ClearsTheCharactersCooldowns`（`:202-219`）：起一个 30 分钟的组冷却，
  清理后断言 `IsCoolingDown` 为 false。删掉
  `SelectCharacterLifecycleServices.cs` 里的 `cooldowns.Clear(...)` 即红
  ——第 1 轮 4.6 的空档已关闭。
- `Cleanup_LeavesTheCooldownsOfACharacterWhoAlreadyReturned`（`:226-244`）：
  `departing` 与 `returned` 是**同一个 `CharacterId(70)`**、不同连接与代号，目录里注册的是
  `returned`；清理 `departing` 后断言冷却仍在。
  这条钉的是守卫 `if (sessions.FindByCharacter(...) is null)` 本身——
  去掉守卫、无条件 `Clear`，第一条仍绿而这条变红。
  两条合起来把「该清时清、不该清时不清」都覆盖了，是本轮质量最好的一对。

### R2-3.3（non-blocking）`A_declared_but_unreadable_scalar_makes_the_whole_rule_unreadable` 不是恒真，但只覆盖了机制的后一半

位置：`Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfItemUseRuleDefinitionSourceTests.cs:167-180`。

**不是恒真**：期望值是字面 `true` / `false`，不由被测代码产生。
把 `ItemUseRule.IsReadable` 改回 `Jobs.IsValid && Places.IsValid`（丢掉新的合取项），
第二条断言立刻变红。所以它确实钉住了一件事：**`IsReadable` 消费了这个标志**。

**但它没有覆盖真正新增的那一半**：谁来把 `HasUnreadableDeclaration` 置为 true。
用例用 `with { HasUnreadableDeclaration = true }` 直接造记录，全程不涉及标签、
不涉及 `PvfTokenReader`、不涉及 `FirstInt32` / `FirstString`。
把 `PvfItemUseRuleDefinitionSource.cs` 里那两处 `unreadable = true;` 删掉，
**这条用例仍绿，全套 841 条也仍绿**——因为真实归档里没有一个这样的文件
（本轮全档扫描 `unreadableScalar=0`，见 R2-2.3）。
方法名承诺的是「一个声明了却读不出的标量使整条规则不可读」，
断言到的只是「这个 bool 会让 `IsReadable` 变 false」。

后果：RQ-003 在标量侧的实现没有任何回归保护。今天不出错（归档里不存在触发输入），
但这正是「归档某天新增一个空声明」时会静默失效的地方——而那恰恰是本条改动存在的理由。

**更好的做法**（与 `ProjectJobs` / `ProjectPlaces` 改 internal 的思路完全一致，
仓库里已有现成脚手架）：把 `FirstInt32` / `FirstString` 由 `private` 改为 `internal static`，
用合成 token 流直接喂它们。`Server/tests/Dfo.Pvf.Tests/TokenReaderTests.cs:7-20`
已经有 `Script(params (PvfTokenKind Kind, int Value)[])` 这个五字节记录构造器，
照搬即可，不需要真实档案：

- `[cool time]`(Identifier) + `[next tag]`(Identifier) → 断言 `unreadable == true` 且返回 0；
- `[cool time]`(Identifier) + `Int32 10000` → 断言 `unreadable == false` 且返回 10000；
- 标签整体缺席 → 断言 `unreadable == false` 且返回 0（「未声明」不是「读不出」）；
- `[stackable type]`(Identifier) + `[next tag]`(Identifier) → `FirstString` 断言
  `unreadable == true`——这条最要紧，因为读不出时 `IsConsumedOnUse` 会退化成 true。

**non-blocking**（缺的是回归保护，不是错误行为；且不可达已由全档扫描证实）。

## R2-4 整改没有破坏第 1 轮确认过的东西

### R2-4.1 线上字节：逐字未变

对两个 `Encode` 函数做了与基线 `a0149f5` 的**逐行 diff**，两者均报告 IDENTICAL：

```
git show a0149f5:<slice> | sed -n '/public static byte\[\] Encode/,/^    }$/p'   vs  当前同段
→ MoveItemSpaceSlice.cs  Encode: IDENTICAL
→ UseStackableSlice.cs   Encode: IDENTICAL
```

`0x0013` 成功 11 / 失败 4、`0x002C` 成功 12 / 失败 11 的布局与字段顺序因此确定未动。

### R2-4.2 成功应答与「每请求恰好一帧」：未变

- `UseStackableSlice.cs:333` 仍是 `UseStackableResponse.Success(message, result.ItemId)`；
- `MoveItemSpaceSlice.cs:399` 仍是 `MoveItemSpaceResponse.Success(message, result.MoveValue, message.DestinationSlotIndex)`，
  `Moved` 与 `Unchanged` 共用同一出口（第 1 轮 §1.2 的两条路径结论不变）；
- 两个 handler 各自只有**一处** `SendExplicitAsync`（`:298` 与 `:358`），
  仍是「算出一个 response → 发一帧」。

### R2-4.3 拒绝条件的一一对应：条件表达式一字未改

对 `MoveItemSpaceCommandHandler.PlanAsync` 与基线做了条件级 diff
（抽出全部 `if (` / `return` / 计数表达式行比对）：**所有 `if (` 条件行完全相同**，
差异只出现在 `return` 语句上（`return null` → `return (null, ItemMoveStatus.XXX)`），
即第 1 轮 §1.1 那张一一对应表在本轮之后依然成立。
`UseStackableCommandHandler` 的四个拒绝出口
（`:236` `ListNotServed`、`:240` `NoBoundContainer`、`:249` `SlotDisagrees`、
`:263` `SlotChanged`）位置与条件也未动。

### R2-4.4 判定顺序改变：**放行集合不变**，只改「同时成立时先报哪一个」

这是本轮最需要确认的不变量。新旧两个顺序检查的是同一组五个条件的**合取**：
`Allowed` 都要求「规则可读 ∧ 角色可取 ∧ 等级够 ∧ 职业符 ∧ 场所允许 ∧ 不在冷却」。
换顺序不会让任何一个原本被拒的输入变成放行，也不会让原本放行的变成拒绝。
逐一核对唯一会变的两类输入：

| 输入 | 旧答案 | 新答案 | 是否可观察 |
|---|---|---|---|
| 冷却中 且 等级不足（共享组场景） | `OnCooldown`→`19` | `LevelTooLow`→`14` | 是——这正是 3.3 要修的 |
| 角色状态取不到 且 冷却中 | `OnCooldown`→`19` | `CharacterStateUnavailable`→静默 | 是——这正是 3.2 要修的 |
| 冷却中 且 场所不允许 | `19` | `19` | 否，两者同码 |
| 其余（只有一个原因成立） | 同 | 同 | 否 |

除这两条**本就是整改目标**的变化外，无其他可观察行为改变。
AC-001（十秒内再问答「冷却中」）仍成立：该场景下前五道全部通过，落到最后一道冷却，
`An_item_used_moments_ago_is_still_cooling_down` 仍绿即为证。

### R2-4.5 `ItemUseEffectDomainService.ApplyAsync` 的 `remaining`：可观察行为完全未变

改动是 `remaining = applied.RemainingCount` 加了条件
`if (consumption.StorageEntryId == item.StorageEntryId)`。

`PlanAsync` 是 `ConsumeFromContainerEffect` 的唯一生产者，它构造时传入的
`storageEntryId` 就是命令传下来的 `item.StorageEntryId`，且每次最多产生一条。
因此在今天可生成的两种效果集合形状下：

| 形状 | 旧 `remaining` | 新 `remaining` | 是否相同 |
|---|---|---|---|
| 无消耗（84 个 unlimited 道具） | `item.Count` | `item.Count` | 相同 |
| 单条消耗（其余全部） | `applied.RemainingCount` | 条件恒真 → `applied.RemainingCount` | 相同 |

**没有任何可观察差异**；改动只是把第 1 轮 3.7 指出的未来陷阱提前堵上。
（且 `0x002C` 的成功应答本就不带剩余数量，该值只进日志——即使有差异也不上线。）
容器拒绝时 `return null` 仍在冷却循环之前，第 1 轮 3.6 的结论不变。

## R2-5 自动化基线的独立复核

本轮自己跑了一遍全量（`Server/86JP.Server.slnx`，`-m:1`，
`PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` 均指向 `D:/project/DNF/DNFClient-debug/Script.pvf`）：

| 程序集 | 通过 | 跳过 | 失败 | 相对第 1 轮 |
|---|---:|---:|---:|---:|
| Dfo.Pvf.Tests | 60 | 0 | 0 | — |
| Dfo.Server.Application.Tests | 368 | 0 | 0 | +5 |
| Dfo.Server.Host.Tests | 4 | 0 | 0 | — |
| Dfo.Server.Infrastructure.Tests | 164 | 2 | 0 | +1 |
| Dfo.Server.Networking.Tests | 46 | 0 | 0 | — |
| Dfo.Server.Protocol.Tests | 199 | 0 | 0 | +1 |
| **合计** | **841** | **2** | **0** | **+7** |

与申报的 841 / 2 / 0 一致。新增的 7 条与整改回应里点名的七条逐一对上
（Application 5 条：`A_character_whose_state_is_not_published_yet_is_not_the_players_fault`、
`A_permanent_reason_is_reported_ahead_of_one_that_will_expire`、
`A_swap_whose_other_end_cannot_land_says_the_item_will_not_move`、
`Cleanup_ClearsTheCharactersCooldowns`、`Cleanup_LeavesTheCooldownsOfACharacterWhoAlreadyReturned`；
Infrastructure 1 条：`A_declared_but_unreadable_scalar_makes_the_whole_rule_unreadable`；
Protocol 1 条：`The_item_commands_are_registered`），**无未申报的用例增减**。
两条跳过仍是既有的缺档案用例，与本成果无关。

## R2-6 工作区状态

本轮为回答「扩大后的读取会不会误判真实道具」做了**一次受控的临时改动**，明细见 R2-2.3：
新增 `Server/tests/Dfo.Server.Infrastructure.Tests/Content/ZzTempReviewScanTests.cs`，
跑完即 `rm` 还原。还原后 `git status --short` 为 32 条，与本轮开始时逐条相同。
除本评审文件外，**工作区没有任何来自评审者的改动**。

---

# 第 2 轮结论

**decision: approved**

## 阻断项：0 条

第 1 轮唯一的阻断项 3.2 已修复且修法正确（R2-1.1）：
`ItemUsability.CharacterStateUnavailable` → `ItemUseStatus.CharacterStateUnavailable` → 静默码 `2`，
三层映射逐层显式，无一层依赖兜底；该分支由新增用例真正执行到，此前从未被跑过。

## 对四个提问的直接回答

1. **3.2 真修好了，新顺序没有引入新的遮蔽。** 新顺序「不可读 → 角色状态 → 等级 → 职业 →
   场所 → 冷却」逐对检查过（R2-1.3）：排在前面的原因在每一对里都更值得报告，
   `场所` 与 `冷却` 同映 `19` 因而玩家侧不可分辨。**放行集合与旧顺序完全相同**（R2-4.4），
   只有两类「多个原因同时成立」的输入换了报告对象，而那两类正是 3.3 与 3.2 的整改目标。
2. **两处扩大都正确。**
   - 3.11 扩到四个标量标签**与 RQ-003 一致，不是自作主张**：契约原文说的是「某项限制」
     与「可用数值」，未限定 section 型（R2-2.1）。`HasUnreadableDeclaration` 的默认值
     `false` 不会被绕过——生产唯一构造点显式传参（R2-2.2）。
     **对真实归档零误判**：全档 34,542 个条目扫描，`HasUnreadableDeclaration` 全为 false，
     没有具体道具可举；`notConsumed=84` 与归档探针精确吻合，构成交叉验证（R2-2.3）。
   - 4.2 抛异常**是对的**，且理由比「会立刻暴露」更强（R2-2.5）。需要修正的是对后果的
     描述：异常经 `NetworkMessageDispatcher`（不捕获）到 `GameNetworkPlugin`，那里
     log 后 `throw;`，**该玩家的连接会被断开**，不只是拿不到应答；不波及他人、不崩宿主。
     这个选择的安全性完全建立在两条遍历枚举的用例之上，二者是配套的，不可只留其一。
3. **六条新增用例（实际七条）都是真断言**（R2-3.1、R2-3.2）。
   `A_declared_but_unreadable_scalar_makes_the_whole_rule_unreadable` **不是恒真**
   ——期望值是字面量，丢掉 `IsReadable` 的新合取项即变红——但它只覆盖了机制的后一半，
   前一半（`FirstInt32` / `FirstString` 何时置位）无任何覆盖，删掉那两行 `unreadable = true;`
   全套仍绿。更好的做法与具体脚手架见 R2-3.3。
4. **没有破坏第 1 轮确认过的任何东西**：两个 `Encode` 与基线**逐行 IDENTICAL**（R2-4.1）；
   成功应答与「每请求恰好一帧」未变（R2-4.2）；`PlanAsync` 的全部 `if` 条件行与基线
   **一字未改**，一一对应表依然成立（R2-4.3）；判定顺序只改报告优先级、不改放行集合（R2-4.4）；
   `ApplyAsync` 的 `remaining` 在今天可生成的两种效果集合形状下**输出完全相同**（R2-4.5）。

## 本轮新增的非阻断项（3 条）

| 编号 | 位置 | 摘要 |
|---|---|---|
| R2-1.2 | `UseStackableSlice.cs` `UseStackableCommandHandler.StatusFor` 的 `_ => ItemUseStatus.NotAUsableItem` | 4.2 刚从两个 `ErrorCodeFor` 里清掉的兜底模式，在这一层仍在；新增 `ItemUsability` 成员若漏映射会变成码 `23`「非消耗品，无法使用」，即 3.2 的同类不诚实。无任何用例遍历 `ItemUsability`。今天 6 个成员全部显式列出，故不可达 |
| R2-2.4 | `PvfItemUseRuleDefinitionSource.ProjectJobs` / `ProjectPlaces`（两轮均未改） | 全档扫描纠正第 1 轮 §3.10 的计数：因声明不可读而被拒的是 **`invalidJobs=64` + `invalidPlaces=113`**，不是我估的 29。形状值得记录：一条声明里混入一个不建模 token 就整条作废——如 `2660698` 角色更名卡，`[village] [seria room] [dont use assult]` 因第三个 token 而整条不可读，于是这张付费卡在本服恒不可用。行为与 Constraints 一致，无需改代码 |
| R2-3.3 | `PvfItemUseRuleDefinitionSourceTests.cs:167-180` | 新标量三态判定的**前一半无回归保护**：删掉 `FirstInt32` / `FirstString` 里的 `unreadable = true;` 全套仍绿。已给出用合成 token 流的具体补法（仓库里已有 `TokenReaderTests.Script(...)` 脚手架） |

## 仍然悬着的、不属于代码的一项

1.5 / 2.3 的处置是「不改选码，改为留痕」，即把 `SlotDisagrees` 静默、
`ListNotServed` 与 `PartialSwapNotAllowed` 取 `23` 三条写进 EV-006 的「因本成果而改语义」清单；
R2-2.4 与 R2-2.5 的两点事实（177 个道具因不可读被拒、抛异常的后果是掉线）也应一并落到
spec-result。这些是 verification 阶段的产出，不在本次 diff 里，因此不作为阻断条件，
但在该清单写成之前，EV-006 尚未完整。

## 通过的部分

- 三层映射全覆盖、无可达兜底（R2-1.1）；新判定顺序逐对无遮蔽、放行集合不变（R2-1.3、R2-4.4）。
- 标量三态与 RQ-003 一致、默认值不可绕过、**全档零误判**（R2-2.1 至 R2-2.3）。
- 抛异常替代兜底的选择正确，且与两条遍历用例构成闭环（R2-2.5）。
- 七条新增用例全部为真断言；`Cleanup_Clears…` 与 `Cleanup_Leaves…` 成对覆盖「该清时清、
  不该清时不清」，是本轮质量最好的一对（R2-3.2）。
- 线上字节、成功应答、每请求一帧、拒绝条件的一一对应，四项与第 1 轮结论逐条复核后不变
  （R2-4.1 至 R2-4.3、R2-4.5）。
- 自动化 841 / 2 / 0 复核通过，新增用例数与申报逐条对上，无未申报的增减（R2-5）。

---

# 第 3 轮 —— 实施者对第 2 轮三条 non-blocking 的处置

第 2 轮已 `approved`，本节记录之后的改动。因为这些改动发生在批准之后，
需要第 3 轮独立确认它们没有引入新问题。自动化 **848 通过 / 2 跳过 / 0 失败**（第 2 轮时 841/2/0）。

| 编号 | 处置 |
|---|---|
| R2-1.2 | 已修。`UseStackableCommandHandler.StatusFor` 去掉 `_ => NotAUsableItem`，逐条列出全部 `ItemUsability` 成员，`Allowed` 与未知值抛 `ArgumentOutOfRangeException`；方法改 internal。新增 `Every_refusal_the_judgement_can_give_has_a_status_of_its_own`，除遍历外还断言各拒绝原因映到互不相同的 status——只列举而全映到同一个成员的话，前半条断言仍会绿。 |
| R2-2.5 | 已记录。两个 `ErrorCodeFor` 的注释写明（`StatusFor` 的注释只讲不用兜底的理由，不重复掉线那段）：抛出会经不捕获的 dispatcher 到插件，log 后 `throw;`，**该玩家掉线**；并写明这个取舍成立的前提是「抛出只可能由编码错误触达」，而维持这一点的正是那两条遍历枚举的用例——用例与方法是同一个机制，不可只留其一。 |
| R2-3.3 | 已修。`FirstString` 重构为 `TryReadFirstString(ref reader, tag, out PvfToken value, ref bool unreadable)`，把名表解析留给调用方，于是「声明是否可读」这个判断不再需要 archive；`FirstInt32` 同改 internal。新增 `PvfItemUseRuleTagReadingTests`，用合成 token 流覆盖三态各两条（未声明 / 声明但读不出 / 声明且可读）。 |
| R2-2.4 | 不是缺陷，是要记录的事实。177 个道具因声明里混入未建模 token 而恒不可用，与 Constraints 一致。连同 EV-006 的语义变更清单一并写入 spec-result 与 verification，不改代码。 |

## 变异验证

第 2 轮指出「删掉那两行 `unreadable = true;`，这条用例和全套 841 条都仍绿」。现在：

| 变异 | 变红的用例 |
|---|---|
| `FirstInt32` 不再置 `unreadable` | `A_declared_integer_tag_with_no_integer_behind_it_is_unreadable` |
| `TryReadFirstString` 不再置 `unreadable` | `A_declared_string_tag_with_no_string_behind_it_is_unreadable` |

（还原后已 `touch` 源文件：上一轮踩到过 MSBuild 因还原使 mtime 回退而跳过重编译，
导致二进制里残留变异、测试假红。）

---

# 第 3 轮 —— 独立评审结论

范围只限第 2 轮 `approved` 之后的增量。前两轮已确认的对照不重跑。

## R3-1 `TryReadFirstString` 重构：三种情形逐一与重构前相同

重构前（第 2 轮我读到的版本）：

```csharp
private static string? FirstString(
    PvfArchive archive, ref PvfTokenReader reader, TagRef tag, ref bool unreadable)
{
    reader.Reset();
    if (!tag.IsResolved || !reader.TryMovePast(tag))
        return null;                                  // A
    while (reader.TryReadSectionValue(out var token))
        if (token.IsString)
            return archive.ResolveString(token.Value); // B
    unreadable = true;                                 // C
    return null;
}
```

重构后（`PvfItemUseRuleDefinitionSource.cs:153-172` 与调用点 `:62-65`）：

```csharp
var stackableType = TryReadFirstString(
        ref reader, archive.GetTag("[stackable type]"), out var typeToken, ref unreadable)
    ? archive.ResolveString(typeToken.Value)
    : null;
```

逐情形核对：

| 情形 | 重构前 | 重构后 | 相同？ |
|---|---|---|---|
| **未声明**（`!tag.IsResolved` 或 `TryMovePast` 失败） | 走 A：返回 `null`，**不动** `unreadable` | 返回 false 且不动 `unreadable`，三元取 `null` | 是 |
| **声明但无字符串**（section 内下一个是 Identifier 或已到文件尾） | `while` 一次不进（或进而无 `IsString`），走 C：`unreadable = true`，返回 `null` | 同样走到循环后的 `unreadable = true; return false;`，三元取 `null` | 是 |
| **正常**（section 内第一个字符串 token） | 走 B：`archive.ResolveString(token.Value)` | `value = token; return true;`，调用方 `archive.ResolveString(typeToken.Value)` —— **同一个 token 的同一个 `Value`** | 是 |

循环体由 `if (token.IsString) return ...;` 改写为 `if (!token.IsString) continue; value = token; return true;`
——等价改写，仍是「跳过非字符串、取第一个字符串」。

另外三处可能出错而没出错的地方，逐条查过：

- `value = default;` 在方法第一行，`out` 参数在所有返回路径上都已确定赋值；三元的 false 分支
  根本不读 `typeToken`，不存在读到默认 token 再 `ResolveString(0)` 的路径。
- `archive.ResolveString(...)` 由「循环内调用」改为「返回后调用」。它只读名表、不碰 reader，
  且下一个读取函数（`FirstInt32`）第一行就 `reader.Reset()`，求值时机变化无影响。
- `Read` 里六次读取的**顺序未变**（`[stackable type]` 仍在最前），每个读取函数仍各自 `Reset()`，
  第 1 轮 §3.9「标签顺序不固定」的结论不受影响。

`FirstInt32` 的方法体与重构前**逐字相同**，只把 `private` 改成 `internal`。
`stackableType` 参与的表达式
`IsConsumedOnUse: stackableType is null || !stackableType.StartsWith("[unlimited", ...)` 未变，
因此「声明但无字符串」仍是 `IsConsumedOnUse = true` **且** `HasUnreadableDeclaration = true`
→ `IsReadable = false` → 拒绝，与第 2 轮一致。

全仓已无 `FirstString` 残留；两个方法的生产调用点仍只有 `Read` 一处。**无发现。**

## R3-2 `StatusFor` 去兜底：R2-1.2 已修

`UseStackableSlice.cs:275-287`：6 个 `ItemUsability` 成员逐条列出
（`NotAUsableItem` / `LevelTooLow` / `JobMismatch` / `OnCooldown` / `PlaceNotAllowed` /
`CharacterStateUnavailable`），`Allowed` 与 `_` 各抛 `ArgumentOutOfRangeException`。
调用点 `:253-254` 的 `if (verdict is not ItemUsability.Allowed)` 使 `Allowed` 那一臂不可达。
五个拒绝裁决映到**五个互不相同**的 status。

至此三处 `switch`（`StatusFor`、两个 `ErrorCodeFor`）都是「逐条列举 + 其余抛出」，
第 2 轮 R2-1.2 指出的最后一处兜底残留已清除。**无发现。**

## R3-3 XML 注释：两处属实，第三处与整改记录的表述有出入

`UseStackableSlice.cs:370-376` 与 `MoveItemSpaceSlice.cs:425-431` 两处 `ErrorCodeFor`
的注释确实写明了后果与前提，且与我第 2 轮 R2-2.5 查到的机制吻合：
「dispatcher 不捕获 → 插件 log 后 rethrow → **该玩家连接掉线**」，
并写明「这个取舍成立的前提是抛出只可能由编码错误触达，而维持这一点的正是那两条遍历枚举的用例；
用例与这个方法是同一个机制，单独留哪一个都没有价值」。表述准确。

出入：整改记录的表格写的是「两个 `ErrorCodeFor` **与 `StatusFor`** 的注释写明」，
实际 `StatusFor` 的注释（`:267-274`）只讲了「为什么不用兜底」，
没有那段掉线后果与「用例与方法同一机制」的说明。
（协调者本轮消息里说的是「两处」，与代码一致。）
`StatusFor` 同样会抛，单独读它的人拿不到这个后果说明。
不构成错误行为，只是记录与代码的一处小出入。**non-blocking，可不改。**

## R3-4 合成 token 流的前提成立 —— 这不是在测一个不存在的东西

你的假设是「标签是 identifier，其 value 就是名表偏移，而 `TagRef` 只比较这个」。
逐条对源码核实：

- `Server/src/Dfo.Pvf/Tokens/PvfTokens.cs`：
  `public bool Matches(in PvfToken token) => IsResolved && token.Kind == PvfTokenKind.Identifier && token.Value == Offset;`
  ——**只比较这两样**，不查名表、不碰 archive。`IsResolved => Offset >= 0`，
  所以 `new TagRef(7)` 与归档解析出来的已解析标签在行为上不可区分。
- `PvfTokenReader.Read`：`kind = (PvfTokenKind)_data[_position]`，
  `value = ReadInt32LittleEndian(_data.Slice(_position + 1, 4))`，步进 `PvfToken.Size`。
  测试里的 `Script(...)` 逐字节写的正是这个布局（kind 一字节 + LE int32 四字节），
  **是 `Read` 的精确逆运算**，且与 `Server/tests/Dfo.Pvf.Tests/TokenReaderTests.cs:8-20`
  已有的同名构造器一致。
- `TryMovePast` / `TryReadSectionValue` / `TryReadInt32` 三个方法全部只在 token 流上工作，
  不接触 archive。这正是本次重构把 `ResolveString` 移出去之后的结果：
  `FirstInt32` 与 `TryReadFirstString` 现在是**纯 token 流函数**，
  合成流对它们而言不是近似，是等价输入。

**前提成立，测试文件测的是真东西。**

需要说清楚的残留边界：合成流能证明「三态判断」对，**不能**证明归档里
`[waste]`、`[village]` 这类带括号的值确实编码为 `QuotedString` 而非 `Identifier`。
若它们是 Identifier，`TryReadSectionValue` 会在其之前停住，于是每个道具的
`[stackable type]` 都读成不可读。守住这一点的是另外两类证据：
真档用例（`Real_archive_reads_the_tutorial_potion` / `..._an_item_that_survives_its_own_use`）
与我第 2 轮的全档扫描（`unreadableScalar=0`、`notConsumed=84`）。
两类证据是互补的，**不可只留合成流那一类**——这一点值得写进测试文件的注释里。

## R3-5 七条新增用例逐条判断：全部真断言

### 六条合成流用例（`PvfItemUseRuleTagReadingTests.cs`）

我把每条的 token 流手工走了一遍：

| 用例 | 流 | 执行路径 | 钉住了什么（即怎样才会红） |
|---|---|---|---|
| `An_undeclared_integer_tag_is_absence_not_an_unreadable_declaration` | `(Id,9),(Int32,5)` | `TryMovePast(7)` 扫完全流未匹配 → 返回 0 | 「未声明」被误置为不可读即红 |
| `A_declared_integer_tag_with_no_integer_behind_it_is_unreadable` | `(Id,7),(Id,9)` | `TryMovePast` 命中；`TryReadSectionValue` 窥见 Identifier 即停 → `TryReadInt32` false | **删掉 `unreadable = true` 即红**——第 2 轮 R2-3.3 指出的空档正是这条 |
| `A_declared_integer_tag_that_reads_leaves_the_rule_readable` | `(Id,7),(Int32,10000)` | 正常读出 | 把「可读」也置为不可读即红（防过度触发） |
| `An_undeclared_string_tag_is_absence_not_an_unreadable_declaration` | `(Id,9),(QuotedString,3)` | 同第一条 | 同第一条，字符串侧 |
| `A_declared_string_tag_with_no_string_behind_it_is_unreadable` | `(Id,7),(Id,9)` | 循环一次未进 → 置位 | **删掉 `unreadable = true` 即红**；这是四个标量里最要紧的一个（读不出 → `IsConsumedOnUse=true` → 销毁不消耗道具） |
| `A_declared_string_tag_that_reads_returns_its_token` | `(Id,7),(QuotedString,42)` | 取到 token | 断言 `value.Value == 42`，即「返回未解析的原始 token」这个新契约 |

三态 × 两个函数 = 六条，**恰好覆盖且无冗余**；期望值全部是字面量，
没有一条的期望由被测代码产生。第 2 轮 R2-3.3 的空档已关闭。
（实施者另做了变异验证；我不依赖它——上表的路径是我自己走的。）

### `Every_refusal_the_judgement_can_give_has_a_status_of_its_own`（`ItemRefusalCodeTests.cs:104-122`）

两段断言各有分工，第二段是关键：

- 前半段遍历 `ItemUsability`（跳过 `Allowed`）调用 `StatusFor`。真正的守卫不是
  `Assert.NotEqual(Used, status)`（今天没有任何映射会产出 `Used`，这半句偏弱），
  而是**调用本身**：新增一个成员却没列出时 `StatusFor` 抛
  `ArgumentOutOfRangeException`，用例直接红。与两条 `ErrorCodeFor` 遍历用例同一机制。
- 后半段断言五个拒绝裁决映到**互不相同**的 status。这一段挡住的是「全部列举了、
  但都映到 `NotAUsableItem`」这种前半段抓不到的退化——实施者的这个判断是对的。

值得记一笔的是这条**单射性断言放对了层**：在裁决→原因这一层要求互不相同，
而在原因→客户端码那一层**允许**合并（`OnCooldown` 与 `PlaceNotAllowed` 同为 `19`，
因为客户端没有分开的文案）。两层的约束强度不同且各自正确，不是疏漏。

## R3-6 关于「为了可测把 private 改 internal」是否可接受

**在本代码库里这是既有做法，不是为本次测试开的口子。** 实测同层的既有内部方法：

```
Server/src/Dfo.Server.Infrastructure/Content/ 下的 internal static：
  PvfQuestDefinitionSource            6 个（NormalizeListEntries / ProjectSeekingItems /
                                          NormalizeRelationships / NormalizeTrainingBlocks /
                                          ProjectTargetCharacterRestriction / ProjectJobRestriction）
  PvfEntryLifecycleContentSource      5 个（Parse / CalculateGrant / ResolveItemMetadataFor /
                                          ResolveStackableItemKind / ResolveSlotRange）
  PvfCreatureEquipmentLifetimeSource  2 个（ResolveMetadata / TryParse）
  PvfItemUseRuleDefinitionSource      4 个（本次后：ProjectJobs / ProjectPlaces /
                                          FirstInt32 / TryReadFirstString）
```

且 `Dfo.Server.Infrastructure.csproj:20-22` 显式声明了三个
`InternalsVisibleTo`（Infrastructure.Tests / Application.Tests / Protocol.Tests），
`Dfo.Server.Protocol.csproj:10` 声明了一个（Protocol.Tests）。
可见「纯静态辅助函数开到 internal 供测试」是这一层的成文约定，你举的
`ProjectJobRestriction` 只是其中之一。

更重要的是**开出去的是什么**。这三个方法与上表里的既有内部方法形状一致：
全是 `static`、无实例状态、无 I/O。类自己的状态
（`gate`、`cache`、`stackables` 三个私有字段）一个都没有变得可见；
`Read` / `GetAsync` 也仍是私有/公开契约不变。所以这不是「打开封装」，
而是「把已经无状态的纯函数标注为可测」。

而且这次重构本身让被开出去的东西**变小了**：`TryReadFirstString` 相比原来的
`FirstString` 少了 `PvfArchive` 参数，职责从「读并解析成字符串」收敛为
「这个声明能不能读出来、是哪个 token」。名表解析回到 `Read` 里，
于是「声明是否可读」这个判断本身不再需要档案。
**这是一个独立成立的设计改进，可测性是它的副产品**——这比「为了测试而开放」有力得多，
也是我认为这次做法正确的主要理由。

我考虑过的两个替代方案都更差：

- **用合成 PVF 档案走 `GetAsync` 端到端**（仓库里有 `Dfo.Pvf.Tests/SyntheticArchiveTests.cs`
  这条路）：要为一个分支构造完整的档案容器（头、名表、文件表、加密），
  测试会绑死在容器格式而不是标签读取逻辑上，成本与脆性都更高，且测的范围远超被测点。
- **把两个函数抽成独立类型**（如 `PvfTagReader`）：那是**更多**暴露而不是更少，
  还会把只服务于一个读取器的逻辑从它身边搬走，得不偿失。

**结论：做法正确，无需改动。** 唯一可选的收紧是给这三个方法各加一行
「internal 是为了可测，生产调用点只有 X」的注释——`TryReadFirstString` 已经写了
（`:148-151` 说明了为什么名表解析留在调用方），`FirstInt32` 与 `StatusFor` 没写。
属可有可无，不作为发现。

## R3-7 自动化基线的独立复核

本轮自己跑了一遍全量（`Server/86JP.Server.slnx`，`-m:1`，
`PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` 均指向 `D:/project/DNF/DNFClient-debug/Script.pvf`）：

| 程序集 | 通过 | 跳过 | 失败 | 相对第 2 轮 |
|---|---:|---:|---:|---:|
| Dfo.Pvf.Tests | 60 | 0 | 0 | — |
| Dfo.Server.Application.Tests | 368 | 0 | 0 | — |
| Dfo.Server.Host.Tests | 4 | 0 | 0 | — |
| Dfo.Server.Infrastructure.Tests | 170 | 2 | 0 | +6 |
| Dfo.Server.Networking.Tests | 46 | 0 | 0 | — |
| Dfo.Server.Protocol.Tests | 200 | 0 | 0 | +1 |
| **合计** | **848** | **2** | **0** | **+7** |

与申报的 848 / 2 / 0 一致。增量分布与改动清单**精确对应**：
Infrastructure +6（`PvfItemUseRuleTagReadingTests` 六条），
Protocol +1（`Every_refusal_the_judgement_can_give_has_a_status_of_its_own`），
Application 不变（`StatusFor` 改 internal 未牵动任何 Application 用例）。
**无未申报的用例增减，也没有任何既有用例因本次重构被改期望值。**

## R3-8 工作区状态

`git status --short` 为 33 条 = 第 2 轮的 32 条 + 新增的
`Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfItemUseRuleTagReadingTests.cs`。
本轮评审只做静态核对与一次全量测试，**没有对工作区做任何改动**（第 2 轮那次受控临时改动已还原且未再引入）。

---

# 第 3 轮结论

**decision: approved**

## 阻断项：0 条

## 对四项改动与一个问题的直接回答

1. **`TryReadFirstString` 重构没有改变读取语义。** 三种情形逐一对照重构前的
   `FirstString`，「未声明 → `null` 且不置位」「声明但无字符串 → `null` 且置位」
   「正常 → 同一个 token 的 `ResolveString`」**全部相同**（R3-1）。
   `value = default` 使 `out` 参数在所有路径确定赋值，false 分支不读它；
   `ResolveString` 移到返回后调用不产生时序影响（它不碰 reader，且下一个读取函数首行即 `Reset()`）；
   六次读取的顺序与各自的 `Reset()` 未变。`FirstInt32` 方法体逐字未改。
   全仓无 `FirstString` 残留，生产调用点仍只有 `Read` 一处。
2. **`StatusFor` 去兜底正确。** 六个成员逐条列出，`Allowed` 那一臂被调用点挡住，
   五个拒绝裁决映到五个互不相同的 status。三处 `switch` 至此全部「列举 + 抛出」，
   R2-1.2 的残留清除（R3-2）。
3. **合成 token 流的前提成立，用例不是在测一个不存在的东西。**
   `TagRef.Matches` 的实现就是 `IsResolved && Kind == Identifier && Value == Offset`，
   不查名表；`Script(...)` 的字节布局是 `PvfTokenReader.Read` 的精确逆运算；
   重构后这两个函数已是纯 token 流函数，合成流对它们是**等价输入而非近似**（R3-4）。
   七条新增用例我逐条走了 token 流路径，**全部真断言**、期望值全为字面量、
   三态 × 两函数恰好覆盖且无冗余（R3-5）。
   `Every_refusal_the_judgement_can_give_has_a_status_of_its_own` 的第二段
   （五个裁决映到互不相同的 status）是关键的一半，你的判断对；
   并且这条单射性约束**放对了层**——裁决→原因要求互不相同，
   原因→客户端码允许合并（`19` 同时承载冷却与场所），两层强度不同且各自正确。
4. **注释两处属实**（两个 `ErrorCodeFor`），与我第 2 轮 R2-2.5 查到的机制吻合；
   整改记录里「与 `StatusFor`」那半句与代码不符（见 R3-3）。
5. **private → internal 是本代码库的既有做法，不是为本次测试开的口子。**
   同层已有 13 个同形状的 `internal static`（`PvfQuestDefinitionSource` 6 个、
   `PvfEntryLifecycleContentSource` 5 个、`PvfCreatureEquipmentLifetimeSource` 2 个），
   csproj 里也已显式声明 `InternalsVisibleTo`。开出去的全是无状态纯静态函数，
   类自己的 `gate` / `cache` / `stackables` 三个私有字段一个都没变得可见。
   **更强的理由是这次重构让被开出去的东西变小了**：`TryReadFirstString` 少了
   `PvfArchive` 参数，职责收敛为「这个声明能不能读出来」，
   于是「是否可读」的判断不再需要档案——这是独立成立的设计改进，可测性是副产品。
   我考虑过的两个替代（合成 PVF 档案端到端、抽成独立类型）都更差，理由见 R3-6。
   **做法正确，无需改动。**

## 本轮非阻断项（1 条，且是记录问题不是代码问题）

| 编号 | 位置 | 摘要 |
|---|---|---|
| R3-3 | 整改记录表格 vs `UseStackableSlice.cs:267-274` | 记录称「两个 `ErrorCodeFor` 与 `StatusFor` 的注释写明掉线后果」，实际只有两个 `ErrorCodeFor` 写了，`StatusFor` 的注释只讲了「为什么不用兜底」。`StatusFor` 同样会抛，单独读它的人拿不到后果说明。不构成错误行为，可不改，但记录应与代码一致 |

## 一条建议（不是发现）

合成流用例能证明三态判断对，**不能**证明归档里 `[waste]` / `[village]` 这类值确实编码为
`QuotedString` 而非 `Identifier`——若是后者，每个道具的 `[stackable type]` 都会读成不可读。
守住这一点的是真档用例与第 2 轮的全档扫描（`unreadableScalar=0`、`notConsumed=84`）。
两类证据互补，**不可只留合成流**。建议在 `PvfItemUseRuleTagReadingTests` 的类注释里点明这层分工，
免得日后有人认为合成流已经把真档用例覆盖了而删掉后者。

## 通过的部分

- 重构三情形语义等价，`FirstInt32` 逐字未改，读取顺序与 `Reset()` 不变（R3-1）。
- 三处 `switch` 全部「列举 + 抛出」，兜底残留清零（R3-2）。
- 合成流前提经源码核实成立；七条新增用例全部真断言，覆盖恰好完备（R3-4、R3-5）。
- 封装开放程度符合本层既有约定，且本次重构缩小而非扩大了被开出去的面（R3-6）。
- 自动化 848 / 2 / 0 复核通过，增量分布与改动清单精确对应，无未申报增减（R3-7）。
- 本轮改动**不触及**协议帧、成功应答、拒绝条件与判定顺序，前两轮相关结论无需重验。
