---
kind: rks-review
verdict: changes_required
---

# 根审查：副本跑动的经验补完（`ff413e0e..593a8916`）

审查者以全新上下文独立进行，未采信执行者或控制器的结论；三个子成果的 `change-review.md` 仅作线索。

**本件不是三个子成果的复审。** 三件各自已独立审阅并通过。本件的判据是**集成结果**：三个提交合在一起
是否满足根 TaskContract 的八条 SC 与五条 Constraints，以及**有没有东西掉在三件之间**。

## 一、审查范围与完整性

- 变更集 = `ff413e0e..593a8916` 三个提交，`git diff --stat` 60 个文件（其中 24 个是 `.rks-agent/`
  流程件、2 个 `Docs/`、1 个 `protocol/` 未动）。
- 工作树状态：只有 `.rks-agent/tasks/dungeon-run-experience/multi-spec-map.md` 一处未提交改动，
  内容是控制器把游标推到 `root_review` 并把第三行封为 `sealed`，属流程记账，不含产品代码。
  审查过程中我为验证 BF-1 临时改过一次测试文件，已 `git checkout` 还原，工作树与接手时逐字节一致。
- 独立复跑（`PVF_ARCHIVE_PATH` 指向 A21 档案）：
  - `dotnet build Server/86JP.Server.slnx` → **0 个警告，0 个错误**。
  - `dotnet test Server/86JP.Server.slnx` → **1577 通过 / 2 跳过 / 0 失败**。两条跳过的是
    `PvfQuestDefinitionSourceTests` / `PvfTownDefinitionSourceTests` 的「档案缺失时应报错」用例，
    档案在场故跳过——**这意味着本次新增的所有依赖档案与发货文件的 `SkippableFact` 都真跑了**
    （`PvfDungeonRankGradeSourceTests`、`PvfExperienceParameterSourceTests` 的清算表用例、
    `ChannelScriptExperienceSourceTests.TheShippedScriptDeclaresTheRateTheCaptureMeasured`、
    `ExperienceTuningSourceTests.TheShippedFileDocumentsTheDefaultsInCode`）。
- 三个子成果均为 sdd-lite、无 plan 件、未提供对抗挑战集，故本件不含 `Challenge Dispositions`。

## 二、接缝：三件之间掉下去的东西

### 接缝 1：「付出去的每一分都报得出来」是否跨三种场合成立

逐场合走过一遍：

| 场合 | 支付 | 申报 | 是否自洽 |
|---|---|---|---|
| 击杀 | 基数 + `Collect()` 的每笔加成 | 有槽的走 `ExperienceBonusLines.Of(award)` 各占一行；`Unlisted`（权益）由客户端并入基础值 | 成立 |
| 交任务 | 只有任务表基数，无 contribution | `ExperienceBonusLines.None`，全部画作基础值 | 成立 |
| 通关 | 通关基数 + 评分奖励（Kind = `Unlisted`） | 通告 `None`（两项都并入基础值）；结算帧 offset 0/4 分列两项 | 成立 |
| 教学 | 不是 add 而是 set（`SetProgressAsync(2, threshold[0])`） | `ExperienceBonusLines.None` | 成立 |

**常规区间内该性质跨三种场合成立**，且被编译期钉住了一半：`ExperienceNotification` 的 `Lines`
是无默认值的位置参数，全仓四个构造点（`MonsterDeathSlice.cs:420`、`DungeonClearSlice.cs:505`、
`FinishQuestHandler.cs:148`、`TutorialLevelUpSlice.cs:263`）都必须显式表态，第五个来源忘不掉。

**但在经验上限附近不成立**——见 BF-2。

### 接缝 2：发放路径唯一性

