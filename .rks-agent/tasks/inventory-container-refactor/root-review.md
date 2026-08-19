---
kind: rks-review
decision: approved
---

# 根任务评审：道具容器抽象与使用效果分离

评审对象：`cf010d5`（成果一）与 `b5264ce`（成果二）合并后的当前基线。
判据：`TaskContract.md` revision 3（根契约）与 `multi-spec-map.md` 的 Success Condition Progress 表。
范围：只回答子评审在结构上答不了的四个问题——接缝、九条 SC、九条 Constraints、Out of scope。
子任务级的缺陷已由三轮独立评审各自查过，不在此重复。

## 1. 两刀之间的接缝

**结论：接缝没有裂。成果一确立的不变量在成果二之后仍然成立，且被比成果一更强的断言钉住。**

核对过程与依据：

**「道具读写只经容器」这条。** 成果二把容器从协议处理器下移到命令处理器，接缝本身没有被绕过：

- `Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs:225-228` 与
  `Server/src/Dfo.Server.Protocol/Game/UseStackable/UseStackableSlice.cs:221-224`——两个命令处理器
  的构造依赖是 `ICharacterContainerDomainService`，不含任何仓储类型。
- 两个协议处理器（`MoveItemSpaceSlice.cs:346-350`、`UseStackableSlice.cs:298-302`）现在既不含仓储
  也不含容器，只持 `ISelectedSessionDirectory` / `ICommandBus` / `IGameMessageSender` / `ILogger`。
- 效果侧没有自己去找存储：`Server/src/Dfo.Server.Application/Inventory/ItemUseEffectDomainService.cs:45-75`
  的 `ApplyAsync` 接收调用方传入的 `IItemContainer`，全部消耗都打在这一个容器上
  （`:65-75` 的 `foreach (var consumption in effects.OfType<ConsumeFromContainerEffect>())`）。
- 全仓 `IEntryInventoryMutationRepository` 的使用点只剩三处生产代码：容器实现自身
  （`Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs:51`）、入场加载与每日补给
  （`Server/src/Dfo.Server.Application/Inventory/SelectCharacterEntryInventoryDomainService.cs:12`）、
  以及教学关奖励发放（`Server/src/Dfo.Server.Protocol/Game/TaggedIdReport/TaggedIdReportHandler.cs:32`）。
  后两条都不是 SC-1 点名的那两条协议，且都在根契约 Out 或 Deferred Follow-up 之内。
- 断言比成果一时更强：`Server/tests/Dfo.Server.Application.Tests/Inventory/CharacterContainerDomainServiceTests.cs`
  的 `The_item_commands_reach_storage_only_through_a_container` 与
  `The_item_protocol_handlers_decide_nothing_about_items` 分别钉住命令层「有容器无仓储」与
  协议层「既无容器也无仓储、必须有 `ICommandBus`」两层。成果一只有前一层。

**移动语义有没有被顺手改掉。** 逐行比对 `git show b5264ce -- .../MoveItemSpaceSlice.cs`：`PlanAsync`
的每个判断条件、`moveCount` 的归一、正反两种读法的 `(moving, displaced, targetSlot)` 取法、
以及「同槽即成功且 MoveValue=0」的分支，一字未改；变的只是返回值从 `ContainerMovePlan?`
变成 `(Plan, Status)` 元组，以及日志从处理器搬到了各自的位置。`Encode` 两处均未被触碰。

**容器注册／清理的生命周期。** 成果二在 `Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs:130-139`
把 `cooldowns.Clear` 放进了容器清理那个 `Run(...)` 里，共用 `sessions.FindByCharacter(...) is null`
这个条件。这是对的，而不是偷懒：该条件的意义是「该角色没有别的活动会话」，冷却与容器一样是
按角色而非按连接持有的，同一个角色若已在另一条连接上重新入场，清掉任何一个都会打到活着的会话。
入场侧（`SelectCharacterEntryInventoryDomainService.cs:50-63`）只重建容器绑定、不清冷却，
这也是对的——重新登录时冷却已由上一条会话的清理抹掉，入场再抹一次只会让「重登绕过冷却」
多一条路径。行为与根契约 Assumptions 「冷却不持久化，重新登录会清空」一致。

### 发现

无发现。

