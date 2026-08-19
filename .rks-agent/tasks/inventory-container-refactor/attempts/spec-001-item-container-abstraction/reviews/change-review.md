---
kind: rks-change-review
decision: approved
---

# Change Review: 容器抽象与角色绑定（第 3 轮 · 本阶段最终评审记录）

本文件是 `spec-001-item-container-abstraction` 全阶段三轮评审的完整记录，取代前两轮的同名文件。
第 3 轮为窄口径复审，只回答两个问题：第 2 轮阻塞项的契约修订是否让验收面与实物重新对齐，
以及第 2 轮三条非阻塞项的修复是否引入了新的问题。前两轮已查证的事项不再重查，其结论保留在
「Verified Non-Issues」一节，供 verification 阶段与后续刀次直接引用。

**结论：approved。无阻塞项。** 三条非阻塞项均给出了具体的收敛动作，列在末节。

## Scope Reviewed

审查对象为工作区 `D:/project/DNF/DNF-JP-86-Server/.worktrees/inventory-container-refactor`
分支 `inventory-container-refactor` 在 `2eacf09` 之上的全部未提交改动（`git diff` 加
`git status --short -uall` 列出的未跟踪文件），对照：

- 子契约 `attempts/spec-001-item-container-abstraction/TaskContract.md`（revision 2，AC-1..AC-5）
- `attempts/spec-001-item-container-abstraction/spec.md`（revision 2，RQ-001..011、AC-001..009、EV-001..005）
- 根契约 `TaskContract.md`（revision 2，其 SC-1..SC-5 由本子任务承载）

本轮重点实读与推演：

- 两份契约与 spec 的全文，逐句核对「已建之物」与「所声称之物」，含 Scope、Success Conditions、
  Assumptions 三处各自的口径
- 七处 revision pin 的一致性
- `ItemContainers.cs` 的 `PersistentItemContainer.ApplyMoveAsync`（`:78-94`）、`ToStorageKind`
  （`:105-112`）与 `TransientItemContainer.Apply`（`:196-247`），逐分支比对
  `SqliteEntryInventoryMutationRepository.ApplyMainMoveAsync`（`:320-386`）
- `SelectCharacterEntryInventoryDomainService.BindContainers`（`:45-57`）与新测试
  `Entry_binds_the_character_to_its_own_main_container`（`SelectCharacterEntryLifecycleServiceTests.cs:153-188`）
- `CharacterEntryInventoryTests.CreateLifecycleResultAsync`（`:19-30`）与 `EmptyInventory`（`:574-583`）
  所产出的状态形状，以及它与 `InventoryEntryProjection.ProjectCharacter`（`InventoryEntryServices.cs:47-86`）
  产出形状的差距
- `ItemContainerEquivalenceTests.RunScriptAsync` 新增的自指计划一段（`:118-124`）在两个容器上的状态推演

本机测试：`PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` 均设为
`D:/project/DNF/DNFClient-debug/Script.pvf`，`dotnet test Server/86JP.Server.slnx -m:1`，退出码 0——
Pvf 60、Application 339、Host 4、Infrastructure 154（跳过 2）、Networking 46、Protocol 188，
合计 **791 通过 / 2 跳过 / 0 失败**，与「780 基线 + 11 条新测试」吻合。Application 由第 2 轮的 338
增为 339，增量正是那条新增的绑定测试。

首跑时 `Dfo.Server.Host.Tests.ChannelHostStartupTests.InvalidRequiredChannelScript_FailsBeforeListenersStart`
以 `TaskCanceledException`（`Process.WaitForExitAsync`）失败过一次，重跑通过。该用例启动一个独立宿主进程
并等待其退出，而本次 diff 未触碰频道脚本、宿主启动或监听器的任何一行；判为契约 AC-5 备注中已知的
「归档加载相关的进程启动超时」抖动，不计入基线。

## Round History and Disposition

