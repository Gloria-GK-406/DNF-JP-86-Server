---
kind: rks-review
verdict: approved
---

# 根审查：副本跑动的经验补完（`ff413e0e..04345c34`）

**本件为第二轮，覆写同一路径上的首轮结论。** 首轮（判据 `ff413e0e..593a8916`）给出
`changes_required` 与两条阻断项；本件覆盖补做后的完整变更集，涵盖第四个提交 `04345c34`。
审查者以全新上下文独立进行，未采信执行者或控制器的结论。

本件仍不是三个子成果的复审——判据是**集成结果**对根 TaskContract 八条 SC 与五条 Constraints
的符合性，以及三件之间的接缝。

## 一、审查范围与完整性

- 变更集 = `ff413e0e..04345c34` 四个提交。第四个 `04345c34`「Close the two gaps the whole run
  showed that no single piece could」触及 7 个产品/测试文件（`git diff --stat 593a8916..04345c34`
  另含 2 个流程件）。
- 工作树接手时干净。审查中我做了两次临时变异（把 `ClearDungeonRewardNotification` 的取数改回
  定价量、把 `TrySettle` 改回 `Find`），每次都 `git checkout` 还原；交回时
  `git status --short` **无任何输出**，产品代码与 `04345c34` 逐字节一致。
- 独立复跑（`PVF_ARCHIVE_PATH` 指向 A21 档案）：
  - `dotnet build Server/86JP.Server.slnx` → **0 个警告，0 个错误**。
  - `dotnet test Server/86JP.Server.slnx` → **1583 通过 / 2 跳过 / 0 失败**
    （51 + 331 + 60 + 4 + 160 + 737 + 240）。与执行者报的数一致。
    两条跳过仍是「档案缺失时应报错」的用例，档案在场故跳过——依赖档案与发货文件的
    `SkippableFact` 全部真跑。
- 三个子成果均为 sdd-lite、无 plan 件、未提供对抗挑战集，故本件不含 `Challenge Dispositions`。

**一处流程记账，交还控制器**：`04345c34` 把首轮的审查制品与 `multi-spec-map.md` 一并提交了，
但 map 里 `workspace.baseline` 仍写 `593a8916`、SC-8 证据仍写「1577 通过」。修复提交之后没有回填，
这两处现在是过期的。不影响代码判定。

## 二、两条阻断项的复核

### BF-1（重放结算重复付款）——已闭合

- **做法核对**：`IDungeonRunDomainService.cs:199` 新增 `TrySettle`，`DungeonRunState`
  加 `IsSettled`（`:22`，加在位置参数末尾并带缺省值，既有构造点与 `with` 表达式都不受影响）；
  `DungeonRunDomainService.cs:105-120` 的实现与 `TryMove` / `TryTakeGroundDrop` /
  `TryReportActorDead` 是同一个 CAS 循环形状——读、判、`with`、`TryUpdate` 比对原引用、失败重试。
  `Begin` 构造的是全新 state，所以再入一局会重置 `IsSettled`。
  `DungeonClearSlice.cs:391` 由 `runs.Find` 改为 `runs.TrySettle`。
- **「结算不结束 run」这个取舍是对的**：`DungeonRunState.MayLeaveCurrentRoom` 对 Boss 房为 false，
  结算屏到门口之间角色仍在副本里，且后续帧要读 run 的 layout；`End` 仍只由
  `GiveUpGameHandler`（`DungeonClearSlice.cs:142`）与 `DungeonSelectionSlice.cs:202` 调用。
  `SettlingDoesNotEndTheRun` 把这一点钉住了。
- **独立变异验证**：我把 `runs.TrySettle` 改回 `runs.Find` 重跑 Protocol 测试 →
  `ARunIsPaidOnceHoweverManyTimesItsResultIsReported` **1 红**（330 通过 / 1 失败）。守卫真的被测住。
- **与首轮实证的对照**：首轮我临时加的「连调两次、统计总线被问次数」用例得到 2；
  现在的正式用例连报三次得到 1，并额外断言只发出一份结算帧。

### BF-2（到顶时报了没入账的经验）——已闭合

