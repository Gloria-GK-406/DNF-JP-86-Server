---
kind: rks-completion-evidence
verdict: pass
spec: ../spec.md
contract: ../TaskContract.md
---

# Completion evidence: Item rules in the bag

证据取自整改后的工作树状态，即将被接受的那一份。

## 基线事实

| 事实 | 结果 |
|---|---|
| 构建 | `dotnet build Server/86JP.Server.slnx` **零错误零警告** |
| 全量 | `dotnet test Server/86JP.Server.slnx -m:1` = **1151 通过 / 2 跳过 / 0 失败** |
| 起点 | main @ `531c0bb` = 1132 通过 / 2 跳过 / 0 失败 |
| 净增 | **+19 条**，逐条可解释（见下） |
| 环境 | `PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` = `D:/project/DNF/DNFClient-debug/Script.pvf` |
| 已知假红 | 本次最终运行 `Dfo.Server.Host.Tests` 未触发（4/4 通过）；过程中触发过一次，隔离复跑 4/4 |

新增的十九条：教程奖励落位七条；每日补充代理键一条、无物品类别一条、同一 use case 内可见性两条（`[Theory]`）；
背包动词五条；移动的半栈交换一条、拆分一条；移动在同一 use case 内可见一条。

## 成功条件

| ID | 结果 | 证据 |
|---|---|---|
| SC-A1 | 成立 | 全仓 `case ItemArrival.` 在生产代码中共 3 处，全部在 `ItemContainers.cs` 的同一个 `LandAsync` 内（三种到货各一支）。教程奖励与每日补充两条路都经由它 |
| SC-A2 | 成立 | `Dfo.Server.Protocol` 对摆放、槽位、到货三类规则服务的引用为**零**。`MoveItemSpaceCommandHandler` 只剩：主列表判断、取背包、转发、把领域拒绝翻成 `ItemMoveStatus` |
| SC-A3 | 成立 | 三处调用方各自的成功与失败路径各有用例。两种「装不下」反应各自独立：教程奖励排除在回执外（`An_item_whose_band_is_full_is_left_out_and_the_rest_of_the_step_proceeds`）、每日补充抛出（`DailyRefill_RefusesWhenTheBandIsFull`）。第一轮评审曾判此条不成立（见 A-1），整改后第二轮判成立 |
| SC-A4 | 成立 | 五种拒绝逐条可构造：带外槽位、两端皆空、并堆超限、被顶开那端无处可去、**半栈交换**（本轮新补，此前全仓无人构造）。两种非拒绝亦可构造：搬成了（含拆分，本轮新补）、两端同格 |
| SC-A5 | 成立 | `DailyRefill_LeavesTheProjectionAloneEvenIfTheRowsSurrogateKeyMoves`：补充后代理键确实改变（断言 `NotEqual`）而投影三项不变，并断言槽位在容器内唯一。协议侧 `StorageEntryId` 只出现在异常消息字符串中，两位评审各自独立复核过 |
| SC-A6 | 成立 | 见上表 |
| SC-A7 | 成立 | 两处反向注释已重写：`IItemContainer` 的接口注释、`ItemContainers.cs` 中 `ItemContainerOperations` 的注释。全仓已无「拼装属于上层」「是协议的问题」这类指向旧划分的说法。设计文档新增 R-7 与「Settled by the item-side implementation」一节 |
| SC-A8 | 成立 | 九次可证伪演示，见下 |

## 验收条目

AC-1 至 AC-5、AC-7 至 AC-10 逐条成立，证据同上表。AC-6 在第一轮判不成立（A-1），
整改并新增守卫后第二轮判成立。

## 可证伪演示

九次，每次都以 sha256 前后比对证明逐字节复原。