（两处判断过、认为不构成发现，记录理由以免下次重查：其一，`containers.Clear` 与 `cooldowns.Clear`
共处一个 `try`，前者若抛异常后者会被跳过——但 `Clear` 是 `ConcurrentDictionary.TryRemove`，
不会抛，且最坏后果只是一条内存字典条目残留；其二，若 `sessions.TryRemoveExact` 抛异常，
两处清理都会因条件不成立而跳过——这是成果一就有的形状，成果二没有改变它，且残留的陈旧冷却
只会让下一次使用被多拒一会儿，方向保守。）

## 2. 九条根成功条件与其证据

先说测试基线。按要求跑了一遍：`PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` 均为
`D:/project/DNF/DNFClient-debug/Script.pvf`，`dotnet test Server/86JP.Server.slnx -m:1`。
分项为 Pvf 60、Application 368、Host 4、Infrastructure 170（跳过 2）、Networking 46、Protocol 200
——与申报的 **848 通过 / 2 跳过 / 0 失败** 逐项吻合（见下方发现 F-4 关于一次偶发失败）。
`SkippableFact` 只跳了两条「归档缺失」用例，说明依赖真实归档的用例这次都真的跑了。

逐条核对：

| SC | 判定 | 核对结论 |
|---|---|---|
| SC-1 | 成立 | 两个协议处理器与两个命令处理器的依赖已逐一读过，均不含持久化仓储；反射用例两层都钉住。**但 map 里这一行的证据文字已经过期**，见 F-1 |
| SC-2 | 成立 | 证据名副其实，见下 |
| SC-3 | 成立 | `CharacterContainerDomainService.Register` 的 `if (!current.ContainsKey(container.Type))` 即「首个注册者为当前」；`GetContainers` / `UseContainer` / `Remove` 各有用例 |
| SC-4 | 成立 | `CharacterContainerDomainService.cs:97-112`：回落容器在任何移除动作之前先找到，找不到就抛且不改 `registered` 与 `current`；两个用例分别覆盖回落与无回落 |
| SC-5 | 成立 | 属成果一的历史性陈述（791/2/0 = 基线 780/2/0 + 11），由 `cf010d5` 承载，不受成果二影响 |
| SC-6 | 成立 | `IItemUsabilityDomainService.CanUseAsync(CharacterId, int, CancellationToken)` 参数只有角色与道具；`ItemUsabilityDomainServiceTests` 18 条覆盖冷却／等级／职业／场所四类拒绝，另有 `Asking_does_not_start_a_cooldown` 证明只读 |
| SC-7 | 成立（按契约要求的证据） | 证据链完整但由两半组成，map 只引了一半，见 F-2 |
| SC-8 | 成立 | 同组共享、无组独立、期满恢复、组号与道具号不碰撞四条用例齐备；键的构造 `ItemUsabilityDomainService.CooldownKeyFor` 与效果侧共用同一函数，不存在两处推导 |
| SC-9 | 成立 | 两处 `ErrorCodeFor` 与一处 `StatusFor` 逐条列举全部枚举成员、未列举者抛出；`ItemRefusalCodeTests` 遍历枚举，因而「漏一个」会变红而不是变静默；静默码 `2` 与三张客户端码表比对不相交，三张表的取值与 `protocol/patterns/data/probes/cmd-reason-messages-20260818.md` 一致 |

**SC-2 特别核对**（契约 revision 2 重述过，我按重述后的文字核）。
`Server/tests/Dfo.Server.Protocol.Tests/Game/StateSynchronization/ContainerInventoryProjectionTests.cs`
不是自我比较：它在一份真实 SQLite 文件上播四行数据——其中一行故意落在 `list_type = 2`、
两行故意乱序——然后让入场加载路径（`CharacterInventoryEntryDomainService`）与容器
（`PersistentItemContainer`）各自独立读出来，再比 `0x000D` 快照与 `0x000E` 增量的编码字节。
容器若忘了过滤 list 或换了顺序，这里就红。容器形状里的 `Parameter` 在用例里取自 loader，
这一点我核过生产路径是同一来源——`SelectCharacterEntryInventoryDomainService.cs:58-62`
用的正是 `main.Parameter`——所以不是把待证的东西喂了进去。
SC-2 只交付「可作为来源」这一能力、生产路径留待多容器切换那一刀，这是契约 Assumptions
里写明并经用户授权的，map 的 Remaining gap 列也如实标注，属诚实收窄而非默默缩小。

