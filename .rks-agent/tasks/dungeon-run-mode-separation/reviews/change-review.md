# 完整变更审查

## Round 1

```yaml
kind: rks-review
target: baseline 2b323fb628b90faa3549fab24e0dd3c47901dd73 to working tree
scope: complete code plus mixed deliverable
verdict: changes_required
```

独立审查确认普通 Interactive 入口不会因活动任务自动升级，但发现四项需要修正：

1. QuestLinked 只投影 `questMaze`，未在出口和结算前检查冻结关联任务的完成条件。
2. actor 死亡去重先于两个独立持久事务，任务事务失败会留下不可重试的丢进度窗口。
3. 一次性入口意图只按角色保存，旧连接留下的 Directed 意图可能被 replacement session 消费。
4. 合法的两字段 `[quest connection]` 因要求第三个难度字段而被静默丢弃。

修正要求：加入按冻结目的和任务日志判定的完成门；将经验与任务推进合并为一个事务并把 actor 死亡改为可精确释放的 claim；意图绑定 exact selected-session generation；两字段连接缺省难度 `-1`，随后补齐故障和边界测试。

完整原始审查回报由独立审查任务 `/root/dungeon_mode_change_review` 返回；本文件保留控制器消费后的决定和修正条件。

## Round 2

```yaml
kind: rks-review
target: baseline 2b323fb628b90faa3549fab24e0dd3c47901dd73 to remediated working tree
scope: Round 1 findings and direct regressions
verdict: approved
```

独立复审逐项确认：

- `resolved`：完成规则在开出口和 `TrySettle` 前检查冻结任务关联条件，普通路径不变。
- `resolved`：经验与 hunt-enemy 推进共享单一事务，带 token/run identity 的死亡 claim 可在失败时精确释放，旧 claim 不能撤销重试。
- `resolved`：入口意图绑定 `SelectedSessionIdentity`，replacement session 不可消费旧代次意图。
- `resolved`：`[quest connection]` 支持恰好两或三字段，两字段难度缺省为 `-1`。

复审采用修订后聚焦结果：Application 33/33、Infrastructure 41 passed + 1 expected skip、Protocol 54/54、Release build 0 warning/0 error、`git diff --check` 通过。本轮无未解决 material finding；A21 双路径仍是完成验证门，而不是代码审查缺陷。

## Round 3

```yaml
kind: rks-review
target: Round 2 approved result plus command-handler architecture remediation
scope: transaction composition, DI registration, and direct regressions
verdict: approved
```

完整解决方案测试发现 `ApplyMonsterKillCommandHandler` 直接注入具体子 Handler，违反生产 Command Handler 只能依赖领域形状的架构约束。修正后，原子击杀 Command 直接依赖 Abstractions 中的五个领域服务；经验与任务推进复用静态实现，但仍由一个 transactional Command 和同一个外层 UnitOfWork 包围，没有嵌套 CommandBus 或第二个事务。独立复审未发现 material finding。

新鲜证据：架构测试 13/13、经验/任务推进/MonsterDeath 直接回归 15/15、生产命令注册解析 6/6、Release build 0 warning/0 error、`git diff --check` 通过。

## Round 4

```yaml
kind: rks-review
target: Round 3 approved result plus quest completion reward implementation
scope: reward projection, target authority, and direct regressions
verdict: changes_required
```

独立审查确认奖励定义不再扁平发放，但发现 type 2 AI 死亡没有接入可信目标推进，以及混合 hunt-enemy 目标在 selector 选中前按整组权威判定，可能拒绝合法单通道或接受错误通道。修正后，Monster 与 AI 使用各自可信死亡通道；只有 Monster 获得经验/掉落；目标权威在 selector 选出实际通道后判定，非法类型、缺失通道与跨权威声明均关闭失败。

## Round 5

```yaml
kind: rks-review
target: Round 4 remediation plus complete mixed deliverable
scope: reward semantics, atomicity evidence, and final artifacts
verdict: changes_required
```

独立审查发现两个收口缺口：只有 `[gold multiple]`、没有 item-zero marker 的任务未生成金币奖励；已有模拟失败测试不足以证明真实 quest/item/gold/experience 写入在晚失败时共享回滚。修正后，GoldMultiplier 自身即可建立金币奖励；新增真实 CommandBus 与同一 SQLite UnitOfWork 的晚失败测试，在经验写入后注入异常并证明任务、物品、金币、等级/经验全部恢复。

## Round 6

```yaml
kind: rks-review
target: baseline 2b323fb628b90faa3549fab24e0dd3c47901dd73 to final working tree
scope: Round 5 remediation plus final mixed-deliverable consistency
verdict: approved
```

最终只读复审确认 Round 5 两项 finding 均已关闭；`verification/completion-evidence.md` 已同步为 pass，并与 TaskContract revision 2、诊断、实施结果、3194+2 自动化证据和 A21 实机结果一致。AI type 2 与真正多选一奖励仍明确保留为非 ClientVerified 的证据边界。本轮无未解决 material finding，`git diff --check` 通过。