全仓 `ICharacterExperienceGrant.AwardAsync` 的调用点恰好三个（击杀、通关、交任务），
`CharacterExperienceGrant` 是唯一「加经验、跨等级线、落库」的实现。四个来源里的第四个——教学——
不经发放件，但它是 **set 而非 add**：`GrantTutorialLevelCommandHandler` 把角色写成固定的
等级 2 与该等级的门槛值，不做累加也不走门槛遍历，因此不构成第二份发放件。根 Constraint
「通关结算不得出现第二份加经验的实现」**成立**。

一处需要知道但不阻断：`DungeonEffectRecoveryService.cs:152-176` 里有一份**休眠**的第二实现——
它从 `DungeonEffectOutbox` 读出一条 `DungeonExperienceSettlement`，自己算 `NewLevel/NewExp`
并直写 `SetProgressAsync`。全仓没有任何写入该 outbox 的代码，所以今天它永远读到空表；
它在 `ff413e0e` 之前就存在，不是本次引入。将来若有东西开始写这张表，它就是第二条跨等级线的路。

### 接缝 3：内容 vs 政策

按「档案声明的从档案读、服务端自定的可配置」逐个分类，本次到位：

- **从档案读**：难度率、组队率、`[monster exp bonusrate]`、`[clear exp bonusrate]`、
  `[clear rank exp bonusrate]`、`[rank grade]`、`[exp reward table]`、副本 `[basis level]`、
  副本 `[experience increasing point]`、`ExpTable.tbl` 等级门槛、
  以及本轮新退回档案的**频道百分比与频道↔副本组归属**。
- **服务端自定、仍可配置且缺省未变**：`Curve`、`WorthDivisor`、`RankRates`、`LevelGapBands`、
  `PremiumRates`、`LabelProbe`。逐项比对 `experience.json` 与 `ExperienceTuning.Default`，
  数值完全一致，因此「删掉 `experience.json` 行为不变」这句仍然为真（`AddJsonFile(optional: true)`，
  段不存在即走 `ExperienceTuning.Default`）。
- **`experience.json` 在 `ChannelRate` 离开后仍然诚实**：`ChannelRate` 键已删，代之以
  `//ChannelReward` 说明它去了 `channel_info.etc`；`ExperienceTuningSourceTests:185` 以
  **缺席断言**钉住这一点（旧文件里留着这个键的部署会读一个服务端已不再查的旋钮，这条断言正是防它）。
  `ApplicationModule` 里对应的注册整块删除，全仓再无 `Experience:ChannelRate` 的读取点。

反过来的方向也检查了。怪物 rank 倍率 `[1,2,3,4]` 全档案扫描确认无对应声明（`[drop bonusrate of
monster kind]` 是掉落用的另一张表），是服务端政策；`AccountHonorLevel.Initial`、
`ExperienceParameters.UnlistedRate = 1.0` 是占位/兜底而非对档案值的抄写；
`PartyMemberCount: 1` 确实顶掉了档案声明的四档人数倍率，但组队被根契约 Scope Out 划出且代码写明了理由。

**但发现一处与本任务同型的残留（NB-14）**：`PremiumRates` 被断言为「档案没有声明，属服务端政策」，
而档案实际有声明。这一处是 `ff413e0e` 的既有代码、不在本次变更范围内，故列为观察而非阻断。

**还有两处注释因为这次搬家而变成了假话**——见 NB-1、NB-2。

### 接缝 4：击杀与通关两条路的一致性

逐项对照 `MonsterDeathHandler` 与 `SetPlayResultHandler`：

| 项 | 击杀 | 通关 | 判断 |
|---|---|---|---|
| 无已发布会话 | 记 warning 后静默返回 | 同 | 一致 |
| 无技能镜像 | 记 warning、只回死亡确认、不发放 | 记 warning、回结果帧 + 全零奖励帧、不发放 | 差异有理由（两条路的帧集不同），且代码写明了通关侧「拒绝一次即永不补发」的后果 |
| 会话回写时机 | 发帧前 | 发帧前 | 一致 |
| 跨等级线 | 刷新可接任务并补发列表帧 | 同 | 一致 |
| 发放未发生（award 为 null） | 沿用入场值报出 | 沿用入场值报出，且奖励帧申报零 | 一致 |
| 日志 | 整笔 award 一行 | 整笔 award + 两项分列一行 | 一致 |
| **重放拒绝** | `TryReportActorDead` 只认第一次 | **无任何消费动作** | **不一致，无理由 → BF-1** |
| 申报来源 | `ExperienceBonusLines.Of(award)` | 硬写 `.None` | 今天等价，但埋了分叉 → NB-3 |
| 探针 | 作用于击杀通告 | 不作用 | 差异有理由（十四槽用一次击杀就测全） |