**SC-7 特别核对**（实机确认已延后）。契约要求的证据是「针对该类道具的自动化测试通过」，
这一条已满足。但要真的证明 SC-7 的措辞——「归档中 `[stackable type]` 以 `unlimited` 开头的道具」
——需要两段：
1. `UseStackableHandlerTests.An_unlimited_item_is_used_without_leaving_the_pile` 证明
   「`IsConsumedOnUse = false` 的规则不产生减数量」，用的是手造规则 `ItemUseRules.Unlimited()`；
2. `PvfItemUseRuleDefinitionSourceTests.Real_archive_reads_an_item_that_survives_its_own_use`
   证明「真实归档里的道具 51（`stackable/cash/owl_unlimited.stk`）读出来就是 `IsConsumedOnUse = false`」。
两段都存在，合起来这条链是通的。我另核了读取实现
（`Server/src/Dfo.Server.Infrastructure/Content/PvfItemUseRuleDefinitionSource.cs:75-76`）：
`StartsWith("[unlimited", OrdinalIgnoreCase)` 直接作用于 `ResolveString` 的结果，没有做反引号剥离
——而第 2 段用例是打在真实归档上的，所以「归档串带不带反引号」这个隐患已被实测排除，不是靠推断。

### 发现

**F-1 — `multi-spec-map.md:77`（SC-1 的 Evidence 列）**
问题：证据文字写的是「两个**处理器**的构造依赖为 `ICharacterContainerDomainService`」。
这在 `cf010d5` 上为真，在 `b5264ce` 之后为假——协议处理器已不再持有容器，持有容器的是命令处理器。
而同一份文件的 Root Evaluation（于 `b5264ce`）又声明「SC-1..SC-9 全部 satisfied，证据见上表」，
等于把一条过期描述当作现状呈现。
后果：日后有人按这行字去核 SC-1，会读到 `MoveItemSpaceHandler` 里没有 `ICharacterContainerDomainService`，
从而误判 SC-1 已经回归。真正该引的是
`The_item_commands_reach_storage_only_through_a_container` 与
`The_item_protocol_handlers_decide_nothing_about_items` 这两条用例。
**non-blocking**（SC-1 本身成立，只是记录失真）。

**F-2 — `multi-spec-map.md:83`（SC-7 的 Evidence 列）**
问题：只引了 `An_unlimited_item_is_used_without_leaving_the_pile`。该用例喂的是手造规则，
它单独并不能连上 SC-7 措辞里的「归档中 `[stackable type]` 以 `unlimited` 开头」这半句；
把归档串接到 `IsConsumedOnUse` 的是 `Real_archive_reads_an_item_that_survives_its_own_use`，
而它没有被引。
后果：证据看起来比实际更弱。若日后有人删掉或弱化那条真实归档用例，SC-7 的引用不会察觉，
而「84 个道具不再被销毁」这句主张就失去了唯一的实测支点。
**non-blocking**（两条用例现在都在、都绿，缺的只是引用）。

**F-3 — 无。** SC-2 与 SC-9 是我按题目要求重点复核的两条，均未发现证据只覆盖条件一部分的情况。

**F-4 — `Server/tests/Dfo.Server.Host.Tests/ChannelHostStartupTests.cs:100`**
问题：首次全量运行时 `InvalidRequiredChannelScript_FailsBeforeListenersStart(kind: "unreadable")`
以 `TaskCanceledException`（`Process.WaitForExitAsync` 等待超时）失败，单独重跑该项目 4/4 通过。
该文件不在 `cf010d5` 与 `b5264ce` 任何一次改动里，是既有的时间敏感用例。
后果：「848 / 2 / 0」不是每次都能复现的——偶发一次 847/2/1。这不影响本任务的结论
（失败项与背包毫无关系），但记录在案，免得下一个人把这次偶发当成本次改动引入的回归。
**non-blocking**，且不属本任务范围。

## 3. 根契约的九条 Constraints

