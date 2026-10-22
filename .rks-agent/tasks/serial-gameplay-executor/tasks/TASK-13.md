# TASK-13 — 修正 TASK-11 调度测试的竞态

## Task Unit Contract

- Revision: 1
- Outcome and references：消除 `ScheduledTaskExecutorTests` 的偶发失败（Expected 2, Actual 3）。这个用例由 TASK-11 新增，属于 TASK-11 已提交结果的修正，只改测试，生产代码不动。诊断结论见本文件 Detailed Plan §1。
  - 编号顺延：原「T-004b 拆分」（`tasks/TASK-08.md` 文末）中的 b6 GM/HTTP 与启动、b7 对账收口，分别改为 TASK-14、TASK-15。
- Owned boundary：只改 `Server/tests/Dfo.Server.Application.Tests/Scheduling/ScheduledTaskExecutorTests.cs`。
- Required checks：
  1. Release 构建 0 警告 0 错误。
  2. 在工作区按 `FullyQualifiedName~Scheduling.ScheduledTaskExecutorTests` 过滤运行：
     - 隔离运行至少 30 次，0 失败；
     - 负载下至少 30 次，0 失败。负载为 8 个限时 `yes`，脚本用 trap 清理，结束后 `pgrep -x yes` 为 0。
  3. 反向判据，只作诊断，不入库：在修正版里，Stop 之前额外等约 2 秒，计数仍为 2；原 1 分钟周期在同样条件下稳定得到 3。
  4. `Dfo.Server.Application.Tests` 整项目运行一次：总数不变，失败只有那 2 条 `DimensionDungeonTests`。
  5. 断言不变：测试方法集合不变，断言行不变，只有周期常量从 1 分钟改为 1 天。
- Dependencies：TASK-11、TASK-12（已提交）。
- Result review：not-required。改动只有一个测试文件中的一个常量，边界清楚；Required check 2 在负载下有区分力（原测试约 50% 失败），check 3 能证明修正去掉的是补跑的来源，而不是缩小时间窗。这两项检查足以暴露错误的修法。

## Detailed Plan

1. **诊断结论**（诊断代理用 systematic-debugging 得出，实验在 648fbc60 和 a02bce46 的导出副本上做）：
   - 测试的周期是 1 分钟，按纪元对齐；重试延迟 2 分钟；一次 `Advance(2 min)` 跨过了 22:00 和 22:01 两个周期。
   - 调度器按既有的 `MissedRun = RunOnce` 语义，在重试完成后补跑一次：`attempt=1`，`ScheduledAt=22:01`。这是正当的新周期，不是重复执行。
   - 这次补跑在 TASK-11 之前就存在。当时重试在 `Advance` 内同步完成，测试的 Stop 总能赶在补跑之前。TASK-11 把每次尝试改为执行器上的异步作业之后，两条路径耗时相当，于是成了真竞态。
   - 结论：生产不受影响，不会多执行同一次尝试。
2. **修正**：把第 29 行的 `new IntervalTaskSchedule(TimeSpan.FromMinutes(1))` 改为 `TimeSpan.FromDays(1)`。
   - 不能用 1 小时：周期按纪元对齐，从 21:59 推进 2 分钟会跨过 22:00。
   - 改为 1 天后：
     - 首次运行仍会因为 RunOnce 在启动时立即执行；
     - `ActiveTimers >= 2` 仍然成立；
     - 推进 2 分钟只触发重试，不再跨过任何周期。
3. **禁止的替代修法**：在 Stop 之前等待、把断言放宽为 `>= 2`、调大 Patience。
4. 按 Required checks 验证，然后写 Result。

## Result

- **Outcome**：done。`ScheduledTaskExecutorTests` 的周期从 1 分钟改为 1 天。推进 2 分钟不再跨过任何周期，偶发的第三次回调的来源因此消除。生产代码没有改动。
- **Changed**：只改了 `Server/tests/Dfo.Server.Application.Tests/Scheduling/ScheduledTaskExecutorTests.cs` 第 29 行，把 `TimeSpan.FromMinutes(1)` 改为 `TimeSpan.FromDays(1)`。`git diff --numstat` 为 `1 1`，基准为 HEAD d32f15b4。
- **Checks**：
  1. Release 构建 `Dfo.Server.Application.Tests`：0 警告，0 错误（`logs/task-13-build.txt`）。
  2. 过滤器 `FullyQualifiedName~Scheduling.ScheduledTaskExecutorTests`，Release，`--no-build`：
     - 隔离运行 30 次：30 通过，0 失败（`logs/task-13-isolated.txt`）。
     - 负载下 30 次：30 通过，0 失败（`logs/task-13-load.txt`）。负载为 8 个 `yes`，开始前 0 个，结束后 `pgrep -x yes` 为 0。
     - 对照：修正前在同样条件下，隔离 30 次失败 4～6 次，负载下 30 次失败 15 次（诊断副本数据）。
  3. 反向判据，只作诊断，不入库（`logs/task-13-reverse.txt`）：
     - 做法：在当前工作区的 scratchpad 副本里，`retried` 之后、Stop 之前额外等 2 秒，并记录回调次数。隔离运行 30 次。
     - 1 天周期：30 次计数全为 2。
     - 1 分钟周期：30 次计数全为 3。
  4. `Dfo.Server.Application.Tests` 整项目运行一次（`logs/task-13-application.txt`，detailed）：
     - 结果：总数 4497，通过 4479，失败 2，跳过 16。
     - 4479 ＋ 2 ＋ 16 ＝ 4497，与 TASK-12 最终的 4497 相同。
     - 失败的 2 条只有 `DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource` 和 `DimensionDungeonTests.All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`。
  5. 断言不变（`logs/task-13-diff.txt`）：
     - 对 HEAD 与工作区文件分别抽取 `[Fact]`、方法签名和 `Assert.` 行，两份 11 行完全一致。
     - 整文件 diff 只有第 29 行。
- **Remaining**：
  - 负载用的 `yes` 没有单独限时，因为 macOS 上没有 `timeout`。它们的寿命受脚本约束：脚本结束时清理，trap 兜底。日志记录了开始前与结束后的进程数，两次都是 0。
  - Networking 测试主机崩溃等 TASK-12 遗留项不在本单元范围内，不受影响。