### 接缝 5：三个子成果各自以为别人会做的事

**两条子审查已经识别、并明确「向上推」的观察，没有落进根的账本。** `spec-003` 的
`reviews/change-review.md` 的 O-2（经验到顶时帧在说谎，理由是「结算这一侧无法干净地绕开，
**留给发放件自己的后继成果**」）与 O-3（重放会重复付款，「**这是本次改动新引入的风险面**」），
在 `multi-spec-map.md` 的 SC 进度表里都没有出现——SC-5 的「剩余缺口」写的是「无」。
根契约没有第四个成果，三行全部 `sealed`，所以这两条被推给的那个「后继」**不存在**。
根审查是它们最后一道闸，因此本件把它们升为阻断项（BF-1、BF-2）。

## 三、Blocking Findings

### BF-1：重放一次 `SET_PLAY_RESULT` 会把整局通关再付一遍

- **位置**：`Server/src/Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs:371-460`
  （`SetPlayResultHandler.HandleMessageAsync`）。
- **事实**：该处理器只用 `runs.Find(session.CharacterId)` 读 run，**不消费任何东西**；全仓
  `IDungeonRunDomainService.End` 只有两个调用点——`GiveUpGameHandler`（`DungeonClearSlice.cs:142`）
  与 `DungeonSelectionSlice.cs:202`，结算不在其中，`DungeonRunState` 上也没有「已结算」的标记。
  分发层 `ClientMessageHandler<T>` 无任何去重。因此第二帧会再次走到
  `GrantClearExperienceCommand` → `ICharacterExperienceGrant.AwardAsync` → 落库。
- **实证**：我在 `SetPlayResultHandlerTests` 里临时加了一条把同一个 handler 连调两次、
  统计 `ICommandBus` 被问次数的用例，实跑结果 `Expected: 1 / Actual: 2`。（用例已删除，工作树已还原。）
- **为什么这是根级而非子级问题**：击杀那条路把同一个危险显式挡住了，`MonsterDeathSlice.cs:360-363`
  的注释写得很清楚——「Reported dead first … this is the one place that can tell a kill from its echo.
  Awarding before it would pay for both.」根 SC-5 要求通关的后果与击杀路径一致；改动前这条路付 0，
  重放无害，改动后有害。子成果自己把它标为「本次新引入的风险面」并留给上层。
- **要求**：结算必须只对同一个 run 生效一次。可选做法：结算成功后 `runs.End`（需先确认结果屏
  后续帧不再读 run）、或给 `DungeonRunState` 加一个「已结算」标记由 domain service 消费。
  无论选哪种，都要一条「第二次报告不再发放、且结算帧申报零」的回归测试。若最终判断为
  「实机不可构造、决定不修」，那也必须是一个**写下来的决定**（Docs 或根契约的 Assumptions），
  而不是三份文件里都没有的空白。

### BF-2：角色贴到经验上限时，两条申报路径都在报一笔没有入账的经验

- **位置**：`Server/src/Dfo.Server.Application/World/CharacterExperienceGrant.cs:47-60`
  与 `Advance`（`:78-89`）。