| 轮次 | Finding | 本轮核实的最终处置 |
|---|---|---|
| R1 阻塞 1 | 投影测试恒真，且生产代码无容器供给的投影 | 已解决。恒真断言在第 2 轮即已消除；契约一侧的缺口转为 R2 阻塞 1，见下行 |
| R1 非阻塞 1 | 枚举序数强转 | 已解决。`ItemContainers.cs:105-112` 逐成员写出 |
| R1 非阻塞 2 | `ReadMainItemsAsync` 校验宽松 | 有意不处理，两轮均接受。理由见 Verified Non-Issues 第 1 条 |
| R1 非阻塞 3 | 临时容器无锁且注释理由不成立 | 已解决。`ItemContainers.cs:148` 加锁，`:132-143` 注释重写 |
| R1 非阻塞 4 | 接口文档窄于实现 | 已解决。`ICharacterContainerDomainService.cs:25-28`、`:36-40` 两处异常写明 |
| R1 非阻塞 5 | 等价脚本只覆盖三种移动 | 已解决。`RunScriptAsync` 现覆盖 Relocate / Split / Swap / Merge / Consume 与三条拒绝 |
| **R2 阻塞 1** | RQ-010 已据实收窄，但其所服务的契约 AC-2 / SC-2 没跟着收窄 | **已解决**（按 R2 给出的收敛方向 1，经用户于 2026-08-18 授权）。根 SC-2 与子 AC-2 重述为「当前容器可作为背包投影内容的来源」，根契约 `TaskContract.md:108-112` 新增 Assumption 记下延后的理由与归属，两份契约推进至 revision 2。本轮确认新措辞与实物一致——但收窄只落在验收行上，三份工件的 Scope 行未跟着改，转为本轮非阻塞 1 |
| R2 非阻塞 1 | 两个容器对未知移动种类的答案不一致 | 已解决。`ToStorageKind` 改返 `MainInventoryMoveKind?`，`ApplyMoveAsync`（`:82-83`）对未知种类答 `false`；`IItemContainer.cs:71-79` 的文档补上「False, never an exception」 |
| R2 非阻塞 2 | 自指 Merge 计划在两个容器上给出三种不同结果 | 已解决。两侧各自在容器层前置守卫（`ItemContainers.cs:82`、`:200-201`），仓储未被触碰；等价脚本 `:118-124` 覆盖 |
| R2 非阻塞 3 | AC-008 说「当前容器」，而 EV-004 的测试绕过了产生当前容器的那段代码 | 已解决。新测试覆盖 `BindContainers` 注册的容器是 Main、不可移除、且携带**加载来的**容量参数 |

## 第 2 轮三条修复的独立复核

本节回答「修复本身是否成立」，逐条自行读码推演，未采信口头结论。

**1. `ToStorageKind` 与 `is not { } kind` 的空值判定（`ItemContainers.cs:82-83`）——成立。**

```
if (plan.DisplacedEntryId == plan.MovingEntryId || ToStorageKind(plan.Kind) is not { } kind)
    return ValueTask.FromResult(false);
```

三处值得确认的语义都对：其一，`DisplacedEntryId` 是 `long?` 而 `MovingEntryId` 是 `long`，
`==` 走提升后的可空比较，`null` 不与任何值相等，因此「目标槽本来是空的」不会被误判为自指；
其二，`{ }` 属性模式作用在 `MainInventoryMoveKind?` 上判的是 `HasValue`，`kind` 的类型被收窄为
不可空的 `MainInventoryMoveKind`，正是 `MainInventoryMovePlan` 所需；其三，`||` 短路后
`kind` 仍满足「为 false 时确定赋值」——左支为 false 时右支必被求值，右支为 false 时 `kind` 已赋值——
所以 `:92` 处的使用合法，编译通过（本轮全量构建为证）。两个容器现在对未定义的 `ContainerMoveKind`
都答 `false`（持久侧 `:82`，内存侧 `:242-243`），RQ-008 所要求的「使用方不区分二者」在这一维成立。

**2. 自指计划守卫的落位——两个容器确实一致。**

持久侧的守卫在 `:82`，位于任何存储调用之前；内存侧在 `Apply` 的第一行 `:200-201`，位于取
`moving` / `displaced` 之前。两侧都是无条件 `return false`，不受后续检查顺序影响，所以「谁先检查什么」
不产生可观察差异。守卫同时把 R2 非阻塞 2 的根因关掉了：`Replace`（`:296-297`）以「`current` 仍在表里」
为前提，而唯一能让同一条记录在一次 `Apply` 里被替换两次的输入就是自指计划；现在 Merge 的
`Replace(displaced,...)` 与 `Replace(moving,...)`、Swap 的两次 `Replace` 都作用在两条
`StorageEntryId` 必然不同的记录上，`items.IndexOf` 不会再返回 -1。仓储侧
（`SqliteEntryInventoryMutationRepository.cs`）如约未被触碰，本轮 diff 对该文件的唯一改动是新增
只读的 `ReadMainItemsAsync`（`:220-242`）。