- **做法核对**：`CharacterExperienceGrant.cs:68-77`，`landed = total - held`；
  `landed == gained` 时行为完全照旧，被截断时返回 `new ExperienceAward(landed, [], landed, …)`。
- **逐场景推演（自己算过一遍，不是照抄测试）**，测试表 `Thresholds = [100, 250, 450, 700]`：
  - **未截断**：与改动前逐字段相同，三条既有路径无行为变化。
  - **部分截断**（level 4 / 690，定价 1,000,000 + 50,000）：`total = 700`，`landed = 10`，
    写库发生，`nextLevel` 仍照常推进到 5、`LeveledUp` 为真。award 报 `(Base 10, Gained 10, [])`。
    通告：客户端算出的增量 = 700 − 690 = 10，无申报行，基础值 = 10——**自洽**。
    结算帧：`(award.Base, award.Gained - award.Base)` = `(10, 0)`，两项之和 = 实际增量——**自洽**。
  - **已在顶上**（level 5 / 700）：`total == held` → 不写库（`LastWrite` 为 null），
    `landed = 0`，award 报 `(0, [], 0, 5, 700, LeveledUp false)`。通告增量 0、无申报行、
    基础值 0；结算帧 `(0, 0)`。**自洽**，且与 `ExperienceAward.Nothing` 等价。
  - **`clearBase == 0`**（副本基准等级不在任务表内）：`gained == 0` 提前返回 `Nothing`，
    结算帧 `(0, 0)`。
  - **`award is null`**：仍走 `(0, 0)` 分支。
  - `award.Gained - award.Base` 不会下溢：`Gained` 由 `@base` 起用 `AddSaturating` 累加，
    恒 `>= Base`；截断分支两者相等。
- **修在共享发放件上，因此三条路一起受益**：击杀侧无需改动（`ExperienceBonusLines.Of(award)`
  在截断时自然得到 `None`，与总量一致）；交任务侧 `QuestFinishOutcome` 报的 `award.Gained`
  现在也是落地量而非定价量。这正是「一处发放、一处修」应有的样子。

#### 对「截断时丢掉 contributions」这个取舍的裁定：可接受，且我认为是唯一站得住的一种

四个候选做法，逐个看它们会说什么谎：

1. **保留 contributions 不变**——申报行之和可能超过客户端算出的增量，基础值为负。这正是 BF-2 本身。
2. **按比例摊分**——发明数字。天花板不是按比例吃掉各份的，档案里也没有任何东西说它是。
3. **按某个顺序截掉最后几笔**——顺序本身是发明的（`Collect` 的返回顺序是实现细节，不是规则）。
4. **整块归给这次场合**（现在的做法）——不发明任何数字，总数正确，只是明细变粗。

第 4 条与 `Docs/ExperienceNoticeSlots.md` 第六节第 2 条已经批准过的 `Unlisted` 取舍**同源**：
「宁可少一行，不可写进一个会让明细算不平的槽」。代码注释也把这个类比写了出来。**同意执行者的判断。**

一条留痕上的代价，记录而不阻断：`ExperienceAward.ToString()` 在截断时打印
`landed (base landed)`，看不出「本来还有一笔加成、被天花板吃掉了」；而
`SetPlayResultHandler` 的日志同时打印 `paid.ClearBase`/`paid.RankBonus`（定价量）与
`paid.Award`（落地量），截断时两者不一致——这其实是有用的信息，但读日志的人得知道该这么读。

#### 对「不会破坏击杀路径四个抓包样本断言」的独立确认：确认，且理由是结构性的

不是「跑下来没红」而已：`GrantKillExperienceCommandTests` 用的是自带的
`PricingGrant`（`:125-140`）——一个只做定价、直接把 `(@base, contributions, gained)`
原样组装成 award 的替身，**根本不经过 `CharacterExperienceGrant`**。
那四条 `ACapturedKillPaysWhatTheWireCarried`（62→65、72→75、126→132、146→153）
检验的是 `MonsterExperienceRules` × `ExperienceBonusService` 的定价与落点，
与天花板逻辑没有交集。全量 1583 全绿只是旁证，这一条才是保证。

## 三、Non-Blocking Observations