- **事实**：`Advance` 把总量封顶在 `thresholds[^1]`。当 `held == thresholds[^1]` 时
  `total == held`，`if (total != held && …)` 短路，**不写库**，但方法仍返回
  `Gained > 0`、`Total == held` 的 award。于是：
  - 击杀侧：`ExperienceBonusLines.Of(award)` 照常申报各行，而帧里的总量没变。客户端按
    「增量 = 新总量 − 旧总量」得 0，再减去申报的各行 → **基础值为负**，弹窗上的明细算不平。
    这正是 `Docs/ExperienceNoticeSlots.md` 全篇要防的那一种不平（第二轮探针读到的
    `基础:-27283` 就是同一个机制）。
  - 通关侧：结算帧照常申报 `ClearBase`/`RankBonus`，经验条一动不动。这与 `spec-003` 契约的
    Constraint「付出去的每一分都必须在某个帧里报得出来」正好反过来——报了没付出去的。
  - 部分封顶（`held < 上限 < held + gained`）是同一个缺陷更宽的窗口：写库发生了，但
    `award.Gained` 仍是未截断的值，申报的加成行可能超过客户端算出的增量。
- **测试缺口**：`CharacterExperienceGrantTests.ATotalCannotRunPastTheLastThreshold`（`:186-194`）
  从 690 起跳（仍会写库），且**只断言 `Total`，从不断言 `Gained`**，恰好绕开这一支。
- **要求**：让 award 报出**实际入账的增量**，而不是定价额——例如 `Advance` 之后按
  `total - held` 重算 `Gained`（并按比例或按顺序削减 contributions），或在 `total == held`
  时返回 `ExperienceAward.Nothing(level, held)`。附一条 `held == thresholds[^1]` 的单测，
  以及 `SetPlayResultHandlerTests` 里一条「award 非 null 但总量未变 → 结算帧申报零」的用例。
- **可达性说明**：需要角色贴近或到达 `ExpTable.tbl` 的最后一档，验收会话大概率碰不到。
  但它是根 SC-1 的可观察条件（「明细各项加基础值等于宣布的总数」）在集成后**唯一**不成立的场景，
  修复面很小，且此处一旦留下就没有下一个成果会去收。

## 四、Non-Blocking Observations

- **NB-1** `Server/src/Dfo.Server.Application/World/ExperienceBonusRules.cs:97-98`：
  `FromChannel` 的注释仍写着「the settlement at the end of a run pays nothing at all yet,
  so there is no base for a clear-side bonus to be a proportion of」。593a8916 之后这句是假的。
  **行为仍然正确**（通关侧不付频道奖励），但**正当理由已换人**——现在的理由是
  `Docs/ExperienceNoticeSlots.md` 第七节：结算帧二十三个固定槽未实测，付了报不出来。
  这是典型的「第二件写下的理由被第三件推翻而无人回来改」。`ExperienceBonusRulesTests.cs:127-129`
  的同一句话同样过期。
- **NB-2** `Server/src/Dfo.Server.Abstractions/World/ExperienceTuning.cs:50`：
  「The bonus rates -- premium terms, **the channel** -- are an operator's choice with no right
  answer」。频道那一半已经不是操作者的选择了，它回到了档案。
- **NB-3** `Server/src/Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs:505-513`
  硬写 `ExperienceBonusLines.None`，而击杀侧写的是 `.Of(award)`。今天两者等价（通关唯一的
  contribution 是 `Unlisted`）。将来给通关加任何一笔有槽的加成时，`.None` 会静默少报一行；
  改成 `ExperienceBonusLines.Of(paid.Award)` 行为不变而分叉不再可能。
- **NB-4** 通关跨等级线时新插入的 `AvailableQuestListMessage`，落在 `NOTI 0x0025` 与
  `NOTI 0x0023` 之间。`protocol/patterns/data/traces/a21-tutorial-ending-20260826.md:95-98`
  的参照抓包在这个位置是**恰好三帧**，没有第四帧。这一插入沿用了击杀路径的做法，合理，
  但不在任何抓包覆盖内——验收会话里请跑一次「通关时升级」，看结果屏是否照常画出。