等价脚本 `:118-124` 构造的 plan 是（moving = 66 槽那条，target = 66，count = 全部，displaced = 自己，Merge），
两侧都在守卫处返回 false，观测行 `self False` 一致。该断言有判别力：只摘掉持久侧的守卫，
存储会先写成 v+m 再删行，`unmoved`（`:125`）与 `final` 行随之分叉；只摘掉内存侧的守卫，
`items.Remove(moving)` 会因结构相等不成立而静默失败，道具留在原地且数量翻倍，同样分叉。

**3. 新测试的判别力——「写 0 会不会挂」的答案是「会挂」。**

`InventoryEntryProjection.GetDefaultParameter`（`InventoryEntryServices.cs:329-330`）对 Main 返回 0，
所以 156 与类型默认值确实不同。把 `SelectCharacterEntryInventoryDomainService.cs:54` 的
`container.Parameter` 写成 `0`，`SelectCharacterEntryLifecycleServiceTests.cs:185` 的
`Assert.Equal(loaded.Parameter, bound.Shape.Parameter)`（156 对 0）与 `:186` 的
`Assert.Equal(156, bound.Shape.Parameter)` 会同时失败。该测试还顺带钉住了另一件事：
`BindContainers` 必须用**重新加载**的库存而不是入参 `result.CharacterInventory`——后者的
`Containers` 是空集合，若用错，`GetContainer`（`:184`）会抛 `InvalidOperationException`。
R2 非阻塞 3 指出的「AC-008 说当前容器，证据里却少了产生当前容器的那一段」由此补齐：
一半由本测试证明 `BindContainers` 注册的是 `character:{id}:Main`、`IsRemovable == false`、
形状取自加载结果；另一半由 `ContainerInventoryProjectionTests` 证明同一形状的
`PersistentItemContainer` 与加载路径逐字节一致。两半的容器标识与形状取值完全同源
（`ContainerInventoryProjectionTests.cs:115-120` 的 `"character:60:Main"` 与
`SelectCharacterEntryInventoryDomainService.cs:52` 的
`$"character:{characterId.Value}:{container.ContainerType}"` 对同一角色同值），组合成立。

## Round 3 Findings

### 非阻塞 1 — 收窄只落在验收行上，三份工件的 Scope 行仍写着「改由容器供给」

- 严重度：non-blocking
- 位置：`.rks-agent/tasks/inventory-container-refactor/TaskContract.md:57`、
  `.../attempts/spec-001-item-container-abstraction/TaskContract.md:51`、
  `.../attempts/spec-001-item-container-abstraction/spec.md:40`

验收行确已据实修订，本轮逐句核过：根 SC-2（`:87`）与子 AC-2（`:75`）现在都只说
「当前容器可作为背包投影内容的来源」，证据栏只要求「编码所得帧字节与入场加载路径一致」，
这两句对应 `PersistentItemContainer.SnapshotAsync`（`ItemContainers.cs:74-76`）与
`ContainerInventoryProjectionTests` 的两条真会失败的断言，属实。根契约 `:108-112`
的新 Assumption 把「为什么不真正切换」与「留待多容器切换一并落地、届时另行评估与授权」写清楚了，
`multi-spec-map.md:39-51` 的 Deferred Follow-up 与之呼应。这一半是干净的。

问题在于三份工件的 Scope 段没有跟着改，仍然把「切换」列为范围内：

- 根 `TaskContract.md:57`：「背包全量投影与增量投影改由容器供给」
- 子 `TaskContract.md:51`：「`0x000D` / `0x000E` 投影所用的 `InventoryContainerState` 改由容器供给」
- `spec.md:40`：「背包投影所需的容器状态改由当前容器供给」

`spec.md` 因此自相矛盾：`:40` 说投影「改由」当前容器供给，`:47` 的 Non-Goals 又把
「每日补给与选角入场路径的存取方式，包括入场时一次性产生的五份初始背包快照」排除在外，
而这两条正是仓库里仅有的两条投影路径——于是 `:40` 所指的对象在本 Spec 内并不存在。
`:83` 的 RQ-010 与 `:112-115` 的 Assumptions 都已改成「可由」。子契约同理，且它比根契约更薄：
根契约把延后写进了 Assumptions，子契约的 Assumptions（`:86-91`）里没有对应的一句。