| # | 约束 | 判定 | 依据 |
|---|---|---|---|
| C-1 | 第一个成果不得改变任何对外可观察行为 | 成立 | 由 `cf010d5` 承载并已验收；成果二的行为变更全部落在 SC-6..SC-9 要求之内，逐条列于 `completion-evidence.md` 的 EV-006 清单 |
| C-2 | 容器自决持久或内存，使用方不区分 | 成立 | `IItemContainer` 没有任何暴露持久性的成员；`PersistentItemContainer` 与 `TransientItemContainer` 对同一 `ContainerMovePlan` 应用同一套接受／拒绝规则（`ItemContainers.cs:196-247`），并有 `ItemContainerEquivalenceTests` 钉住 |
| C-3 | 某类型下第一个被注册的容器成为当前容器 | 成立 | `CharacterContainerDomainService.cs:68-70` |
| C-4 | 移除当前容器时回落到首个不可移除者；无可回落视为程序错误 | 成立 | `CharacterContainerDomainService.cs:97-112`，且回落查找在任何写之前完成，抛出时 `registered` 与 `current` 均未被改动 |
| C-5 | 可用性判定为只读，且不暴露事实获取方式 | 成立 | `CanUseAsync(角色, 道具, ct)`，返回具名枚举；四类事实（冷却、等级职业、场所、归档规则）全部在 `ItemUsabilityDomainService` 内部取得；`Asking_does_not_start_a_cooldown` 证明提问不写 |
| C-6 | 协议帧的构造与推送不属于效果，效果只改变状态 | 成立 | `IItemUseEffect` 只有一个 `Target` 成员；两个具体效果分别写容器与写冷却字典，均不触碰 `IGameMessageSender`；应答在协议处理器里构造 |
| C-7 | 冷却以角色与共享标识为键、靠时间比较、不依赖定时回调 | 成立 | `CharacterItemCooldownDomainService.cs:23-36`：`ConcurrentDictionary<(int, ItemCooldownKey), long>` + `TimeProvider.GetUtcNow().UtcTicks` 比较，无 `Timer`／无回调注册；键的 `IsShared` 位使冷却组 70 与道具 70 不碰撞，且有专门用例 |
| C-8 | 一次道具使用的效果集合作用于同一个存储 | **有歧义，见 F-5** | — |
| C-9 | 拒绝原因不得借用文案与之不符的提示码；无相符文案时选不显示提示的码 | 成立 | 两处翻译表逐条对照 `cmd-reason-messages-20260818.md` 的「按拒绝原因选码」表，选码与建议一致；静默码 `2` 经三表比对确不在任何一张里 |

### 发现

**F-5 —— `Server/src/Dfo.Server.Abstractions/Inventory/IItemUseEffects.cs:17-46` 与
`Server/src/Dfo.Server.Application/Inventory/ItemUseEffectDomainService.cs:31-42`（对应 C-8）**

问题：一次「喝药」使用产生的效果集合是 `ConsumeFromContainerEffect`（`Target = Container`）
加 `StartItemCooldownEffect`（`Target = Character`）两项。而 `ItemEffectTarget` 的文档注释
自己写的就是「Which store an effect changes」——按代码自己的词汇，这一次使用跨了两个存储。
按约束字面读，C-8 不成立；按它与邻近条款合读（Assumptions「本契约只要求其目标同存储」、
Out of scope「跨存储的事务机制」），它想说的显然是「不要求跨存储事务、因而不许一次使用同时写两个
需要事务的持久存储」，而冷却是内存字典、不是需要事务的存储，这样读则 C-8 成立。

后果：当前无实际危害——`ApplyAsync` 把可能失败的容器写排在前、冷却写排在后，容器一拒就整体返回
`null`，不存在「扣了道具没起冷却」或反过来的中间态；冷却写本身也不会失败。真正的风险在下一刀：
经验类道具与仓库扩容的效果目标是 `Account`，届时一次使用会同时写容器与账号两个**持久**存储，
那时 C-8 无论怎么读都被违反，而现在这条约束在两份验收记录里都被标成 `aligned`、没有留下
「按哪种读法成立」的痕迹，下一个人会直接沿用这个 `aligned`。

**non-blocking**。代码不需要改。需要的是在根记录里补一句 C-8 的适用口径
（例如「目标同存储」指的是需要事务保护的持久存储，内存冷却不计入），否则这条约束在
`Account` 目标出现时会被无声地跨过去。

## 4. Out of scope 有没有被越界

**结论：没有越界，也没有「文档声称做了、实际没做」的项。**

逐条核对根契约 Out 列表：

