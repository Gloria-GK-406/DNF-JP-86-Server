---
kind: rks-verification-evidence
spec: ./../spec.md
contract: ./../TaskContract.md
change_review: ./../reviews/change-review.md
state: passed
baseline: ea95d491fd9d706bf4f7bc7e1b465040081223ef
---

# spec-001 完成验证

评审通过、评审整改全部落地之后重跑，不复用整改之前的任何数。

## 一、新鲜证据

| 项 | 命令 | 结果 |
|---|---|---|
| 构建 | `dotnet build Server/86JP.Server.slnx --no-incremental` | 0 错误 0 警告（`TreatWarningsAsErrors` 打开）|
| 全量 | `dotnet test Server/86JP.Server.slnx -m:1` | **1037 通过 / 2 跳过 / 0 失败** |
| 假红复核 | 隔离复跑 `Dfo.Server.Host.Tests` | 4/4 通过，8 秒 |

满载全量里 `ChannelHostStartupTests.InvalidRequiredChannelScript_FailsBeforeListenersStart(kind: "unreadable")`
撞 10 秒进程退出预算，报 `TaskCanceledException`。这是 `AGENTS.md` 记载的已知假红形状，
按其判据隔离复跑该项目：4/4 通过。故计为 0 失败。

分项：Pvf 60、Application 499、Host 4、Infrastructure 99（+2 跳过）、Networking 46、
Persistence 126、Protocol 203。

基线 1011 / 2 / 0，增量 +26：其中 24 条是八个任务单各自新增的用例（构成见 TASK-08），
另 2 条是评审整改新增的回归用例（CR-1、CR-2 各一条）。无用例被删除。

## 二、TaskContract 成功条件

| SC | 条件 | 结论 | 证据 |
|---|---|---|---|
| SC-1 | 每个仓储的读写不越出单张表 | 满足 | `Server/src/Dfo.Server.Persistence/` 下全部 65 个 `internal sealed class` 逐类清点，提及 >1 张表的为 0（基线上九个仓储分别跨 34/14/3/…张）|
| SC-2 | 持久化实现不自开连接 | 满足 | `new SqliteConnection(` 恰 3 处，全在 `Database/` 与 `Schema/`；`BeginTransaction` 恰 2 处，均在参与者内。58 个表仓储中 0 处（基线为用例边界外 26 处）|
| SC-3 | 一次用例内的多次读看到同一份快照 | 满足 | `Persistence.Tests/Database/UseCaseTransactionCoversReadsTests` 7 条，含「外部连接在两次读之间提交，用例仍看见旧值」与「只读用例结束后放开对库的视图与连接」|
| SC-4 | 一次用例内的多处改动全有或全无 | 满足 | `Persistence.Tests/Database/OneUseCaseSettlesEveryTableTests` 3 条；且 ProtocolModule 19 条命令逐条核实，能触达持久写的都在边界内 |
| SC-5 | 跨表组装归领域层且只有一处 | 满足 | 三个入场记录各只有一处构造，全在 `Dfo.Server.Application`；`Server/src/Dfo.Server.Persistence/` 内零命中 |
| SC-7 | 对外可观察行为一字未变 | 满足 | 协议 203/203、改动行中断言 0 条、全树字节字面量与基线一字节不差；三份整记录金样本（人物入场 33 表、账号入场 8 表、背包 12 表）在搬家前捕获、搬家后原样通过 |

SC-6（退役内存载体）属 spec-002，本次不涉及。

## 三、本次接受的两处行为变化

两处都不是对外可观察行为，都已在代码注释与任务记录里写明：

1. **出库效果与每日补给随所在用例结算，不再各自一个事务。** 任一条失败时调用方拒绝整次入场，
   用例把这次做过的全部收回——正是逐条回滚原本达到的效果。失去的是一次失败入场里的部分进度。
2. **一次失败的选角会带走被顶掉角色的最后站位。** 会话结束发生在顶替它的那次用例内，随之回滚。
   代价小且有界，另一种做法是选角根本无法进行。

## 四、一处经评审授权的规格例外

`dungeon_persistent_effect_outbox` 补上声明，表 52 → 53、列 404 → 423。
RQ-007 的字面是「不引入任何新的表或列声明」，此处是补上一处既有空洞使 RQ-006 的前提成真，
不补则「不查表结构元数据」与「选角能成功」二者只能取一。理由见 `reviews/change-review.md` 第七节。

## 五、未决事项（不阻塞接受）

并发「先读后写」的第二个用例会等满 30 秒再失败。这是 deferred 事务换来快照隔离的代价，
真正的解法是「整条用例重来」，属于派发用例那一层，不在本 Spec 边界内。
已写入 `AGENTS.md`，并已上报待用户裁决。

## 六、结论

八条 RQ 全部成立，六条适用的成功条件全部满足，新鲜全量与新鲜构建均通过。

**验证结论：passed。**