具体失败场景：`sdd-execution.yaml:42-49` 规定 verification 阶段读 `contract` 与 `change_review`，
逐条核 Success Conditions——这一关会正常通过，因为 AC-1..AC-5 都真。真正的代价在其后：
成果二的 spec 作者（或任何一个重新捡起这个任务的人）读根契约划边界时，Scope 段是最先读的一段，
他会看到「背包全量投影与增量投影改由容器供给」列在 In 里、SC-1..SC-5 又已判定达成，从而得出
「投影切换属于成果一且已交付」的结论。而事实是 `SnapshotAsync` 至今在 `Server/src` 下零调用方
（全仓检索只命中 `IItemContainer.cs:69` 的声明、`ItemContainers.cs` 的两处实现与三个测试文件），
入场快照仍由加载路径产生。他会据此把「入场快照已经从容器来」当作既成事实去设计多容器切换，
而这恰恰是 `multi-spec-map.md:46-48` 明确点名会导致「切换后重新登录会看到旧容器的内容」的那件事。

建议动作（三处措辞对齐，不改语义）：把三处 Scope 行改成与验收行同口径的
「容器可作为背包全量投影与增量投影内容的来源」，并把根契约 `:108-112` 那段延后说明复制一份进
子契约的 Assumptions——子契约是 verification 唯一会读的契约，延后的理由不该只存在于根契约里。

### 非阻塞 2 — 根 routing-decision 的 revision pin 仍停在 1

- 严重度：non-blocking
- 位置：`.rks-agent/tasks/inventory-container-refactor/routing-decision.md:4`

七处 pin 里六处已是 2：根 `TaskContract.md:3`、子 `TaskContract.md:3`、`multi-spec-map.md:5`、
子 `routing-decision.md:4`、`spec.md:5`、`sdd-execution.yaml:5`。只有根 `routing-decision.md:4`
还写着 `Contract revision: 1`，指向一份已经是 revision 2 的根契约。

具体失败场景：下一次经 `using-rks-agent` / `multi-spec-workflow` 进入这个任务时，
根 routing-decision 是用来确认「当前路由是否仍对当前契约有效」的工件。pin 1 对 revision 2
的读法只有一种——路由是针对旧契约作出的，需要重新评估结构。于是在成果一即将封板、
成果二尚未开工的时刻，成果一/成果二的多 Spec 边界决策被无谓地重开一次。

该文件正文无须改动，本轮已逐条核过：SC-2 的重述完全落在成果一内部，两个成果各自独立可验收的
论证（`:38-41`）与 Reroute conditions（`:44-46`）都不受影响——重述的原因**不是**
「容器无法在不改变线上帧的前提下接管投影」（那条已被 `ContainerInventoryProjectionTests` 的
逐字节相等证伪），而是「不新增推送」这一非目标。`:15-16` 的 Execution direction 也仍然成立。
建议动作：仅把 pin 抬到 `2`。

顺带记录两处经核实**不构成** finding 的相邻事项，以免下轮重查：
`multi-spec-map.md:64` 的 SC-2 行已改用新措辞，Progress 仍为 `not_started` 是对的——
子成果尚未被接受，Progress 是控制器在 child 接受时才推进的字段，此刻不该由实施方预填。
`multi-spec-map.md:43` 把角色容器列为「Main / Avatar / PersonalCargo / Pet / AccountCargo」，
而 `InventoryEntryServices.cs:38-45` 的 `CharacterContainerOrder` 实际是
「Main / Avatar / PersonalCargo / Equipment / Pet」（AccountCargo 走 `ProjectAccount`）；
这是 Deferred Follow-up 里的一句散文描述，不参与任何判定，不改。

### 非阻塞 3 — `BindContainers` 在库存里没有 Main 容器时静默不注册

- 严重度：non-blocking
- 位置：`Server/src/Dfo.Server.Application/Inventory/SelectCharacterEntryInventoryDomainService.cs:48-56`

`BindContainers` 先 `containers.Clear(characterId)`（`:47`），再对
`inventory.Containers.Where(value => value.ContainerType == Main)` 迭代注册。库存里没有 Main 时，
`Clear` 照常执行而循环体一次都不进，角色最终处于「什么都没绑」的状态，全程无声。

**判断：应该出声。** 三条理由：

其一，同类不同判。同一个类的 `ResolveCommittedItems`（`:106-107`）面对完全同类的事实——
「重新加载回来的库存缺少某个本该在的容器」——用的是
`?? throw new InvalidDataException($"Committed inventory container {containerType} was not reloaded.")`。
两处的不变量强度相同，处置却一个抛一个吞。

