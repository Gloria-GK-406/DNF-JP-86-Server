---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: Item rules in the bag

## Delivered

- 背包实例获得三个领域动词——放入、补充、移动——各自在内部查规则、作判定、执行，并把
  「落在哪里、放了多少、或者为什么没放」交回调用方。
- 「把到货判决变成写入」的那一段在整个代码库中只剩一处（`ItemContainers.LandAsync`），
  此前教程奖励与每日补充各写过一份。
- `Dfo.Server.Protocol` 不再持有物品规则：约 50 行判定从 `MoveItemSpaceCommandHandler.PlanAsync`
  迁入背包，该工程对摆放、槽位、到货三类规则服务的引用归零。
- 三处调用方各自的失败语义原样保留：每日补充遇满仍抛并带回整个入场，教程奖励遇满仍静默跳过
  并把该件排除在回执之外，移动遇拒仍返回各自的客户端码。
- 两处与设计裁定相反的既有注释已重写；`ICharacterItemTable` 上四个失去最后调用方的成员
  与 `MainStackRow` 记录已删除。
- 设计文档新增 R-7 与一节落地记录。

## Success Evidence

| Condition | Evidence |
|---|---|
| SC-A1 | 全仓 `case ItemArrival.` 在生产代码中共 3 处，同属一个 `LandAsync`；见 `verification/completion-evidence.md` |
| SC-A2 | `Dfo.Server.Protocol` 对三类规则服务零引用（搜索证明，两轮评审各自独立复核） |
| SC-A3 | 三处调用方成功与失败路径各有用例；两种「装不下」反应各自独立且各自可证伪（D4、D5） |
| SC-A4 | 五种拒绝与两种非拒绝逐条可构造；其中半栈交换与拆分为本轮新补，此前全仓无人构造 |
| SC-A5 | `DailyRefill_LeavesTheProjectionAloneEvenIfTheRowsSurrogateKeyMoves` |
| SC-A6 | 构建零错误零警告；`dotnet test Server/86JP.Server.slnx -m:1` = 1151 / 2 跳过 / 0 失败 |
| SC-A7 | 两处注释已重写，全仓已无指向旧划分的说法；`Docs/Refactor/DomainInstanceDesign.md` 已修订 |
| SC-A8 | 九次可证伪演示，全部先红后绿并逐字节复原；见 `verification/completion-evidence.md` |

## Repository Result

- Changed paths：26 个（生产 8、测试 15、文档 1、流程记录若干），全部属于本尝试。
- Baseline：`531c0bbd8a35fd579c3add6e478965307bd083d4`，尝试期间未变。
- Parent rollback required：no。

## Successor Guidance

- **R-7 是本成果发现并写入设计文档的新规则**：一个动词必须在返回前把它决定的写入应用出去。
  存储把一次用例的写入暂存到该用例结算，期间只对走同一 session 的读可见；同一用例里
  经表通道读同一批行的一切——入场装载器，以及容器自己那个取自装载器的快照——都会读到写之前的状态。
  后续每一个新动词都继承这个陷阱。
- **能掩盖它的测试形状很寻常**：给每次调用各包一个用例，两条写通道就不可区分。
  任何验证「写完再读」的用例必须用**真实命令的事务形状**，即一个用例内先写后读。
- `ItemContainerOperations.ConsumeAsync` 是结构上的复合动作但不是动词，不在 R-7 覆盖内。
  今天没有调用方在同一用例内把它读回来。把它变成动词的工作，属于按 R-6 拆解物品使用的那个成果。
- 每日补充的「规则物品类别为 0 不得开格」现在是**背包**的不变量，不再是补充的；异常类型、时机与
  效果不变，消息文本是背包的。
- `IItemStoreSession` 新增 `ApplyDecidedWritesAsync`。它不结算、不放宽隔离，只让同一用例内的其他
  读者看到本用例已决定的写入。
- 移动仍只服务主列表。跨容器移动所需的第二条槽位带，应加在规则服务里而不是在背包里分支。

## Artifacts

- `spec.md`、`plan.md`
- `reviews/change-review.md`（两轮，第一轮 `changes_required`，第二轮 `passed`）
- `verification/completion-evidence.md`
- `Docs/Refactor/DomainInstanceDesign.md`（R-7 与落地记录）