首轮的 NB-14 已闭合，其余沿用，另新增三条。

- **NB-18（新，BF-1 的残留形状）** 守卫把「付款」和「回话」合在了一起，而击杀路径把两者分开。
  `MonsterDeathHandler` 在 `TryReportActorDead` 返回 null 时**仍然回帧**（经验帧报当前存量 +
  死亡确认），只是不付；`SetPlayResultHandler` 在 `TrySettle` 返回 null 时**一帧不回**。
  若客户端因为没收到答复而重发，击杀那条路能自愈，结算这条路会把玩家留在空白结算屏上。
  与击杀完全对齐的形状是：用 `Find` 决定「回不回话」、用 `TrySettle` 决定「付不付款」——
  重发时仍送 `PLAY_RESULT` + 经验帧（当前存量）+ `CLEAR_DUNGEON_REWARD(0,0)`，只是不再发放。
  不阻断的理由：这一支按既有认识实机不可构造，且改动前的行为（回话 + 重复付款）更糟。
- **NB-19（新，BF-2 的覆盖缺口，有变异证据）** BF-2 的修复有两半，**只有一半被测住**。
  发放件那一半有 `AnAwardClampedByTheCeilingReportsOnlyWhatLanded` 与
  `ACharacterAtTheCeilingIsAwardedNothing`（断言 `Gained`、`Base` 与 `Assert.Empty(Contributions)`，
  形状上必然咬住）。**处理器那一半没有**：我把 `DungeonClearSlice.cs:533` 的
  `new ClearDungeonRewardNotification(award.Base, award.Gained - award.Base)`
  变异回 `(paid.ClearBase, paid.RankBonus)`，Protocol 测试 **331 全绿**。
  原因是 `FixedAward` 造出的 award 里 `Base == ClearBase`、`Gained - Base == RankBonus`，
  两种取数在这个夹具下给出同一组数字。
  闭合方式很小：给 `FixedAward` 加一个「award 被截断」的形态（`Base = Gained = landed`，
  而 `ClearExperienceResult` 仍带未截断的定价量），断言结算帧报 `(landed, 0)`。
  **行为本身是对的**（上面逐场景推演过），缺的只是回归保护——所以是观察而非阻断。
- **NB-20（新）** `TrySettle` 现在发生在技能镜像检查**之前**（`:391` vs `:400`），
  所以镜像为空的那一支会把这一局的结算名额消耗掉却不付款。代码自己论证了那一支
  对已发布会话不可达（入场就拒绝发布无镜像的角色），且「报告发生过」本身消耗名额也说得通，
  这里只记录顺序上的事实。
- **NB-1（沿用）** `Server/src/Dfo.Server.Application/World/ExperienceBonusRules.cs:102-104`：
  `FromChannel` 的注释仍写着「the settlement at the end of a run pays nothing at all yet,
  so there is no base for a clear-side bonus to be a proportion of」。`593a8916` 之后这句是假的。
  行为仍然正确（通关侧不付频道奖励），但正当理由已换成
  `Docs/ExperienceNoticeSlots.md` 第七节：结算帧二十三个固定槽未实测，付了报不出来。
- **NB-2（沿用）** `Server/src/Dfo.Server.Abstractions/World/ExperienceTuning.cs:50`：
  「The bonus rates -- premium terms, **the channel** -- are an operator's choice」。
  频道那一半已经回到档案。
- **NB-3（沿用）** `DungeonClearSlice.cs:517` 硬写 `ExperienceBonusLines.None`，
  击杀侧写的是 `.Of(award)`。今天等价；将来给通关加任何一笔有槽的加成时 `.None` 会静默少报一行。
- **NB-4（沿用）** 通关跨等级线时插入的 `AvailableQuestListMessage` 落在 `NOTI 0x0025`
  与 `NOTI 0x0023` 之间，而
  `protocol/patterns/data/traces/a21-tutorial-ending-20260826.md:95-98` 的参照抓包
  在这个位置是**恰好三帧**。沿用击杀路径的做法，合理但无抓包覆盖——验收时跑一次「通关时升级」。
