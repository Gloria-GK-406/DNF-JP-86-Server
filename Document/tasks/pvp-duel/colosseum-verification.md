---
kind: rks-verification
decision: manual_required
---

# 斗兽场验收记录

服务端候选于 2026-11-25 通过 Host 构建、项目引用检查、6 项双端 TCP 回归和 14 项迁移测试。TCP 使用开发通道的 A21 `Script.pvf` 和独立临时 SQLite。测试用参赛资格、货币和临近终局的进度写入临时库。

- 普通斗兽场覆盖未定级拒绝、从匹配请求绕过入场的拒绝、余额正好为 200、重复入场、与普通对手混排、参赛胜方推进任务、10 胜结轮和重复领奖。入口和实物奖励走生产协议及背包代码。
- 巅峰斗兽场覆盖代表入场、取消后继续匹配、与普通巅峰对手配对、三角色轮换、整场只计一次挑战胜场和三胜任务进度。普通匹配和巅峰定级的原有回归同时通过。
- v20 至 v21 新建挑战表和索引，旧账户及决斗数据由迁移测试验证保留。

复现入口为 `Dfo.Server.Networking.Tests` 的 `Colosseum_mixes_with_ordinary_duels_and_credits_only_the_paid_challenge`、`Ranked_total_match_cancels_pairs_rotates_and_settles_by_account` 与 `Ordinary_duel_can_cancel_its_queue_and_then_pair_two_waiting_players`，以及 `DbMigrate.Tests` 的 `SchemaVersionTests`。

## 待实机验证

在 CH60 和 CH81 用已定级的测试号各走一次「开始对战」、扣费、续打和返回大厅。验证重登复原、胜负显示与对应三胜任务。使用临近 10 胜或 3 负的测试号验证 A21 奖品列、领奖确认以及余额和实物同步。巅峰混排是否符合原版、第二个每日上限的含义以及是否还有 GP 门槛，现有证据尚未闭合。