- **NB-5** `Server/src/Dfo.Server.Host/appsettings.json`：`Channel:Channels[].Port` 与
  `Networking:GamePorts` 现在是两份互不校验的列表（发货默认一致：10/10010、11/10011）。
  对不上会出现「客户端看得见但没人在听」的频道，没有启动校验也没有日志会指出来。
  这是根 Scope In 的「实机能看见所需的部署配置」的一部分，验收前值得眼过一遍。
  （`spec-002` 已记为观察，此处只是把它带上根。）
- **NB-6** 根 Constraint「不可用的两个槽任何情况下都不得被写入」与实现不符：`LabelProbe`
  打开时 `MonsterDeathSlice.cs:242-243` 会把探针值写进 `+0x2F` 与 `+0x33`。
  这**不是本次引入的**（`ff413e0e` 处的探针已经这样写十四个槽），而且限制探针会直接摧毁
  产生本次全部证据的那件测量工具（掩码解码要求十四个槽都有互异值）。子契约 `spec-001` 已把它
  收窄为「**非探针情况下**恒为零」并按此测试。建议把根契约的这句改成同样的措辞，让文本与实现对齐。
- **NB-7** 探针本身的两个常量互不校验：`ExperienceDiagnostics.ProbeAmounts`（十四个 2 的幂）
  与 `ProbeTotal = 32766` 是两份独立手写值，`The_label_probe_still_pays_a_distinct_amount_into_every_slot`
  （`MonsterDeathCodecTests.cs:175-184`）只比对「生产常量 vs 用该常量写出的字节」，
  把数组改成全零也照样绿。测量方法本身（互异、bit0 空置、总和等于 `ProbeTotal`）无人断言。
  下次用它测结算帧二十三个槽之前值得补上。
- **NB-8** `ExperienceTuningSourceTests.TheShippedFileDocumentsTheDefaultsInCode` 没有断言
  `LabelProbe` 在发货文件里是 `false`（它断言了 `ChannelRate` 的缺席，却没断言这个）。
  一行即可闭合，防的是「探针带着发货配置上线」。
- **NB-9** `Server/src/Dfo.Server.Application/World/ExperienceBonusService.cs:10` 的类注释
  仍写「Each private method reaches one domain」，重构后该类已无私有方法。（`spec-002` 记过，未修。）
- **NB-10** `Docs/ExperienceNoticeSlots.md:131-132` 说「四个头部字段之后跟着二十三个固定槽」，
  实际中间还隔着一个 `u8`（帧 offset 16）。同文件第七节其余内容与
  `protocol/game/server-to-client/NOTI-0x0023.md` 的人工区（`+17 23×i32`，`+109` 起是计数列表）
  一致，`[server]` 段与 `[dungeon]` 段的引用我也逐行核对过发货档案，无误。
- **NB-11** 评分等级与评分比率「从两端配对」（八档 grade 与五档 rate，得分低于 50 一分不给）
  是一条**推断**，不是实测，也不在任何 SC 的证据要求里。`DungeonRankGrades.BandOf` 的注释
  给了理由，但一次实机 C/D 评级通关就能证伪它。建议写进验收清单。
- **NB-12** `GrantClearExperienceCommand` 没有像物品命令那样的「确实被注册了」断言
  （`CommandRegistrationTests.The_item_commands_are_registered` 的对照）。
  它**确实已注册**（`ProtocolModule.cs:110`），而且
  `Every_registered_command_handler_resolves_from_the_production_graph` 证明了它连同
  `IChannelExperienceSource`、`IDungeonRankGradeSource` 都能从真实图里构造出来（这也是本轮
  必须给测试图加 `AddLogging` 的原因）。缺的只是「漏注册会被发现」这一层。
- **NB-13** `MonsterDeathHandler` 至今没有 handler 级测试，而本轮给通关侧建了
  `SetPlayResultHandlerTests`。击杀侧的会话回写、跨级刷新、探针抬高总量三处逻辑因此仍无覆盖。
  这是两条路在**测试**上的不对称，与 BF-1 是同一种不对称的另一面。