其二，`Where` + `foreach` 表达的是「0 到 n 个 Main」，而真实不变量是「恰好一个」。
它在 0 端静默；在 2 端则更糟：`ContainerId` 由
`$"character:{characterId.Value}:{container.ContainerType}"` 决定（`:52`），两条 Main 会生成
同一个 Id，第二次 `Register` 撞上 `CharacterContainerDomainService.cs:62-66` 的「已注册」异常并
抛在循环中间，留下一个注册了一半的角色。两端都不是这段代码想要的行为。

其三，静默的代价在这里特别高，因为**入场快照不经容器**。绑定失败的角色照样完成入场、
客户端照样画出一个满的背包（快照来自加载路径，与容器无关），只有玩家拖动道具时
`MoveItemSpaceSlice.cs:189-196` 回 `Rejected`、喝药时 `UseStackableSlice.cs:173-180` 回 `InvalidItem`。
这两个错误码同时也是十来种正常拒绝的取值（`MoveItemSpaceSlice.cs:171`、`:216`、`:233`；
`UseStackableSlice.cs:170`、`:200`、`:207`、`:220`），排查会从处理器起步，离真因四层远。
日志里那行 `no bound main container` 是唯一的线索，且只在玩家动手之后才出现，不在入场时。

静默已经在遮东西这件事本身也有证据：`CharacterEntryInventoryTests.EmptyInventory`
（`CharacterEntryInventoryTests.cs:574-583`）产出 `Containers = []`，而生产里
`ProjectCharacter`（`InventoryEntryServices.cs:60-67`）对 `CharacterContainerOrder` 的五种类型恒产出容器，
永不产出空集合。于是 `SelectCharacterEntryLifecycleServiceTests.cs:37` 与 `:81` 两条既有测试
一直在走这条静默路径而无人知晓（`:59` 那条因为自带了 Main 容器所以走的是正常路径）。
R2 非阻塞 3 之所以能存活到第 2 轮才被发现，正是因为这条路径不出声。

**判为非阻塞的依据**：生产不可达。`PrepareLeaseAsync` 的 `inventories.LoadAsync` 只有一个实现
（`ApplicationModule.cs` 里 `ICharacterInventoryEntryDomainService` → `CharacterInventoryEntryDomainService`），
它必过 `ProjectCharacter`，Main 必在。

**建议动作**（改动面已核实，仅涉两条测试）：把 `:48-56` 换成与 `:106-107` 同形的写法——

```
var main = inventory.Containers.SingleOrDefault(value => value.ContainerType == InventoryContainerType.Main)
    ?? throw new InvalidDataException("Entry inventory did not include a main container.");
```

`SingleOrDefault` 同时把 2 端也变响（`InvalidOperationException`），比 `Where` 更贴合真实不变量。
配套只需给 `SelectCharacterEntryLifecycleServiceTests.cs:33` 与 `:78` 两处的 `InventoryLoader`
喂一份带 Main 容器的库存（`:52-55` 那条测试已经是这么写的，照抄即可），
**不要**去动共享的 `EmptyInventory`——它另有 `CharacterEntryInventoryTests.cs:257` 与 `:298`
两处入场测试在用。这两处改动不触碰任何断言、场景或期望值，仍属子契约 `:68` 允许的
「因类型或依赖变化而做的机械调整」。

考虑到本刀的验收面是「行为零变化」，把它留到下一刀也是站得住的；但若留，
建议把「Main 必在」这条不变量至少写进 `BindContainers` 的 `<remarks>`，
使下一个读这段代码的人不必自己去 `ProjectCharacter` 里推。

## Constraint Conformance

- **不得改变任何对外可观察行为，包括线上帧的字节内容与时序**：符合。diff 中没有任何编码器、
  消息类型或发送点的改动；两个处理器仍在原位置返回同样的 `GameExplicitMessageDelivery([response], [])`，
  错误码取值未变。`ContainerSlotItem`（`IItemContainer.cs:92-93`）与 `MainInventoryOccupant`、
  `ContainerMovePlan`（`:122-127`）与 `MainInventoryMovePlan` 字段逐一同名同型，无隐式收窄；
  `PersistentItemContainer.ReadSlotsAsync`（`:66-72`）把 `Source` 映射为 `First`、`Destination`
  映射为 `Second`，`MoveItemSpaceSlice.cs:198-200` 的取用顺序因此与变更前指向同一对槽。
  本轮新增的两道守卫都不改变可观察行为：自指计划在处理器侧构造不出来
  （`MoveItemSpaceSlice.cs:186` 对同槽早返，`moving` 与 `displaced` 取自两个不同的槽，
  唯一索引 `UNIQUE(owner_scope, owner_id, list_type, slot_index)` 保证两槽是两行），
  未知 `ContainerMoveKind` 在只有四个成员的枚举下同样构造不出。
  「无绑定容器」分支不可达（Verified Non-Issues 第 5 条）。
  `ReadMainItemsAsync` 是只读新方法且在 `Server/src` 下只被 `ItemContainers.cs:76` 调用，
  而该调用点（`SnapshotAsync`）本身无生产调用方，动不了任何一个字节。
