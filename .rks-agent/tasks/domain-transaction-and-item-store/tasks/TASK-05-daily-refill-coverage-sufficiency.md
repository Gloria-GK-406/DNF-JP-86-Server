# TASK-05 — 判定每日刷新与教学奖励的等价覆盖是否充分

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 对四类判定（日界、领取幂等、叠加或新开格、按段查找空位）逐条判定既有覆盖是否足以证明归属改变前后行为等价；不足处在归属改变**之前**补齐，用例断言的是当前行为。
- Referenced spec requirements: RQ-006
- Referenced plan sections: `../plan.md` 的 `### T-005` 全节；BD-016；`## Risks and Rollback` 第三条
- Owned boundary: BD-016
- Permitted files or interfaces:
  - `Server/tests/Dfo.Server.Infrastructure.Tests/Inventory/`（补齐用例）
  - `Server/tests/Dfo.Server.Application.Tests/`（补齐用例）
- Dependencies: TASK-04
- Required dependency outputs: TASK-04 之后的领域词汇。
- Acceptance conditions:
  1. 四类判定各有至少一条会因该判定改变而失败的用例。
  2. 每一条新补用例在归属改变之前即为绿——它断言的是当前行为，不是目标行为。
  3. 结论落纸：哪些判定原本已被锁住，哪些是本任务新补的。
- Task-specific authorization: 沿用根 TaskContract 的 `task` 授权。收窄：本任务只补覆盖，不改变任何生产代码行为。
- Required methods: `test-driven-development` 的变体：新补用例断言现状因而不经 RED；其证伪能力由「改动该判定会使该用例失败」证明，须实际做一次该证明而非声称。
- Required checks: CK-017、CK-018（见 `../plan.md` 对应任务的 Validation 表，命令与预期结果以该表为准）
- Required evidence: CK-017 的测试结果与每条用例的证伪演示；CK-018 的充分性结论与补齐清单。

## Execution Result 1

### 结论：覆盖不足。四类判定里三类无覆盖，第四类是假覆盖

判定方法不是数测试，而是**逐条改坏实现看有没有测试变红**。四类判定各改一处最小变更，跑 `~DailyRefill` 过滤器：

| 判定 | 改动前的覆盖 | 变红的用例 |
|---|---|---|
| 日界（UTC+8） | **无** | 无 |
| 领取幂等 | **假覆盖**（见下） | 无 |
| 叠加或新开格 | **无** | 无 |
| 按段查找空位 | **无** | 无 |

「假覆盖」值得说清楚。既有的 `DailyRefill_CommitsGrantAndClaimTogetherAndSecondRunIsNoOp` 看上去锁住了幂等——它断言第二次运行 `Assert.Empty(second.Grants)`。但那条规则是 `RefillToTarget`、目标 5，第一次发放后角色已持有 5，于是 `CalculateGrant` 第二次本来就返回 0。把幂等检查彻底改坏（让它永远判为「未领取」），这条用例**依然全绿**：发放数为零不是因为领过，而是因为已经满了。它另一条断言查的是计数器的 `value`（恒为 1），不是它记的**期**，所以期被改坏也照样绿。

这正是 T-005 存在的理由：如果直接进 T-006 把规则外迁，日界、叠加决策、按段查找三者任意一处漂移都不会有任何东西变红，而漂移只在入场路径显现——玩家进游戏才看得见。

### 补齐的覆盖（5 条）