- **NB-5（沿用）** `appsettings.json` 的 `Channel:Channels[].Port` 与 `Networking:GamePorts`
  是两份互不校验的列表（发货默认一致：10/10010、11/10011）。对不上会出现
  「客户端看得见但没人在听」的频道，无启动校验也无日志。验收前值得眼过一遍。
- **NB-6（沿用）** 根 Constraint「不可用的两个槽任何情况下都不得被写入」与实现不符：
  `LabelProbe` 打开时 `MonsterDeathSlice.cs:242-243` 会写 `+0x2F` 与 `+0x33`。
  不是本次引入（`ff413e0e` 处已如此），且限制探针会摧毁产生全部证据的那件测量工具。
  子契约 `spec-001` 已收窄为「非探针情况下恒为零」；建议根契约同步措辞。
- **NB-7（沿用）** `ExperienceDiagnostics.ProbeAmounts` 与 `ProbeTotal = 32766`
  是两份独立手写值，互不校验；`The_label_probe_still_pays_a_distinct_amount_into_every_slot`
  只比对「生产常量 vs 用该常量写出的字节」。下次用它测结算帧二十三个槽之前值得补上。
- **NB-8（沿用）** `ExperienceTuningSourceTests.TheShippedFileDocumentsTheDefaultsInCode`
  没有断言发货文件里 `LabelProbe` 为 `false`。
- **NB-9（沿用）** `ExperienceBonusService.cs:11` 的类注释仍写「Each private method reaches
  one domain」，该类已无私有方法。
- **NB-10（沿用）** `Docs/ExperienceNoticeSlots.md:131-132` 说「四个头部字段之后跟着二十三个
  固定槽」，实际中间还隔着一个 `u8`（帧 offset 16）。
- **NB-11（沿用）** 评分等级与评分比率「从两端配对」（八档 grade 配五档 rate，得分低于 50
  一分不给）是推断而非实测。一次实机 C/D 评级通关即可证伪，建议写进验收清单。
- **NB-12（沿用）** `GrantClearExperienceCommand` 没有「确实被注册了」的专项断言；
  它确已注册（`ProtocolModule.cs:110`），且
  `Every_registered_command_handler_resolves_from_the_production_graph` 证明它能从真实图里构造。
- **NB-13（沿用）** `MonsterDeathHandler` 至今没有 handler 级测试，而通关侧有
  `SetPlayResultHandlerTests`。击杀侧的会话回写、跨级刷新、探针抬高总量三处仍无覆盖。
- **NB-14 —— 已闭合。** `ExperienceBonusRules.cs:9-22` 的注释已改成如实陈述：
  三个 premium type 带经验声明（两个 `[exp]` 为 5、一个 `[bonus exp]` 为 20），并写明
  接线需要「档案声明 → 账号行 type」的映射、属下一个成果。我用仓库自己的 `Dfo.Pvf`
  直读 A21 档案复核过：`etc/premiumlist.etc` 共 53 个 `[type]` 块，
  type 5 `[exp] => 5`、type 12 `[exp] => 5`、type 84 `[bonus exp] => 20`——**与新注释完全对上**。
  行为未改（`PremiumRates` 缺省为空，一分不付），符合「不发明规则」的既定纪律。
- **NB-15（沿用，收窄）** `experience.json:2` 的顶层注释仍说「只有档案**不**声明的才放这里」，
  而 `PremiumRates` 现已知是档案有声明的（代码注释已改对，JSON 这句还没）。
  另外 `:35` 的 `//ChannelReward` 漏了一条实现事实：这笔加成**只在击杀时给，通关不给**；
  并把路径写死为 `Resources/Channel/channel_info.etc`，而实际路径来自 `Channel:ScriptPath`。
- **NB-16（沿用）** 根契约 Scope Out 要求「等级差惩罚表只记录」。记录目前在
  `ExperienceTuning.cs:79-81`，是一句断言而非证据；把 `etc/worldmapexppenaltytable.etc`
  的实测值（`[penalty table info]`，−16…+20 逐级，0.3 / 0.7 / 0.8 / 0.9 / 1.0，无大于 1 的项）
  补进注释，读者才能复核「它是另一张表」这个结论。
