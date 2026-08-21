---
kind: rks-verification-evidence
spec: ./../spec.md
contract: ./../TaskContract.md
change_review: ./../reviews/change-review.md
state: passed
---

# spec-002 完成验证

评审通过、补救全部落地之后重跑。

## 一、新鲜证据

| 项 | 命令 | 结果 |
|---|---|---|
| 构建 | `dotnet build Server/86JP.Server.slnx --no-incremental` | 0 错误 0 警告 |
| 全量 | `dotnet test Server/86JP.Server.slnx -m:1` | **1032 通过 / 2 跳过 / 0 失败** |
| 假红复核 | 隔离复跑 `Dfo.Server.Host.Tests` | 4/4 通过，8 秒 |

分项：Pvf 60、Application 494、Host 4、Infrastructure 99（+2 跳过）、Networking 46、
Persistence 126、Protocol 203。

`ChannelHostStartupTests.InvalidRequiredChannelScript_FailsBeforeListenersStart(kind: "unreadable")`
在满载全量下撞 10 秒进程退出预算。按 `AGENTS.md` 的判据隔离复跑：4/4 通过，计为 0 失败。

## 二、契约成功条件

| SC | 条件 | 结论 | 证据 |
|---|---|---|---|
| SC-A | 四个构造在 `Server/src/` 下零命中 | 满足 | 逐名 `grep -rn`，四个全部零命中；`CreateTransient` 亦零命中 |
| SC-B | 每个删除对象事先被证明无生产调用方 | 满足 | 见下方第三节；评审者另行回到基线独立复核过一遍 |
| SC-C | 依赖过该替身的测试仍成立，断言未放宽 | 满足 | 替身逻辑逐字未改；其余文件只有改名与 using；被误删的四条保护已搬回并证明能红 |
| SC-D | 对外可观察行为未变 | 满足 | Protocol.Tests 203/203；相关文件的 `0x` 字面量逐个一致，`Assert.` 计数与基线相同 |
| SC-E | 减量构成可交代（原数 23，经评审修订为 5） | 满足 | 1037 → 1032：−8（删两个测试文件）+4（搬回被误删的保护）−1（删一条同义反复）|

## 三、删除之前的无调用方证据（C-3 / SC-B）

在基线 `ea95d49` 上检索 `Server/src`，排除各自的声明文件：

| 构造 | 生产命中 |
|---|---|
| `MemoryItemStore` | 仅 `ApplicationModule.cs` 一处 `TryAddSingleton<MemoryItemStore>()`，无人解析 |
| `TransientItemContainer` | 无 |
| `ItemContainerFactory` | 仅 `ApplicationModule.cs` 一处注册，无人解析 |
| `IItemContainerFactory` | 仅同一行注册 |

它们服务的东西不存在：本作没有副本运行状态（`Docs/DungeonEntryNotes.md`）。

## 四、一处未照契约字面执行，已经评审修订

`MemoryItemStoreTests`（15 例）没有随另外两个测试文件一起删，而是随替身改名保留。
理由：`VolatileItemStore` 是全仓唯一的非持久 `IItemStore`，服务器真正有的两条规则只能靠它测；
替身若静默失真，两个仍然生效的测试会变成假绿。SC-E 的数随之由 23 修订为 5。

## 五、一处备案

替身退役出生产之后，服务器里没有任何非持久的 `IItemStore`，
`AmbientStoreAccess` 与容器按 `IsDurable` 的分叉在生产上都只走一边。
这批分支是否也该退役，与「要不要有临时背包」同源，属产品问题，不在本 Spec 边界内。

## 六、结论

六条 RQ 全部成立，五条成功条件全部满足，新鲜构建与新鲜全量均通过。

**验证结论：passed。**
