---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to: [SC-2, SC-3, SC-8, SC-9, SC-10]
depends_on: []
---

# TaskContract: Item rules in the bag

## Context

### Source Request

父控制器 `.rks-agent/tasks/domain-instances-and-snapshots/multi-spec-map.md` 在 `child_modeling`
阶段派生的第一个子成果。根授权：「既然你有更好的想法，那就按你的跑吧。重构结束后再找我」。

### Current Situation

背包实例（`IItemContainer`，唯一实现 `PersistentItemContainer`）今天只有读和写：
`ReadSlotAsync` / `ReadSlotsAsync` / `ReadAllAsync` / `SnapshotAsync` / `PutAsync` / `ClearAsync`。
复合动作由 `ItemContainerOperations` 以静态函数拼出，**决定允不允许则在调用方**。

后果有二，都已核实：

1. **「到货的东西放哪个格子」写了两遍。** `ItemArrival` 的三分支落地出现在
   `Dfo.Server.Protocol/Game/TaggedIdReport/GrantTutorialRewardCommand.cs:70` 与
   `Dfo.Server.Application/Inventory/DailyRefillService.cs:105`。两处的到货规则不同
   （`ForGrant` 认叠加上限并溢出到新格，`ForDailyRefill` 并入任一堆且上限已在上游算过），
   失败语义也不同（教程奖励静默跳过，每日补充直接抛），但**把到货判决变成写入**这一段是同一段。
2. **判定物品能否移动的规则在协议工程里。**
   `MoveItemSpaceCommandHandler.PlanAsync`（`MoveItemSpaceSlice.cs:313`，约 50 行）持有摆放查表、
   槽位带、叠加上限、半栈禁止交换、被顶开那件自己的槽位带。

`Docs/Refactor/DomainInstanceDesign.md` 已裁定这两条应当收回实例内部。

两处与该裁定相反的既有注释必须一并订正，否则下一个读者会照着注释把规则搬回去：

- `Dfo.Server.Abstractions/Inventory/IItemContainer.cs` 接口注释（约 :78-93）写着「读和写就是全部……
  拼装属于上层」，并给出理由「提供 move 的容器将不得不被告知 move 是什么，其下的存储也一样」。
  后半句在今天已不成立：`ItemContainerOperations` 正是在不告知存储的前提下拼出复合动作的。
- `Dfo.Server.Application/Inventory/ItemContainers.cs:51` 写着那些问题「是协议的问题，在这些被调用之前
  就已回答」。前半句将不再成立。

## Goal

背包实例成为物品规则的所在地：它对外提供「放入」「补充」「移动」三个领域动词，各自内部调用注入的
规则服务作出判定并执行，并把「发生了什么或为什么没发生」交回调用方。到货判决的落地在整个代码库中
只剩一处。对外协议行为一字不变。

## Scope

### In

- `IItemContainer` 增加领域动词：放入（grant 语义）、补充（refill 语义）、移动（同一容器内）。
- 容器获得所需的只读规则依赖（摆放、槽位、到货）。
- `GrantTutorialRewardCommandHandler`、`DailyRefillService` 的授予路径、`MoveItemSpaceCommandHandler`
  三处改为调用背包动词。
- 各调用方**保留各自现有的失败语义**：背包只报告结果，是否致命由调用方决定。
- 两处反向注释订正；`Docs/Refactor/DomainInstanceDesign.md` 相应修订。
- 随上述改动而来的测试改写与新增。

### Out

- 跨容器移动。`MOVE_ITEMSPACE` 的非主列表仍按今天拒绝。
- 人物实例、规则服务的身份拆分、快照重塑。
- 客户端错误码及其翻译。
- `ItemContainerOperations` 的判定职责：它继续不判断，只作为背包内部的拼装。

## Constraints

- **C-1** 三处调用方的对外行为一字不变，含各自的失败语义：每日补充遇到装不下仍抛，
  教程奖励遇到装不下仍静默跳过并把该件排除在回执之外。
- **C-2** 三份整记录表征用例的期望值不得修改。
- **C-3** `ItemContainerOperations` 保持不作任何判定。
- **C-4** 不得为 `AddAsync` / `MoveToAsync` 另开 `canX` 并行查询。
- **C-5** 不得改动持久层形状，不得让容器自建表或自持数据库。
- **C-6** accept 时构建零警告、全量测试通过。
- **C-7** 删除任何测试文件前逐条核对其断言证的是不是即将消失的东西。
- **C-8** 沿用根契约 C-7 的既有项目约束（CRLF、无 BOM、`-m:1`、PVF 路径、不动 `Tool/` 与
  `.worktrees/legacy-reference/`、擦库不迁移、不产生任务级提交）。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-A1 | 把到货判决变成写入的那一段，在整个代码库中只出现一次 | 全仓搜索 `ItemArrival` 的分支处理，生产代码中只有一处；教程奖励与每日补充两条路都经由它 |
| SC-A2 | 协议工程不再持有物品规则 | `Dfo.Server.Protocol` 不再引用摆放、槽位、到货三类规则服务；`MoveItemSpaceCommandHandler` 只剩转发与结果翻译 |
| SC-A3 | 三处调用方的对外行为逐条不变 | 每处各有用例证明其成功路径与失败路径与改动前一致，含两种不同的「装不下」反应 |
| SC-A4 | 移动的判定逐条保留 | `ItemMoveStatus` 的每个拒绝分支仍可被构造出来，且构造方式与今天等价 |
| SC-A5 | 每日补充改走容器后代理键变化不入帧这一事实被钉住 | 一条用例证明补充后的行代理键可以改变而下发投影不变；并指出代理键在协议侧只出现在异常字符串与一个因槽位唯一而不可触发的排序备选键 |
| SC-A6 | 结果是绿的 | `dotnet build Server/86JP.Server.slnx` 零错误零警告；`dotnet test Server/86JP.Server.slnx -m:1` 全量通过 |
| SC-A7 | 不留反向指引 | 两处注释已按新裁定重写；设计文档中被本次落地推翻或细化的条目已修订 |
| SC-A8 | 可证伪 | 对本成果新建立的每一处保证，各演示一次「故意改坏使其变红、复原后变绿」 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 根契约 `task` 行；父控制器在 `child_modeling` 判定本成果落在其授权范围内 | 在共享任务工作树内实施本成果；经评审与新鲜验证后由父控制器做一次已接受结果提交 |

## Assumptions

- 每日补充运行时背包已经绑定。已核实：`PrepareSelectedCharacterSessionCommand`
  （`SelectCharacterHandler.cs:80`）内的 `PrepareLeaseAsync` 调用 `BindContainers`，
  而 `ApplySelectedCharacterDailyRefillCommand` 在 `:246`，晚于它。
- 行代理键不入下发帧。已核实：`StorageEntryId` 在 `Dfo.Server.Protocol` 中只出现在异常消息字符串里；
  另有 `InventoryEntryServices.cs:167`、`:184` 用它作排序备选键，但主键是槽位，而槽位在一个容器内唯一
  （`UNIQUE(owner_scope, owner_id, list_type, slot_index)`），故该备选键不可触发。此假设由 SC-A5 钉住。