- **NB-14（与本任务同型的残留，值得单独提出来）**
  `Server/src/Dfo.Server.Application/World/ExperienceBonusRules.cs:10-15` 写着：
  「the archive … declares none for a premium term: **every one of the fifty-two types in
  `etc/(r)premiumlist.etc` has an empty `[bonus]` section**」，并以此论证 `PremiumRates`
  属服务端政策、应当可配置。
  我用仓库自己的 `Dfo.Pvf` 直读 A21 档案核对：`etc/premiumlist.etc` 有 **53 个 `[type]` 块**，
  其中三个声明了经验数字——**type 5 `[exp] => 5`、type 12 `[exp] => 5`、type 84
  `[bonus exp] => 20`**（type 84 旁边还有 `[quest item drop rate] => 20`）。
  「空的 `[bonus]` 段」这个读法很可能来自把 `[attr]` 那一行（它的值就是 `[bonus]` 这个名字）
  误读成一个段落的开头——它是属性值，不是段落。
  **这是本任务开篇诊断的那句话的孪生**：根契约 Current Situation 第 2 条批评的正是
  「代码里写着『档案没有声明，属服务端政策』。这句话是错的」，而同一份文件里对 premium
  还留着同一句同样为假的话。而且服务端的槽位枚举里正好有 `GrowthContract`（成长之契约奖励），
  与 type 84 的 `[bonus exp] => 20` 是同一件东西的两半。
  **不阻断的理由**：这段代码在 `ff413e0e` 就是这样，不在 `ff413e0e..593a8916` 的改动面内
  （该文件本次的 diff 只删掉了 `ExperienceChannelPolicy` 并改了 `FromChannel` 的签名）；
  `PremiumRates` 缺省为空，今天一分不付，没有任何行为是错的。
  **建议**：按本任务给频道那一笔的同一套处理——把这三条读进来、把注释改成实测事实——
  作为下一个成果；至少先把注释改对，否则它就是下一次「花一天才发现的混淆」。
- **NB-15** `experience.json` 的两句话不准：`:2` 的顶层注释「每个值都默认为实测或还原出来的数」
  （`LabelProbe: false` 与 `PremiumRates: {}` 都不是）与「只有档案**不**声明的才放这里」
  （被 NB-14 证伪）。`:35` 新增的 `//ChannelReward` 描述档案的部分正确，但漏了一条实现事实：
  这笔加成**只在击杀时给，通关结算完全不给**（`ExperienceBonusRules.cs:108`）。
  按注释理解的运营者会以为通关也吃这 5%。另外注释把路径写死为
  `Resources/Channel/channel_info.etc`，而实际路径来自 `appsettings.json` 的 `Channel:ScriptPath`。
- **NB-16** 根契约 Scope Out 要求「上游当前版本与我们不一致的等级差惩罚表——只记录」。
  记录目前在 `ExperienceTuning.cs:79-81`，只有一句「`etc/worldmapexppenaltytable.etc` 是
  另一张表、为另一个目的，不可与本表混淆」。实读该文件：`[penalty table info]` 是
  −16…+20 逐级的经验惩罚率（0.3 / 0.7 / 0.8 / 0.9 / 1.0），键为「世界地图等级 − 角色等级」，
  值域里没有大于 1 的项（服务端表有 1.12 的溢价段）。**排除的结论站得住**，但那句话目前是
  断言而非证据；把这几个实测数值补进注释，读者才能复核。
- **NB-17** `.dgn` 的 `[story mode]` 里有 `[increase exp rate]`，51 个副本声明了非零值
  （多为 `0, 30`），全仓无人读取。剧情模式没实现，副本 144 声明的是 `0, 0`，
  所以不影响任何已验证样本——仅记为已知缺口。

## 五、根 Success Conditions 逐条判定