| Out 项 | 核对结论 |
|---|---|
| 多容器切换的持久化结构与触发协议 | 未做。`UseContainer` / `Remove` 只有测试在调，生产代码里没有任何调用点；没有新增表、字段或协议帧。契约 Scope In 只要求「可切换、可移除」的能力，SC-3 的证据要求也只是自动化测试 |
| 副本临时容器的可见性策略 | 未做。`TransientItemContainer` 只有测试构造，没有任何生产路径把它注册给角色 |
| 跨存储事务 | 未做。`ItemUseEffectDomainService.ApplyAsync` 是顺序应用 + 早退，没有引入任何事务协调 |
| 装备穿戴／丢弃／排序／售卖等其余道具协议 | 未做。两次改动只触及 `0x0013` 与 `0x002C` 两个切片 |
| 经验类道具与仓库扩容的效果实现 | 未做。`IItemUseEffect` 的实现只有 `ConsumeFromContainerEffect` 与 `StartItemCooldownEffect` 两个；`ItemEffectTarget.Account` 有枚举值但无实现，与契约 Assumptions「模型要能表达三类目标，但只实现现有协议能触达的」一致 |
| `Tool/` 与 `.worktrees/legacy-reference/` | 未改。`git show cf010d5 --name-only` 与 `git show b5264ce --name-only` 过滤这两个前缀均无命中 |

顺带核了两处「顺手拆重复」是否夹带了范围外的行为变更：
`ArchiveJobFamilyTokens`（自 `PvfQuestDefinitionSource` 抽出）与 `CharacterJobFamilies`
（自 `WorldContentEntryRuleService` 抽出）都是逐条搬迁——16 个词条的拼写、顺序、
`StringComparer.OrdinalIgnoreCase` 比较器、以及 12 个职业号的映射（含 `8 or 10` / `9 or 11` 两处合并
与 `DemonicSwordman` 故意不可达）全部一字未变。`PvfQuestDefinitionSource` 里
`JobTokens.Where(...)` 换成 `ArchiveJobFamilyTokens.All.Where(...)` 后，字典的插入顺序也一致，
因而 `QuestTargetCharacterRestriction` 的元素顺序不变。任务本身需要这张表（`[usable job]` 判定），
不属越界。

### 发现

**F-6 —— `Server/src/Dfo.Server.Abstractions/Inventory/IItemContainer.cs:41-89`（范围内的遗漏，程度轻）**

问题：根契约 Scope In 写的是容器「支持按槽读、写、删，以及枚举已占用的槽」。实际接口提供
`ReadSlotAsync` / `ReadSlotsAsync` / `SnapshotAsync` / `ApplyMoveAsync` / `ConsumeAsync`
——读、枚举、以及以「移动」和「消耗」形式出现的复合写与删都有，但**没有「把一件道具放进某个槽」
这个原始写操作**。`TransientItemContainer.Place`（`ItemContainers.cs:273`）实现了它，
却没有出现在 `IItemContainer` 上。

后果：拿到 `IItemContainerFactory.CreateTransient` 返回的 `IItemContainer` 的调用方，
没有办法往里放东西——副本要给玩家一个装了战利品的临时背包时，必须先把接口拓宽，
或者向下转型到具体类。同理，教学关奖励发放（`TaggedIdReportHandler`）至今仍直接写仓储，
而它正是唯一一条需要「按槽写」的既有路径。

**non-blocking**。没有任何一条 SC 要求这个原语（SC-1 只点名移动与使用两条协议，
奖励发放协议不在 Scope In 里），当前也没有生产调用方被卡住；记录它是为了让下一刀知道
接缝在这个方向上还没打通，而不是以为已经打通了。

## 5. 两份长期留存记录与代码是否相符

核的是 `attempts/spec-002-item-use-rules-and-effects/spec-result.md` 与
`attempts/spec-002-item-use-rules-and-effects/verification/completion-evidence.md`
里的事实性陈述，逐条打到代码上。

**核过且相符的（这些是最容易写错、也最难事后发现的几条）：**

- `completion-evidence.md:26` 的测试分项 —— Pvf 60 / Application 368 / Host 4 /
  Infrastructure 170（跳过 2）/ Networking 46 / Protocol 200 = 848 通过 / 2 跳过。
  我这次实测逐项一致，不是抄的。
- `completion-evidence.md:59` 「`PvfItemUseRuleDefinitionSource` 现为
  `CooldownMilliseconds: Math.Max(0, cooldown)`」——属实（该文件第 77 行），
  实机走查用的「冷却 ×2」探针确已还原；全仓 `grep TEMPORARY` 无残留。