- **既有测试的语义不得修改**：符合。六个既有测试文件的改动只有三类——构造函数多传一个
  `CharacterContainerDomainService`；为 `IEntryInventoryMutationRepository` 的新成员补一个抛
  `NotSupportedException` 的桩（六处）；以及两个把既有夹具包进 `PersistentItemContainer` 的私有
  `Containers(...)` 辅助方法（`MoveItemSpaceHandlerTests.cs:189`、`UseStackableHandlerTests.cs:166`，
  其中的 `CharacterId(60)` 与两个夹具 `OneSession` 的角色一致）。
  第 3 轮新增的只有 `SelectCharacterEntryLifecycleServiceTests.cs:153-188` 这一条**新**测试
  与同文件两处新桩；没有任何断言、场景、期望值或测试名被改写。
- **`unlimited*` 道具被扣减的缺陷不得在此修复**：符合。`UseStackableSlice.cs:209` 仍然无条件
  `ConsumeAsync(..., 1, ...)`，仍只按 `placement.ItemKind == EquipmentItemKind` 拒绝装备；
  `git diff` 全文检索 `unlimited` 零命中。
- **`Tool/` 与 `.worktrees/legacy-reference/` 不得触碰**：符合。`git status --short -uall`
  只列出 `Server/` 与 `.rks-agent/` 下的条目；本工作区内不存在 `.worktrees/` 目录。
- **既有测试基线全绿**：符合。791 通过 / 2 跳过 / 0 失败（跳过的两条是
  `PvfQuestDefinitionSourceTests` 与 `PvfTownDefinitionSourceTests` 中断言「缺归档时报错」的两条，
  与契约 AC-5 记录的 780 / 2 中的 2 同源）。780 + 11 条新测试 = 791，数字自洽。
- **实机走通教学关（AC-5 后半、AC-009、EV-005）**：三轮审查均未覆盖，属 verification 阶段。

## Verified Non-Issues

三轮累计查证过、确认不构成 finding 的事项，全部记录于此，后续轮次与 verification 无须重查。

1. **`ReadMainItemsAsync` 不做 `ValidateSlot` / `ValidateUniqueSlots` / 归属校验**（R1 非阻塞 2）：
   接受不处理。同一仓储的 `ReadMainSlotAsync` 与 `ReadMainEntryAsync` 同样只按
   `character_id` + `list_type` 过滤，只给新方法加校验会让仓储内部不一致；表上的
   `UNIQUE(owner_scope, owner_id, list_type, slot_index)` 与现有写入方产生不出会分叉的行；
   Swap 寄存的 `slot_index = -1` 不跨事务边界。判为纵深防御上的落差而非活着的缺陷。
2. **容器快照不带 `AvatarDetail` 会不会让编码抛异常**：不会。
   `WriteInventoryItem`（`StateSynchronizationPayloadCodec.Inventory.cs:199`）在 `ItemKind == 8`
   且无 detail 时抛异常，而 `ReadMainItemsAsync` 恒不填 `AvatarDetail`；但按
   `Docs/GM工具_新背包表结构与ItemCore语义.md` 的槽位边界表，时装（`KindAvatar` = 8）属
   `list_type` 1 与 3，主背包 `list_type=0` 的 289-351 是徽章栏（`KindAvatarEmblem` = 9），
   而 `character_avatar_detail` 只服务 `ItemKind == 8`。主背包的合法内容里不含 kind 8。
   （若将来允许时装落入 `list_type=0`，`ReadMainItemsAsync` 必须同时读 `character_avatar_detail`，
   否则容器路径会抛而加载路径不会。）
3. **排序不可观察**：`EncodeInventorySnapshot` 自己
   `OrderBy(value => value.SlotIndex)`（`StateSynchronizationPayloadCodec.Inventory.cs:116`），
   而主背包槽位在上述唯一索引下唯一，稳定排序的结果被完全决定。这解释了为何删掉
   `ReadMainItemsAsync` 的 `ORDER BY slot_index` 不会让 `ContainerInventoryProjectionTests` 失败、
   而删掉 `AND list_type=0` 会（`:54` 的字节相等与 `:55` 的 `Items.Count == 3` 会同时失败）——
   不是测试无力，是那一维本来就不进帧。