| SC | 判定 | 依据 |
|---|---|---|
| SC-1 每一笔已支付的加成落在客户端为它单列的那一行，明细与总数自洽 | **不满足（暂）** | 常规区间成立（九对三元组独立锚表比对、`Every_named_line_is_written_at_the_offset_the_client_draws_it_from`）；上限附近不成立 → BF-2。且契约要求的实机截图尚未产出 |
| SC-2 每个种类的槽位与叫法可追到实测出处，两个不自洽的槽被挡住 | **满足** | 枚举成员逐个带偏移与客户端叫法；`NamedLines` 与 `Docs` §4 逐字一致；测试锚表是独立转写而非读生产表；`DoesNotContain(0x2F/0x33)` + `The_slots_that_do_not_balance_are_never_written` |
| SC-3 频道奖励只在本局副本属于该频道声明的组时支付，比率取自该行 | **满足** | 解析、归属、双频道交叉、四个抓包样本端到端（62→3、72→3、126→6、146→7）；发货档案实读断言真跑 |
| SC-4 未声明 / 不在组内 / 读不到时不支付且不影响其余加成 | **满足** | 无行、五种残缺、重复声明、空目录各有测试；`Read()` 的异常口径覆盖 `FileChannelScriptSource` 全部抛出类型，`Parse` 无抛出路径 |
| SC-5 通关经同一发放件支付，跨等级线后果与击杀一致 | **不满足（暂）** | 发放件唯一、跨线刷新与补发列表帧都成立；但「重放拒绝」这一项与击杀路径不一致 → BF-1 |
| SC-6 结算帧报出的数字与实际支付一致，不再恒零 | **不满足（暂）** | 编解码断言充分（274 字节、offset 0/4 独立字面量、其余字节与空帧逐字相同）；但上限处会报出未入账的数 → BF-2。且帧头两字段的含义未实测，需实机截图 |
| SC-7 档案的数从档案读，服务端自定的仍可配置且缺省不变 | **满足（本次变更面内）** | 四张清算表 + 频道两段全部读档案；`experience.json` 与 `ExperienceTuning.Default` 逐值一致，文件可删；`ChannelRate` 缺席被断言；区间内没有任何既有可配置默认值改过数值。**注**：`PremiumRates` 仍是「档案已声明却做成配置」的一例（NB-14），但那段代码不在本次改动面内，故不影响本次判定 |
| SC-8 全套测试通过、构建零警告零错误 | **满足** | 本人独立复跑：build 0/0，1577 通过 / 2 跳过 / 0 失败 |

## 六、必须靠那一次实机会话才能关闭的 SC

- **SC-1**——契约的 Required evidence 明写「实机一次击杀的通告截图，明细自洽」。
  编解码测试能证明字节落点，**证明不了客户端画出来的那一行叫什么、加起来等不等于宣布的总数**。
  九个叫法虽然来自 2026-08-28 的探针，但那一轮之后槽位映射进了生产表，映射本身没有再被客户端确认过。
- **SC-6**——契约明写「实机通关截图」，而且这里有一个**只有截图能回答**的问题：
  帧头两个字段（offset 0 / offset 4）哪个是通关基数、哪个是评分奖励，取的是上游命名，未经实测。
  通关基数远大于评分奖励，一眼即可判断有没有写反。

其余六条（SC-2、SC-3、SC-4、SC-5、SC-7、SC-8）在**证据类型**上都不需要实机：
契约给它们要求的是测试与构建输出，已经具备。SC-5 与 SC-6/SC-1 当前的「不满足」是 BF-1/BF-2
的代码问题，不是缺实机。

顺带列进那一次会话值得多看的三件（都不构成 SC 判据）：
1. 通关时正好升级 → 结果屏是否照常（NB-4）；
2. 同一张 144 图，频道 10 有「赛丽亚的祝福奖励」一行、频道 11 没有（Docs/HostStartup.md 的正反面）；
3. 一次 C/D 评级的通关，看评分奖励是否真的为零（NB-11 的配对推断）。