- `completion-evidence.md:89-98` 的 `0x0013` 新旧码对照 —— 我把 `cf010d5` 版本的
  `MoveItemSpaceHandler` 调出来逐个分支比对，八行全部属实（旧 `InvalidOperation = 0x02`、
  旧 `Rejected = 0x04` 各自落在哪几条路径上，与表里写的一致）。
- `completion-evidence.md:104-111` 的 `0x002C` 新旧码对照 —— 同样属实，
  旧 `InvalidItem` 的字面值确为 `23`，所以「列表不服务 23→23 未变」这行是准的。
- `spec-result.md` 「Known Gaps」里那条最需要警惕的断言 ——「容器行自带的 `ItemKind == 1`
  检查被移除；新的拒绝集合是旧集合的超集，唯一的理论例外是『道具在 stackable 索引内、
  但存储行上的 kind 被写成 1』」。我按这条自己走了一遍：新判定的入口是
  「该 id 在 `stackable/stackable.lst` 里有没有条目」；而
  `PvfEntryLifecycleContentSource.ResolveItemMetadata` 对任何 stackable 条目
  都返回非 1 的 kind 与非空槽区间，因此「在 stackable 索引内却 `placement is null`
  或 `placement.ItemKind == 1`」不可能发生——旧代码的另外两道闸门确实被新闸门完全覆盖，
  「唯一的理论例外」这个措辞是准确的，不是乐观说法。
- `spec-result.md` 「新增 57 个用例」（848 − 791）、「三轮评审 16 条发现处置 12 条」
  与 `reviews/change-review.md` 的记载一致。

### 发现

**F-7 —— `attempts/spec-002-item-use-rules-and-effects/spec-result.md:53`**
问题：写的是「`SelectedSessionCleanupService` 在会话目录条目移除后清理冷却记录，
**与容器注册同一条件**」。共用条件的是容器的**清理**（`containers.Clear`），不是注册；
注册发生在入场路径的 `SelectCharacterEntryInventoryDomainService.BindContainers` 里，
与该条件无关。
后果：轻。但这句话是描述生命周期接线的唯一一句，读者若照字面去找「容器注册」处的条件，
会找错地方，进而可能误以为入场也带着这个判断。
**non-blocking**。

**F-8 —— `attempts/spec-002-item-use-rules-and-effects/verification/completion-evidence.md:67`
与 `multi-spec-map.md:58`**
问题：前者写「依次尝试过**四个** `[unlimited *]` 道具」，其下的表格是四**行**但列了六个 id
（`51`、`1120`、`1143`、`10008052` / `10008051` / `2749196`）；根 map 的 Deferred Follow-up
则写「**六个**」。同一件事在两份长期记录里数字不一致。
后果：轻，但这正是「写错比代码错更难发现」的那类——这项走查是被**延后**的，
日后重启它的人会拿这份清单当起点，两个数字对不上会让人不确定是否还有第五、第六个候选没试过。
按表格内容，正确的说法是「四类现象、六个道具」。
**non-blocking**。

**F-9 —— 无其他不符。** 特别核过但确认无误的两处高风险陈述已列在上面（`ItemKind == 1`
超集论证、以及 EV-006 的两张新旧码对照表），未发现任何一处与代码相反的断言。

## 结论

**approved。**

九条根成功条件在当前基线上全部成立，其证据我逐条追到了用例与代码，没有一条是自我比较、
恒真或只覆盖了条件的一部分；两刀之间的接缝没有裂，成果二不但没有绕过成果一确立的
「道具读写只经容器」，还把这条不变量的断言从一层加到了两层；Out of scope 没有被越界，
范围内也没有「声称做了实际没做」的项。

九条发现全部 non-blocking，没有任何一条要求改代码。其中值得根控制器在收尾时顺手处理的是
三条记录类的：**F-1**（SC-1 的证据文字已过期，会让日后的核对得出相反结论）、
**F-2**（SC-7 的证据只引了两段链条中的一段）、**F-5**（C-8 「同一个存储」的适用口径没有留痕，
而下一刀的 `Account` 目标效果会直接撞上它）。**F-6** 与 **F-8** 是给下一刀的输入，
不必现在动。**F-4** 与本任务无关，仅作留痕。

