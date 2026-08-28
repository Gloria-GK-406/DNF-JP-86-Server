---
kind: rks-verification
decision: pass
---

# 终验：频道奖励按档案声明的副本归属发放

**被评价的主张**：`spec-002-channel-reward-by-dungeon-group` 的八条成功条件全部成立，改动可以验收。

**新鲜度**：证据取自评审后补强完成的最终状态。命令在 `.worktrees/dungeon-run-experience/Server` 下执行，`PVF_ARCHIVE_PATH=/d/project/DNF/DNFClient-A21-protocol/Script.pvf`。

| 条件 | 证据 |
|---|---|
| SC-1 组内按该行声明的百分比支付 | `ChannelScriptExperienceSourceTests.AChannelPaysForADungeonInItsOwnGroup`；`ExperienceBonusServiceTests.TheRateIsTheOneDeclaredForTheChannelTheSessionIsOn`（两个频道两个比率）；`GrantKillExperienceCommandTests.ACapturedKillPaysWhatTheWireCarried` 端到端复现四个抓包样本 62→65、72→75、126→132、146→153，且贡献的 Kind 为 `SeriaBlessing` |
| SC-2 组外不支付 | `AChannelPaysNothingForADungeonOutsideItsGroup`（同一副本 144 在频道 10 付、在频道 11 不付）；`ExperienceBonusServiceTests.AChannelEarnsNothingForAnotherChannelsDungeon` 两向各一例；`GrantKillExperienceCommandTests.TheSameKillOutsideTheChannelsDungeonsPaysOnlyItsWorth` |
| SC-3 无行、行残缺、百分比越界时不支付 | `AnUnlistedChannelPaysNothing`；`ARowThatCannotBeReadPaysNothing` 五例（字段不足、类型非数、百分比非数、>100、<0）；`AChannelDeclaredTwicePaysNothing`；`AGroupDeclaredTwicePaysNothing`；`TheSectionsOpeningCountIsNotAChannel` |
| SC-4 解析不出东西时照常启动、照常发经验 | 启动一半是实机的：临时目录放 74 字节、无任何段落的散文当 `channel_info.etc`，随机端口（60384/60385/60386）起真 Host，日志出现 `Application started`，随后按 PID 停止。发经验一半是测试的：`ExperienceBonusServiceTests.ACatalogueThatDeclaresNothingPaysNothing` 与 `TheSameKillOutsideTheChannelsDungeonsPaysOnlyItsWorth` |
| SC-5 比率不再是服务端配置项 | `ExperienceTuningSourceTests.TheShippedFileDocumentsTheDefaultsInCode` 断言 `experience.json` 中 `ChannelRate` 键**不存在**；`ExperienceChannelPolicy` 类已删除，`ApplicationModule` 不再注册它 |
| SC-6 仍落在 `+0x1A` | `GrantKillExperienceCommandTests` 断言贡献 Kind 为 `SeriaBlessing`；`MonsterDeathCodecTests.Every_named_line_is_written_at_the_offset_the_client_draws_it_from` 断言该 Kind 写在 `0x1A` |
| SC-7 部署能让正反两面被看见 | `appsettings.json` 配了频道 10（`[granfloris]`，端口 10010）与频道 11（`[sky_catle]`，端口 10011）；`Docs/HostStartup.md` 新增一节，写明在频道 10 打 144 会多出「赛丽亚的祝福奖励」、在频道 11 打同一张图不会 |
| SC-8 全套通过、构建零警告零错误 | `dotnet build`：0 警告 0 错误。`dotnet test`：Protocol 311、Networking 51、Pvf 60、Host 4、Application 716、Infrastructure 157(+2 跳过)、Persistence 240 —— 合计 **1539 通过 / 2 跳过 / 0 失败** |

## 判据强度是量出来的

这一组的价值全在「条件到底有没有生效」，而条件的缺失恰恰是被四个全部吻合的样本掩盖过一次的。独立评审跑了九组变异，八组变红：`Covers` 恒真、`RateFor` 忽略 `dungeonId`、百分比改读 `tokens[5]`、组名改读 `tokens[2]`、取消频道矛盾作废、取消组名矛盾作废、去掉计数标量保护、去掉 0–100 上界。

首轮唯一存活的一组是「把会话的频道号写死成 10」——因为服务层夹具当时只有一个频道。补强后夹具改成两个频道（0.05 与 0.20 两种比率、监听端口固定在 10010），该变异现在红两例，另一种更现实的混淆「误读 `ListenerPort`」红三例。评审复跑确认。

## 两处需要如实标注的

1. **`Channel experience: N channel(s)` 这行日志不会在启动时出现。** 目录是惰性加载的，第一次击杀才读。启动日志里没有它**不代表**解析没跑。
2. **SC-4 的「照常发经验」是测试证据，不是实机证据。** 实机只验了「照常启动」那一半。要在实机上闭合，得拿一份不可解析的脚本真打一只怪，本次没有做——单元测试已经覆盖到发放路径不因空目录改变，代价与收益不相称。

## 未由本次终验覆盖的

`spec.md` 的 EV-005 后半句（同一张图在两个频道各一张实机通告截图）属根契约 SC-3 的实机验收，由用户在三个子成果全部完成后一次性执行。本次终验不代替它。

## 交给后继的一处既有隐患

`Channel:Channels` 里每个频道的 `Port` 与 `Networking:GamePorts` 是两份互相独立的配置，没有任何一处校验它们一致。本成果把默认部署从一个频道改成两个，等于把犯错的机会翻倍。这不是本成果引入的缺陷，也不在其边界内，记在这里以免被后来者当成新问题。