4. **守卫 `Clear` 的 check-then-act 窗口**（`SelectCharacterLifecycleServices.cs:130-135`）：不可达。
   同一角色只属于一个账号；选角（`SelectCharacterHandler.cs:75`）与断线清理
   （`OnlineUserConnectionObserver.cs:83`）都在该账号上取 `lifecycleGate` 的排他租约，二者互斥；
   跨账号抢同一角色被 `SelectedSessionDirectory.RegisterReplacing`（`:18-27`）拒绝。
   `displaced` 的清理发生在 `RegisterReplacing` 之后，`FindByCharacter` 会命中新会话因而跳过 `Clear`；
   同连接重选则先 `Clear`、再由 `BindContainers` 重建。该守卫包在 `Run(...)`（`:141-145`）内，
   与其余七个清理步骤同样吞异常并计入 `failures`，不改变清理的通知计划。
5. **「无绑定容器」新分支不可达**：`InventoryEntryProjection.ProjectCharacter`
   （`InventoryEntryServices.cs:60-67`）对 `CharacterContainerOrder` 里的五种类型恒产出容器
   （缺参数时取 `GetDefaultParameter`），因此生产中 `BindContainers` 必定注册恰好一个 Main 容器；
   会话进目录（`SelectCharacterPhaseCommands.cs:93`）与 `PrepareLeaseAsync`（`:118`）之间的窗口
   只对同连接的后续帧开放，而同连接的帧串行在选角处理器之后。
   （测试夹具能产出没有 Main 的库存，那是本轮非阻塞 3 的题目，与生产可达性无关。）
6. **临时容器的锁**：锁内无 `await`，无跨锁调用，`Place` 与 `Apply` 同在一把锁内，
   `SnapshotAsync` 返回 `ToArray` 出来的快照而非活列表。不构成并发缺陷。
7. **等价脚本仍在比同类**：两个容器的初始内容一致（65 槽 8474×5、66 槽 9001×3）；
   持久侧种子行的 `Marker16` 为 0 而 `Place` 建出的记录为 -1，但脚本的观测（`Describe` 与 `final` 行）
   只取槽位、种类、ID、数量，`Marker16` 不进入比较，而 Split 新建的堆两侧又都走 -1。
   逐步推演两条脚本的状态序列完全一致，包括 Swap 与三条拒绝（`stale False overdrawn False self False`）。
8. **`IItemContainer.ApplyMoveAsync` 新文档「False, never an exception」是否被实现违反**（第 3 轮查证）：
   实质不违反。唯一能从该方法抛出的非参数异常是 Merge 的加法溢出——而两侧都是
   `checked`（`ItemContainers.cs:235`、`SqliteEntryInventoryMutationRepository.cs:370`），
   行为对称，RQ-008 的等价性不因此破裂；且处理器侧的合并受 `placement.StackLimit` 约束
   （`MoveItemSpaceSlice.cs:288`），归档给出的堆叠上限离 `int` 边界极远，构造不出来。
   `ArgumentNullException.ThrowIfNull(plan)` 针对的是空引用而非「不肯执行的计划」，与该句不冲突。

## Requirement Coverage

