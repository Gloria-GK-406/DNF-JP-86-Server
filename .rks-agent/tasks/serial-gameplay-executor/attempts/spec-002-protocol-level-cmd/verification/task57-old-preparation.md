# TASK-57 旧实现准备阶段

基准为本 Task 开工父提交 `94746fe1`。此阶段只新增正式测试与本记录，未改生产代码、共享执行器、Spec／Plan、迁移账或 Git。它是 TASK-57 的迁移前置，不能作为 Task 完成结果。

## 实际消费者

两源的 Sweep／return 仍消费两条 Leave；Boss departure 的共享阶段仍真实调用 Start。两 PartyCoordinator 只注册，但原强测试仍大量调用，不能直接删测试或适配器来清零。两条 Synchronize 只有对应 Coordinator 消费。RefillOnlineBossTowerTicket 仍由选角与 scheduled 日切消费，保持不在本七行范围。

## 正式证据

日志均在 `verification/logs/`，每次独立 argv、起止 UTC、真实 child exit、TRX 和 DFO 目录见各次 `run.json`。原 wrapper 自身退出码不代表 dotnet 成功，以下使用 child exit。

- `task57-old-baseline-r1`：Application 213 通过，Protocol 42 通过，均 child exit 0。
- `task57-old-preparation-r3`：Application 288 通过，Protocol 43 通过，均 child exit 0；两项目原 DFO parser 各确认一个 control，额外违反为零。
- 最终纯准备状态 `task57-old-preparation-r4/Application`：289 通过，child exit 0，自然结束；独立 DFO 原 parser 确认一个 control、额外违反零。Protocol 沿用同一未改生产源码及未改 Protocol 测试的 r3 43 通过。
- `task57-old-internal-return-red-r1/Application`：两个合法内部回城回归均以真实 `Assert.Null` 业务断言失败，child exit 1；不是编译失败。
- `task57-old-physical-protection-red-r1/Application`：当前去重缺口表征一例通过，保护回归一例在第二 physical 未封锁处失败，child exit 1。

红例分别保存在本机 `/private/tmp/task57-production-regressions.patch` 与 `/private/tmp/task57-physical-protection-regression.patch`，并保留对应源文件；不混入须旧绿的 `pure-preparation.patch`。它们在批准修复后恢复为正式新绿回归。

旧绿纯测试补丁位于 `verification/evidence/task57-old-preparation/pure-preparation.patch`，只含十个测试路径；新增及已跟踪测试的 whitespace 检查通过，生产源码 diff 为空，index 为空。旁边 `preparation.json` 保存路径清单与检查 argv，未包含主控随后编写的 Spec 改动。所有本阶段 runner 已自然结束。

## 决策相关事实

1. **当前生产 timer 的 Leave 到不了领域动作。** 两真实 Tick 的两 due 均开启独立事务，但取 `context.LegacyFootprint` 时抛 `The legacy invocation has no explicit footprint source.`，事务确认回滚、run 不移除、各记两条 Warning。旧 fake-bus 的 Attempts 不能证明真实业务成功。Boss source connection 与 metadata 为空，但 reporter 的实际 physical 已被捕获；Illusion 没有 selected recipient。
2. **旧算法有逐项失败隔离。** 真实 Boss departure 由两个实际两成员 run 的领域 Leave 生成排队 action，真实 Tick 对 survivor 执行 Start。首项动作 SQL、native Commit、nonfallible uncertain、真实 DI scope 清理四类故障均不阻止第二 cohort 进入 Fighting。两个来源的真实 host encoder 首项失败也只关闭首 target，后项 host 登记，下一 Tick 不重复消费。
3. **授权算法接缝不能冒充生产来源。** 因第 1 点，due 的四类故障只能在明确命名的 TEST-ONLY ActiveNonEffect 接缝核旧算法。两塔均继续下一 due；持续首 action／Commit 故障的第二轮仍完成后来新 run，没有首项饥饿。SQL 来自原动作之后、同一真实事务内追加的 `AddGold(+7)` 受控 marker；Start 自然主体是内存，不能称自然生产 SQL 故障。native 787 来自实际 deferred-FK Commit，1811 来自实际 SQL trigger；uncertain 为受控 nonfallible 违约，scope 为 DI 真正释放资源抛错。
4. **固定上下文与取消政策。** Boss loading timeout 先移除死成员，逐 fine 仍携同一 run/version 与原 Members，两个事务分别提交，不新增死亡结算／经验。真实 Tick 在 departure 投影推进时钟后才取 due 扫描时钟。最终 Leave 的真实 Commit 钩子取消 producer 后，Boss 的返回 current-view 读取使用 None，Illusion 继续原 token 并抛同 token 的 OCE。
5. **physical 与 session 不能混成一项。** 多 member 的旧授权 sweep 在第二 Leave uncertain 时，双方 frozen session 都 fenced，但只有第二 fine reporter physical 被 blocked／登记关闭，前 fine physical 保持未封锁。另一个真实 Internal whole 两个普通 Write 均 SQLite 已提交，声明先 `[A physical,B null]` 后 `[A null,B physical]`；后置失败使双方 session fenced，却因当前按 session 取首项而漏掉 B 已声明的 physical。后者有独立正式 RED，P11 不得把它带入新多 member 路径，也不能据此声称旧新 physical 范围完全相同。
6. **BodyOnly 当前有可测差异。** 同一单独 producer OCE，旧 GameplayTimerWork 在队列完成后重抛，执行器零 Error；当前 typed BodyOnly 在 request／scope 清理后仍被标记 EndedAJob 并有一条 Error。P11 需要区分 body 原 OCE、scope-only OCE、独立复用同 cause 的 cleanup、typed／aggregate 与 sticky；本阶段未改核心。

以上为受控正式测试；没有 TCP／A21 实机或新实现通过主张。单 tick 一个 whole 会把这些独立请求失败边界合并；纯 producer／typed 扫描／逐 departure 与 due-run 的方案必须先经主控、Spec／Plan 与唯一 Astra 审定，不能由本记录授权实施。
