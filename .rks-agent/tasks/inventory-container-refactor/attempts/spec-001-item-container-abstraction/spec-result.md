---
kind: rks-spec-result
outcome: completed
contract:
  path: ./TaskContract.md
  revision: 2
---

# Spec Result: 容器抽象与角色绑定（行为零变化）

## Outcome

道具的存取不再由协议处理器直接打向持久化仓储，而是经由一个可替换的容器完成；
角色对「每种类型当前用哪个容器」有显式绑定。对外可观察行为一字未变。

后续需要一份不同的道具存储时——副本临时背包是最近的一个——只需提供一个新的容器实现，
既有的移动与使用规则原样复用。参考实现在同一位置没有设这层接缝，代价是为死亡之塔另写了
857 行把整套规则复制一遍；这次的接缝在只有两个调用点时建立，成本是本文件记录的这些。

## What Was Delivered

新增抽象（`Dfo.Server.Abstractions/Inventory/`）：

- `IItemContainer` 自述 `Id` / `Type` / `Shape`（容器类型与客户端可见容量）/ `IsRemovable`，
  提供按槽读、成对读、占用槽枚举，以及移动与消耗两个复合操作。复合操作放在容器上，
  因为只有容器知道如何把一次位移做成一个原子变更。
- `ICharacterContainerDomainService` 管理角色维度的注册与绑定：注册、移除、按类型列出、
  按类型取当前、切换当前、清空。
- `IItemContainerFactory` 按既有容器的形状创建非持久容器。

新增实现（`Dfo.Server.Application/Inventory/`）：

- `PersistentItemContainer` 包住既有仓储，`IsRemovable` 为 false——它是角色的回落目标。
- `TransientItemContainer` 只活在本次运行内，与持久容器施行同一套
  Relocate / Split / Swap / Merge / Consume 规则与同样的拒绝条件。
- `StackableItemCore` 把「拆分时写出的堆叠核心」这一形状收拢到一处，使两种容器写出的行相同。
- `CharacterContainerDomainService`：首个注册者成为该类型的当前容器；移除当前容器时回落到
  该类型首个不可移除容器；无可回落容器时抛异常，且抛出前不改动任何状态。

迁移与接线：

- `MoveItemSpaceHandler` 与 `UseStackableHandler` 改为经 `ICharacterContainerDomainService` 取容器。
- `SelectCharacterEntryInventoryDomainService.BindContainers` 在入场时把角色绑定到其持久 Main 容器，
  形状取自刚加载的背包（容量是这个角色的事实，不是容器类型的）；加载结果缺少 Main 容器时报错，
  不静默放过。
- `SelectedSessionCleanupService` 在会话目录条目移除后清理注册，并先确认该角色没有别的活动会话，
  避免清掉一个刚重新登录的角色的绑定。
- `IEntryInventoryMutationRepository` 增加 `ReadMainItemsAsync`，供容器投影用。

## Evidence

- 自动化：**791 通过 / 2 跳过 / 0 失败**（基线 780/2/0 加新增 11 个用例）。
- 实机：2026-08-18 在教学关走通拖拽与使用，服务端日志见 `verification/completion-evidence.md`。
- 评审：三轮独立 subagent 评审，记录见 `reviews/change-review.md`，最终 `approved`。

## What Review Changed

三轮评审各抓到一件真问题，都不是靠测试全绿能发现的：

1. **投影测试是恒真断言**。原测试用一个返回「期望值本身」的假仓储，断言等价于
   `Encode(x) == Encode(x)`。重写为在真实 SQLite 上比对容器与入场加载路径两条独立读取的编码字节，
   并以变异验证：去掉 `AND list_type=0` 该用例会红。
2. **`(MainInventoryMoveKind)plan.Kind` 是按序数强转**。两个枚举分开声明，任一加成员就会把
   swap 静默变成 merge——删掉一件道具，且没有任何东西会失败。改为显式映射，未知种类返回拒绝
   而非抛出，与内存容器保持一致。
3. **`BindContainers` 静默空转**。入场是唯一建立绑定的地方，而入场快照并不走容器，
   所以一个没绑上容器的角色会看到满满一背包、每次拖拽都被拒，错误码还与十几种正常拒绝共用。
   改为显式报错。

## Boundary Changes Made During Execution

- **RQ-010 与 SC-2 / AC-2 重述**。现存的两处背包投影（选角入场的五份初始快照、每日补给的增量）
  都在非目标内，而两条已迁移的协议只回 CMD 应答；此时新增一次推送本身就是行为变更，为本 Spec 所禁止。
  因此交付的是「容器可作为投影内容的来源」这一能力，并以其与入场加载路径逐字节一致为证。
  根契约与子契约据此升至 revision 2，由用户于 2026-08-18 授权。
- 「投影真正由容器驱动」与「入场快照改由容器供给」记入 `../../multi-spec-map.md` 的
  `## Deferred Follow-up`，随多容器切换的持久化结构与触发协议一并落地。

## Known Gaps Left Open

- 只有 Main 类型注册了持久容器。其余四种容器类型（Avatar / PersonalCargo / Pet / AccountCargo）
  在既有仓储上没有对应的按槽读写能力，也没有任何协议路径需要它们；注册一个背后无物的容器
  比不注册更坏。
- `ReadMainItemsAsync` 未复制入场加载路径的 `ValidateSlot` / `ValidateUniqueSlots` / owner scope 校验。
  同一仓储上的 `ReadMainSlotAsync` / `ReadMainEntryAsync` 同样没有，单给新方法加会让仓储内部不一致；
  且其差异只在 schema 的唯一键与写入路径都产生不了的数据上才显现。评审判定可接受。
- 已知缺陷「`unlimited*` 道具被扣减」按约束未在本次修复，属根任务成果二。