- **NB-17（沿用）** `.dgn` 的 `[story mode]` 里有 `[increase exp rate]`，51 个副本声明了非零值，
  全仓无人读取。剧情模式没实现，副本 144 声明 `0, 0`，不影响任何已验证样本——仅记为已知缺口。

## 四、根 Success Conditions 逐条判定

| SC | 判定 | 依据 |
| --- | --- | --- |
| SC-1 每一笔已支付的加成落在客户端为它单列的那一行，明细与总数自洽 | **满足（代码面）；实机证据待补** | 九对三元组独立锚表比对、`Every_named_line_is_written_at_the_offset_the_client_draws_it_from`；上限处的不自洽已由 BF-2 闭合，四种场景逐一推演均自洽。契约要求的实机截图尚未产出 |
| SC-2 每个种类的槽位与叫法可追到实测出处，两个不自洽的槽被挡住 | **满足** | 枚举成员逐个带偏移与客户端叫法；`NamedLines` 与 `Docs` §4 逐字一致；测试锚表是独立转写而非读生产表；`DoesNotContain(0x2F/0x33)` 与 `The_slots_that_do_not_balance_are_never_written` |
| SC-3 频道奖励只在本局副本属于该频道声明的组时支付，比率取自该行 | **满足** | 解析、归属、双频道交叉、四个抓包样本端到端（62→3、72→3、126→6、146→7）；发货档案实读断言真跑 |
| SC-4 未声明 / 不在组内 / 读不到时不支付且不影响其余加成 | **满足** | 无行、五种残缺、重复声明、空目录各有测试；异常口径覆盖 `FileChannelScriptSource` 全部抛出类型，`Parse` 无抛出路径 |
| SC-5 通关经同一发放件支付，跨等级线后果与击杀一致 | **满足** | 发放件唯一；跨线刷新与补发列表帧；重放拒绝已与击杀对齐（`TrySettle` 与 `TryReportActorDead` 同形），变异验证 1 红 |
| SC-6 结算帧报出的数字与实际支付一致，不再恒零 | **满足（代码面）；实机证据待补** | 274 字节、offset 0/4 独立字面量、其余字节与空帧逐字相同；申报改为从 award 取数，四种场景两项之和恒等于实际增量。帧头两字段的含义未实测，需实机截图 |
| SC-7 档案的数从档案读，服务端自定的仍可配置且缺省不变 | **满足** | 四张清算表 + 频道两段全部读档案；`experience.json` 与 `ExperienceTuning.Default` 逐值一致，文件可删；`ChannelRate` 缺席被断言；区间内无既有默认值改动。`PremiumRates` 的性质已如实记录并划给下一个成果（NB-14） |
| SC-8 全套测试通过、构建零警告零错误 | **满足** | 本人独立复跑：build 0/0，1583 通过 / 2 跳过 / 0 失败 |

## 五、必须靠那一次实机会话才能关闭的 SC

**仍然只有 SC-1 与 SC-6，`04345c34` 没有改变这一点。** 两条阻断项的修复都落在服务端自己
说得清的范围内（重放守卫、落地量口径），既不新增也不消除任何需要客户端才能回答的问题。

- **SC-1**——契约的 Required evidence 明写「实机一次击杀的通告截图，明细自洽」。
  编解码测试能证明字节落点，证明不了客户端画出来的那一行叫什么、加起来等不等于宣布的总数。
- **SC-6**——契约明写「实机通关截图」，而且这里有一个只有截图能回答的问题：
  帧头两个字段（offset 0 / offset 4）哪个是通关基数、哪个是评分奖励，取的是上游命名、未经实测。
  通关基数远大于评分奖励，一眼即可判断有没有写反。

其余六条在**证据类型**上都不需要实机：契约给它们要求的是测试与构建输出，已经具备。

那一次会话值得顺手多看的三件（都不构成 SC 判据）：

1. 通关时正好升级 → 结果屏是否照常（NB-4）；
2. 同一张 144 图，频道 10 有「赛丽亚的祝福奖励」一行、频道 11 没有；
3. 一次 C/D 评级的通关，看评分奖励是否真的为零（NB-11 的配对推断）。
