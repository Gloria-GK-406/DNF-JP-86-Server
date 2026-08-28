---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 频道奖励按档案声明的副本归属发放

## Delivered

- `channel_info.etc` 第一次被解析：`[dungeon]` 段给出每个副本组含哪些副本，`[server]` 段给出每个频道绑哪个组、加成百分之几。这个文件本仓一直在原样发给客户端，从来没读过它写了什么。
- 频道奖励因此有了**归属条件**：本局副本属于所连频道声明的那个组才发，比率取自那一行。此前是不问频道、不问副本，一律 5%。
- 矛盾即作废：一个频道号或一个组名被声明两次、一行读不懂、百分比越界，都按不发处理，而不是替档案选一个答案。
- 比率退出服务端配置。`ExperienceChannelPolicy` 删除，`Experience:ChannelRate` 从 `experience.json` 移除并留下一行说明去哪儿改。
- 默认部署改为两个频道（10 → `[granfloris]`，11 → `[sky_catle]`），好让这条规则在实机上正反两面都看得见；`Docs/HostStartup.md` 写明各自跑哪张图会看到什么。

## Success Evidence

| Condition | Evidence |
|---|---|
| SC-1 | `verification/completion-evidence.md`；`GrantKillExperienceCommandTests` 端到端复现四个抓包样本 |
| SC-2 | 同上；同一副本在组内频道与组外频道的两种结果，解析层、服务层、命令层各一处 |
| SC-3 | 同上；无行、五种残缺、频道重复、组名重复、计数标量 |
| SC-4 | 同上；实机启动（不可解析脚本 + 随机端口）+ 空目录不付不抛的测试 |
| SC-5 | 同上；`experience.json` 中该键的缺席被断言 |
| SC-6 | 同上；贡献 Kind 为 `SeriaBlessing`，写在 `0x1A` |
| SC-7 | 同上；`appsettings.json` 两个频道 + `Docs/HostStartup.md` |
| SC-8 | 同上；build 0/0，全量 1539 通过 / 2 跳过 / 0 失败 |

## Repository Result

- Changed paths：新增 `Server/src/Dfo.Server.Abstractions/Channel/ChannelExperienceContent.cs`、`Server/src/Dfo.Server.Infrastructure/Content/ChannelScriptExperienceSource.cs` 及三个测试文件；修改 `ApplicationModule`、`InfrastructureModule`、`ExperienceBonusRules`、`ExperienceBonusService`、`appsettings.json`、`experience.json`、`Docs/HostStartup.md`、`ExperienceBonusRulesTests`、`ExperienceTuningSourceTests`、`ChannelHostStartupTests`、`CommandRegistrationTests`
- Baseline or accepted result：基线 `2d121cf9`
- Parent rollback required: no

## Successor Guidance

- **通关结算的频道加成现在有了来源。** `ChannelExperienceCatalog.RateFor` 对结算同样适用——上游那份注释指明它出自 `df_game_r CParty::getClearRewardBonusExp`，参数第八位就是频道比率，按冻结的通关基数算。`ExperienceBonusRules.FromChannel` 目前只认 `MonsterKill`，第三个成果要放开 `DungeonClear` 时，改的是那一个条件，不是再写一条规则。
- **`Channel:Channels` 的 `Port` 与 `Networking:GamePorts` 是两份互不校验的配置。** 既有隐患，本成果把默认频道从一个变成两个，等于把犯错机会翻倍。谁先动到这块配置，谁顺手加一道启动期校验。
- **`Channel experience:` 日志在启动时不出现**，目录惰性加载、首次击杀才读。排查时别把它的缺席当成解析没跑。
- **给第三个成果的侦察已经做完**（均在 A21 档案中实地确认）：`Etc/ServerParameter.etc` 的 `[clear rank exp bonusrate] = 0.05 0.1 0.12 0.15 0.2`（上游兜底 `0/0.01/0.02/0.03/0.05` 与本档案不符，不可抄）、`[clear exp bonusrate] = 1`、`[pc room party exp bonus rate] = 0.05`；`Etc/RankSystemInfo.etc` 的 `[rank grade] = 99 90 80 60 50 30 20 10`（与上游兜底一致）、`[dungeon clear time reduction bonus] = 0 0 10000 0 20000 0 30000 0`；`n_quest/questParameter.etc` 的 `[exp reward table]` 前几项 `100 100 126 187 293 456 683 …`（`-1` 是分隔符）。上游通关基数取该表在副本 `[basis level]` 处的值乘难度率与副本权重，我们的 `DungeonLayout.BasisLevel` 与其 `StandardLevel` 对应。
- **不要跟随上游删除频道加成。** 其当前版本把 `+0x4B` 叫 `RemovedChannelExpOffset` 并注明「当前版本没有频道经验来源」。档案里这条规则写得清清楚楚，实机也测到过它发钱。

## Artifacts

- `.rks-agent/tasks/dungeon-run-experience/attempts/spec-002-channel-reward-by-dungeon-group/spec.md`
- `.../reviews/change-review.md`（`verdict: approved`，两轮：首轮通过并提九条非阻断，补强后复核维持）
- `.../verification/completion-evidence.md`