| # | 改坏了什么 | 变红的 |
|---|---|---|
| D1 | 续堆时把「这次放进多少」报成「共有多少」 | 7 条（背包动词、教程奖励四条、每日补充两条）——共用落地步骤，多条同时注意到是应有之义 |
| D2 | 补充时无视已有的堆 | 5 条（含一条 Host 已知假红） |
| D3 | 允许半栈交换 | **第一次运行什么都没红**——暴露该拒绝全仓无人构造；补测后重跑，只红 `Dragging_part_of_a_pile_onto_another_item_will_not_swap` |
| D4 | 教程奖励把「装不下」也报进回执 | 只红 `An_item_whose_band_is_full_is_left_out_and_the_rest_of_the_step_proceeds` |
| D5 | 每日补充吞掉「装不下」 | 只红 `DailyRefill_RefusesWhenTheBandIsFull` |
| D6 | 去掉「无物品类别不得开格」 | 只红 `DailyRefill_RefusesToOpenASlotForARuleWithNoItemKindButStillJoinsAPile` |
| D7 | 放入动词不再应用已决定的写入 | 红新守卫（第二轮评审复核为两例皆红，比自查更强） |
| D8 | 拆分一律当成整搬 | 只红 `Dragging_part_of_a_pile_onto_an_empty_slot_leaves_the_rest_behind` |
| D9 | 移动动词不再应用已决定的写入 | 只红 `AMoveIsVisibleToTheTablesInsideTheSameUseCase` |

## 约束

| 约束 | 结果 |
|---|---|
| C-1 三处调用方对外行为不变 | 成立（整改后）。A-1 曾使每日补充这一条不成立，已修并加守卫 |
| C-2 三份整记录表征用例期望值不改 | 成立：三份文件 `git diff` 为空 |
| C-3 `ItemContainerOperations` 不判定 | 成立：它仍只由背包的动词调用，自身不作任何判断 |
| C-4 不开 `canX` 并行查询 | 成立 |
| C-5 不改持久层形状、背包不自持数据库 | 成立：仍 43 张表（`CREATE TABLE IF NOT EXISTS` 计 44，其中一处在 XML 注释内，与既有裁定一致）；背包经既有按表类与既有环境事务落地 |
| C-6 accept 时绿 | 成立 |
| C-7 删测试文件前逐条核对 | 不适用：未删除任何测试文件。删除的四个持久化成员与 `MainStackRow` 经两位评审各自独立确认全仓零调用方、零守卫 |
| C-8 项目既有约束 | 成立：C# 文件 CRLF、无 BOM，扫描无异常；未改动 `Tool/` 与 `.worktrees/legacy-reference/` |

## 评审

两轮，均在全新上下文中独立进行。

- **第一轮**：`changes_required`。一条 A 级——每日补充改走背包后，写入暂存到事务 prepare，
  而同一事务内的库存重载走表通道，读不到。新开格子的日子整个选人入场回滚，并堆的日子下发陈旧数量。
  全量当时全绿，因为所有 refill 测试都以「每调用一个事务」的形状驱动，那种形状下两条写通道不可区分。
  评审者自写四条探针把机制与后果都实测出来，另做十五次故意改坏。
- **第二轮**：`passed`。独立重做探针确认 A-1 从根上修好；确认 R-7 落在背包动词里是对的
  （`SnapshotAsync` 也走表通道，改在调用方覆盖不到）；逐一排除了整改自身可能引入的三处风险
  （清空 staged 后事务仍正确提交、回滚仍完整、二次 flush 既不重放也不漏写）。
  新提四条 B 级，其中 B2-1、B2-3、B2-4 已在本轮闭合，B2-2 已写入设计文档作为已知未覆盖面。

## 未解决且已记录

- `ItemContainerOperations.ConsumeAsync` 是结构上的第四个复合动作但不是动词，因此不在 R-7 覆盖内。
  今天没有调用方在同一 use case 内把它读回来，所以是陷阱而非缺陷。已写入
  `Docs/Refactor/DomainInstanceDesign.md` 的 R-7 一节，交给按 R-6 拆解物品使用的那个成果。
- 两轮评审共留下若干 B 级观察记录在 `reviews/change-review.md`，均判为不阻塞。