| ID | 结论 | 依据 |
|---|---|---|
| RQ-001 | 满足 | 两个处理器的构造依赖改为 `ICharacterContainerDomainService`（`MoveItemSpaceSlice.cs:140`、`UseStackableSlice.cs:137`），道具存取全部经 `IItemContainer`；`CharacterContainerDomainServiceTests.cs:107` 以反射断言构造参数中不含 `IEntryInventoryMutationRepository` 且含 `ICharacterContainerDomainService`（AC-001 / EV-001） |
| RQ-002 | 满足 | `IItemContainer` 暴露 `Id` / `Type` / `Shape` / `IsRemovable`（`IItemContainer.cs:43-57`）；`CharacterContainerDomainServiceTests.cs:23` 四项逐一断言（AC-002 / EV-001） |
| RQ-003 | 满足 | 同类型多容器由 `registered` 承载、当前绑定由 `current` 按类型唯一（`CharacterContainerDomainService.cs:52-54`）；`CharacterContainerDomainServiceTests.cs:35` 覆盖（AC-003 / EV-002） |
| RQ-004 | 满足 | `Register` 仅在 `!current.ContainsKey(container.Type)` 时设为当前（`CharacterContainerDomainService.cs:69-70`），同一测试断言首个注册者为当前（AC-003 / EV-002） |
| RQ-005 | 满足 | `GetContainers` / `UseContainer`（`CharacterContainerDomainService.cs:29-31`、`:41-42`）；`CharacterContainerDomainServiceTests.cs:50` 覆盖切换后当前变更而集合不变（AC-004 / EV-002） |
| RQ-006 | 满足 | 回落目标在任何移除动作之前取得（`CharacterContainerDomainService.cs:99-100`），取该类型下第一个 `!IsRemovable`；`CharacterContainerDomainServiceTests.cs:70` 覆盖（AC-005 / EV-002） |
| RQ-007 | 满足 | 无回落目标时抛 `InvalidOperationException` 且抛在移除之前（`CharacterContainerDomainService.cs:103-106`）；`CharacterContainerDomainServiceTests.cs:90` 用一个可移除容器走到这条路径，并断言绑定与集合都未被破坏（AC-006 / EV-002）。生产中角色唯一的容器 `IsRemovable == false`，会先命中 `:92` 那条异常；两条路径都已写进 `ICharacterContainerDomainService.cs:36-40` |
| RQ-008 | 满足 | 两种容器并存且使用方只见 `IItemContainer`；`ItemContainerEquivalenceTests.cs:27` 的同一脚本覆盖 Relocate、Split、Swap、Merge、Consume 与三条拒绝（陈旧 displaced、超额 MoveCount、自指计划），本轮逐步推演确认两侧状态序列一致（AC-007 / EV-003）。第 2 轮的两项保留——未知种类的应答不一致、自指计划的三种分歧——本轮确认均已消除，两个容器现在对这两类输入都答 `false` |
| RQ-009 | 满足 | `IItemContainerFactory.CreateTransient`（`IItemContainer.cs:141`）与 `ItemContainerFactory`（`ItemContainers.cs:310-314`），已在 `ApplicationModule.cs` 注册；`ItemContainerEquivalenceTests.cs:37` 按持久容器的 `Shape` 建临时容器，`:49` 断言其内容不进入持久存储（AC-007 / EV-003） |
| RQ-010 | 满足 | 契约与 spec 的口径本轮已一致：交付的是「当前容器可作为投影内容的来源」这一能力，不含推送路径的切换。`ContainerInventoryProjectionTests` 在真实 SQLite 上比对两条独立读取路径（`ReadMainItemsAsync` 与 `LoadCharacterAsync` + `ProjectCharacter`，二者唯一共享的是 `DecodeItemCore` 与编码器本身），全量帧与增量帧各一条，种子行故意乱序（121、3、65）且含一行 `list_type=2`；`BindContainers` 那一段由 `SelectCharacterEntryLifecycleServiceTests.cs:162` 单独覆盖，两半的容器标识与形状同源（AC-008 / EV-004）。**尚未交付、且已在根契约 `:108-112` 与 `multi-spec-map.md:39-51` 中记录归属的部分**：生产路径仍由加载路径供给入场快照，`SnapshotAsync` 无生产调用方，真正切换留待多容器切换那一刀 |
| RQ-011 | 满足 | 迁移为纯类型改名，字段与分支逐一对应，两道新守卫在处理器侧均构造不出对应输入，唯一新增分支不可达；六个既有测试文件语义未变，全套 791 / 2 / 0（AC-009 的实机部分留待 verification） |

## Outstanding Recommendations

三条均为非阻塞，不构成本阶段的收敛前置；按归属列出，供控制器分派。

| # | 动作 | 归属 |
|---|---|---|
| 1 | 三处 Scope 行（根 `TaskContract.md:57`、子 `TaskContract.md:51`、`spec.md:40`）改为与验收行同口径的「可作为来源」；并把根契约 `:108-112` 的延后说明复制一份进子契约 Assumptions | 文档修订，宜在 verification 之前完成，无须再推进 revision（语义未变，仅措辞对齐） |
| 2 | 根 `routing-decision.md:4` 的 pin 由 `1` 抬到 `2`，正文不动 | 同上 |
| 3 | `SelectCharacterEntryInventoryDomainService.cs:48-56` 改用 `SingleOrDefault` + `InvalidDataException`，配套给 `SelectCharacterEntryLifecycleServiceTests.cs:33` 与 `:78` 的 `InventoryLoader` 喂带 Main 的库存 | 代码改动。本刀的验收面是「行为零变化」，留到下一刀亦可；若留，至少把「Main 必在」这条不变量写进 `BindContainers` 的 `<remarks>` |