| 用例 | 锁住什么 |
|---|---|
| `DailyRefill_TurnsOverAtMidnightInUtcPlusEight` | UTC+8 的 23:30 与次日 00:30 相隔一小时、分属两天，各发一次。两个时刻在 UTC 下同属 12 日——所以把日界改成 UTC 就会少发一次 |
| `DailyRefill_TreatsOneUtcPlusEightDayAsOneDayHoweverUtcFalls` | 反向：UTC+8 同一天的 07:30 与 08:30 在 UTC 下跨了午夜，仍只发一次。这一条同时也是幂等的真覆盖 |
| `DailyRefill_AddsToAPileThatExistsRatherThanOpeningASecond` | 已有一堆时就地累加而不另开一格：断言行数仍为 1、槽位是原来那格、`GrantedCount` 与 `CommittedCount` 各自正确 |
| `DailyRefill_TakesTheFirstFreeSlotOfTheRulesOwnBand` | 段内首个空位。故意让 3 号（段外）空着、65 与 66 占着，答案必须是 67 |
| `DailyRefill_RefusesWhenTheBandIsFull` | 段满时拒绝且不外溢，且拒绝把整次入场一起带走——计数器一行都没写 |

两条日界用例用的是 `AddUpToStackLimit` 而非 `RefillToTarget`。理由与上面的假覆盖同源：refill-to-target 的规则第二天本来就不发，那样的用例分不出「跨了一天」和「没跨一天」。

### 每条新用例都会因其判定改变而失败（CK-017 的实质）

对实现做四处最小改动，逐一验证：

| 改坏的判定 | 改动 | 变红的用例 |
|---|---|---|
| 日界 | `ToOffset(TimeSpan.FromHours(8))` → `ToOffset(TimeSpan.Zero)` | `TurnsOverAtMidnightInUtcPlusEight`、`TreatsOneUtcPlusEightDayAsOneDayHoweverUtcFalls` |
| 领取幂等 | 查询用的期改为 `period + "x"`（永不匹配） | `TreatsOneUtcPlusEightDayAsOneDayHoweverUtcFalls` |
| 叠加或新开格 | `items.Count == 0` → `items.Count >= 0`（永远新开格） | `AddsToAPileThatExistsRatherThanOpeningASecond` |
| 按段查找空位 | 起点 `slotStart` → `0`（无视段的下界） | `TakesTheFirstFreeSlotOfTheRulesOwnBand`、`RefusesWhenTheBandIsFull`、`CommitsGrantAndClaimTogetherAndSecondRunIsNoOp` |

四类判定各至少一条。改动逐次施加、逐次还原；`git diff` 对该实现文件为空，实现未被改动。

（首轮尝试里「领取幂等」与「叠加或新开格」两处改成了编译期常量条件，`TreatWarningsAsErrors` 把 CS0162 变成错误，构建失败因而零变红——那是探针的假阴性，不是覆盖不足。换成可编译的等价改动后各得一条。）

### 教学奖励一侧

`GrantItemAsync` 与每日刷新共用「叠加或新开格」和「按段查找空位」两类判定（`SqliteEntryInventoryMutationRepository.cs:161-196`），上面的按段查找改动同样让教学奖励路径失去正确性。教学奖励自身的对外行为由 `SmallReportPayloadCodecTests` 与 `TaggedIdReportHandler` 的既有用例保住。本任务不额外补，因为它的两类判定已由上述用例锁住，而 T-006 迁的正是这两类。

## Executor Self-check 1

| Acceptance condition | 结果 |
|---|---|
| 逐条核对四类判定是否被锁住 | 通过——三类无覆盖、一类假覆盖，逐条记录 |
| 归属改变之前补齐覆盖，断言的是当前行为 | 通过——5 条新用例全部断言改动**前**的行为，且在实现未动的情况下即为绿 |
| 记录结论：哪些原本已锁住，哪些是本任务新补的 | 通过——见上两表 |

### Validation

| ID | 命令 | 结果 |
|---|---|---|
| CK-017 | `dotnet test Server/tests/Dfo.Server.Infrastructure.Tests -m:1 --filter FullyQualifiedName~DailyRefill` | `已通过! - 失败: 0，通过: 9`（既有 4 + 新增 5）。四类判定各有至少一条会因该判定改变而失败的用例，见上表 |
| CK-018 | 充分性结论与补齐清单 | 本节即是。每条新用例在归属改变之前即为绿 |
| 回归 | `dotnet test Server/86JP.Server.slnx -m:1` | `923 通过 / 2 跳过 / 0 失败`（较上一轮 +5） |
