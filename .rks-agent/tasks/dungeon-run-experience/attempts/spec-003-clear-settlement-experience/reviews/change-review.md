---
kind: rks-review
verdict: approved
---

# Change Review: 通关结算发经验（第二轮，覆写第一轮）

范围 `change`。基线 `3d22d384`，被审对象是分支 `dungeon-run-experience` 的整个未提交工作树。
依据为 `attempts/spec-003-clear-settlement-experience/TaskContract.md`（revision 1）与同目录
`spec.md`，以及 `Docs/ExperienceNoticeSlots.md` 第七节。sdd-lite 无 plan。
执行者报告只作旁证，下面每一条结论都由本次独立复算得出。

第一轮判定 `changes_required`（B-1 付 0 报 618；B-2 两条空分支零覆盖）。本轮改动集相对上一轮
只有两个文件变化——`Server/src/Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs` 与
`Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonClear/SetPlayResultHandlerTests.cs`；
其余四个新增源码文件的 md5 与第一轮逐字节一致（`DungeonRankContent.cs`
`b0f53eb1…`、`PvfDungeonRankGradeSource.cs` `7f9ef08a…`、`ExperienceParameterContent.cs`
`2d1d0441…`、`GrantClearExperienceCommand.cs` `900b650b…`），其余 diff 文件的 `--stat` 也未变。
因此本轮把复核重量放在两个改动文件与「新路径是否被引入」上，第一轮已用变异证明过的部分保留结论。

## 复算方式

构建与全量测试本轮重跑（`PVF_ARCHIVE_PATH=/d/project/DNF/DNFClient-A21-protocol/Script.pvf`）：
构建 0 警告 0 错误；测试 **1577 通过 / 2 跳过 / 0 失败**，与执行者报告一致。
两个跳过项仍是那两个「档案缺失」用例，与本改动无关；所有读档案的新测试都真实执行。
`Dfo.Server.Host.Tests` 本轮 4/4，第一轮那次单点失败确系并行端口争用，与改动无关。

本轮另做两组变异（核对后已还原，工作树 md5 与还原前一致）：

- **变异 A**：把结算帧的申报改回无条件
  （`frames.Add(new ClearDungeonRewardNotification(paid.ClearBase, paid.RankBonus));`）——
  `AnAwardThatDidNotHappenDeclaresNothing` 变红。**B-1 的新判据吃重**，与执行者自查一致。
- **变异 B**：在 `mirror is null` 分支的早退之前插入一次真实的 `commandBus.SendAsync`（即「照付」）——
  `WithoutASkillMirrorTheClearIsNeitherPaidNorReported` 因 `Assert.False(bus.WasAsked)` 变红。
  **「根本没被问过」这条断言是真断言，不是摆设。**

## 上一轮阻塞项的处置

### B-1 结算屏申报未到账的经验 —— 已解决

`DungeonClearSlice.cs` 现在写：

```csharp
frames.Add(award is null
    ? new ClearDungeonRewardNotification(0, 0)
    : new ClearDungeonRewardNotification(paid.ClearBase, paid.RankBonus));
```

判据取的是**发放结果**而不是命令算出来的金额，这正是第一轮指出的分界：`ClearExperienceResult`
的两个数是「这一局值多少」，`Award` 才是「有没有发生」。注释把这个区分写清楚了。
`AnAwardThatDidNotHappenDeclaresNothing` 同时断言了另一半——经验帧报的仍是入场值
1000 / 等级 3，也就是屏上两处对同一件事说的话一致。

### B-2 两条空分支零覆盖 —— 已解决

`Award == null` 与 `mirror == null` 各有一条用例，且两条都经上面的变异证明会因对应缺陷变红。
夹具的改法也干净：`FixedAward` 多了 `awarded` 与 `WasAsked` 两个开关而不是新写一个替身，
`Entry(withMirror)` 参数化而不是复制一份 fixture。

## 对本轮三个提问的回答

### 一、`mirror is null` 改成「不付」是否恰当 —— 恰当

判断有据，不只是取舍：

1. **这条分支对已发布会话实际上不可达。** `SelectCharacterHandler.cs:229` 在发布前调
   `BuildPrePublicationMessages`，其中 `SelectCharacterHandler.cs:384-385` 是
   `_ = result.CharacterEntry.Skills.ProtocolMirror ?? throw new InvalidDataException(...)`；
   `StateSynchronizationPayloadCodec.Entry.cs:341` 的 `EncodeSkills` 也会 `throw Missing(...)`。
   也就是说，一个能走到结算的会话必然带着非空 mirror。
   两种做法的**实际代价都是零**，那就该选把不变量守死的那个。
2. **它与本 Spec 的 RQ-007 相容，另一种做法不相容。** RQ-007 要求「结算的经验帧报出的是支付
   之后的等级与总量」。没有 mirror 就没有经验帧，照付就必然有一次结算无法满足 RQ-007；
   拒付则「凡是付了的结算都满足 RQ-007」保持为真。
3. **两个先例确实指向相反方向，但击杀那个更贴。** `FinishQuestHandler.cs:144-166` 是照付后
   跳过经验帧——因为交任务的完成是一次性状态迁移，任务已经被消费，拒付只会更糟；
   `MonsterDeathHandler.cs:340-360` 是拒付，且它连 `TryReportActorDead` 都没调，
   所以那次死亡可以被客户端重报，拒绝是可恢复的。结算落在两者之间：它像交任务一样
   不可重来（`SET_PLAY_RESULT` 一局只来一次，之后 `GIVEUP_GAME` 就结束 run），
   又像击杀一样没有被消费掉的一次性资源。在这个不可达分支上，
   「宁可不付也不报不出来」与 Spec 的 Constraints 同向，取它是对的。

一句提醒而非异议：这条拒付**不可挽回**（不像击杀那次可以重报），
所以它成立的前提是上面第 1 条——mirror 不可能为空。哪天入场路径放松了那个保证，
这条分支就从「防御」变成「会吞掉一整局经验」，届时应回到交任务那种做法。
建议把这层依赖写进分支注释里，现在的注释只讲了「没有帧可以报」。

### 二、是否引入了新的路径问题 —— 未发现

逐条查过：

- **`mirror` 局部变量的取值时机**：现在在支付之前取，之后 `result` 被 `with` 重建。
  但重建只动 `Character.Progression` 与 `CharacterEntry.Public.Extras`，
  `QuestOfferRefresher.RefreshAsync` 只动 `World.Content` 与任务日志，
  `Skills.ProtocolMirror` 不受影响，所以捕获的 `mirror` 仍然有效。
  击杀路径也是先捕获再发放，一致。
- **经验帧由条件添加改成集合初始化器里的固定项**：因为守卫在前，编译期流分析已把 `mirror`
  收窄为非空，构建 0 警告可证。帧序仍是 `PlayResult → Experience →（AvailableQuestList）→
  ClearDungeonReward`，由 `ALevelCrossedAtSettlementRefreshesTheOfferList` 固定。
- **早退分支的帧数**：发 `PlayResult` + `ClearDungeonReward(0, 0)` 两帧。
  改动前 mirror 为空时也正好是这两帧（经验帧本来就是条件添加的），所以对客户端没有帧数回归。
- **早退分支不碰 `runtimeSessions` 与 `runs`**：没有付款也就没有要纠正的会话状态，正确。
- **`award is null` 时不 `ReplaceExact`、不刷任务表**：与「什么都没发生」一致，正确。
- **`award` 非空但 `Gained == 0`**（基准等级越界，`ExperienceAward.Nothing`）：
  `paid.ClearBase` 与 `paid.RankBonus` 本身就是 0，申报 0，自洽。
- **溢出**：`Gained` 用 `AddSaturating`，`BaseWorth` 饱和到 `uint.MaxValue`（第一轮探针实证）。

唯一的残留见 O-2，它属于共享发放件的既有性质而不是本次引入。

### 三、`run` 没有被 `End` —— 记忆正确

全仓 `runs.End(` 只有两处：`DungeonClearSlice.cs:142`（`GiveUpGameHandler`，CMD 0x002A
`GIVEUP_GAME`，注释写明「it is the only thing in this server that ends a run」）与
`DungeonSelectionSlice.cs:202`（`BackToVillageHandler`，防御性的第二处）。
`SetPlayResultHandler` 在基线 `3d22d384` 与现在都没有 `End` 调用——diff 里也没有被删掉的。
所以 mirror 为空时早退不会漏掉任何 run 收尾：客户端看完结算屏照样发 `GIVEUP_GAME`。

## 第一轮已核对项（结论保留）

以下八项在第一轮已独立复算并逐项变异验证，本轮相关文件 md5 未变，结论沿用：

1. **两张表从相反两端配对**——`BandOf` 的 `covered - 1 - place` 方向正确
   （99→0.2 … 50→0.05，49 无）；把它改成 `return place;` 会让
   `DungeonClearExperienceRulesTests` 5 个失败。档案实值由未跳过的 `SkippableFact` 实读确认。
2. **没有付了报不出来的**——`Gained = Base + Σ贡献` 在构造上等于 `ClearBase + RankBonus`；
   塞一条 `phantom channel` 贡献会让 `GrantClearExperienceCommandTests` 2 个失败。
3. **`ExperienceParameters` 的同名 `init` 属性**——归一化确实发生（探针实证：位置传 `null`
   与省略都得到非空空表，`with` 与 `Deconstruct` 均正常）；生产端唯一构造点
   `PvfExperienceParameterSource.cs:93`，击杀定价不受影响。
4. **`PvfDungeonRankGradeSource` 的 section 隔离**——削掉 section 判断会收到 13233 个整数而非 8 个，
   且首几个是 `[1, 1, 139000, …]`，证明 `[rank grade]` 不是文件首节，隔离是必需品。
5. **与击杀路径的一致性**——会话纠正、跨等级线刷任务表、帧序均一致；本轮 mirror 分支也已对齐。
6. **结算帧**——274 字节构成逐字节复算无误，付款只动前 8 字节，不碰 109/110/155 三个长度字段；
   `Paying_leaves_every_other_field_exactly_as_an_empty_run_leaves_it` 用
   `4_000_000_000 / 800_000_000` 做整段比对，是真检查。与上游当前版 `BuildClearDungeonReward` 逐段一致。
7. **测试质量**——档案侧、规则侧、编解码侧、命令侧的用例都吃重（各有变异佐证）；
   处理器侧本轮补齐后同样吃重。
8. **`Docs/ExperienceNoticeSlots.md` 第七节**——没有把未知说成已知。
   上游断言逐条核实属实：`ServerS4A21` 的 `15e536e` 版是 `for (int i = 0; i < 25; i++)` 且
   `else if (i == 23) value = channelExp; // 槽位24: 频道奖励`，当前版
   `FixedRewardSlotCount = 23`（25 与 23 确实不同，下标 23 在这份帧里越界）；
   `b82bd44` 删去 `int channelExp = 0`，删之前它已是 `_ = channelExp;`。

## Non-Blocking Observations

- **O-1**：`SetPlayResultHandlerTests.NothingIsPaidThatTheScreenCannotAccountFor` 的第一条断言
  `Assert.Equal(ClearBase + RankBonus, reward.ClearBase + reward.RankBonus)` 仍是同义反复——
  两边都出自同一个 `FixedAward` 常量，且 `TheResultScreenReportsWhatWasPaid` 已分别断言过这两个字段。
  真正吃重的对照断言在 `GrantClearExperienceCommandTests` 那条同名用例（已变异验证），
  以及本轮新增的 `AnAwardThatDidNotHappenDeclaresNothing`。
  建议要么改断言「两项之和 == 经验帧总量的增量」这一个真命题，要么换个不那么承诺的名字。
  第二条断言（总量取自 `award.Total`）有效。
- **O-2（本轮新记）**：经验到顶时会残留同一形状的不一致。
  `CharacterExperienceGrant.Advance` 把总量封顶在 `thresholds[^1]`，于是 `total == held` 时
  `if (total != held && ...)` 短路、不写库，却仍返回 `Gained > 0` 的 award，
  结算帧因此会申报一笔总量没有变化的经验。这属于共享发放件的既有性质
  （击杀与交任务同样如此），而本 Spec 的 Constraints 明写「不得出现第二份加经验的实现」，
  结算这一侧无法干净地绕开它，故非本次阻塞。但结算是第一个把「这一局值多少」单独画在屏上的地方，
  所以到顶的角色会第一个看见它。留给发放件自己的后续成果。
- **O-3**：重复的 `SET_PLAY_RESULT` 会重复付款。击杀靠 `TryReportActorDead` 的「同一具尸体只认一次」
  挡住重放；结算这条路上 `runs.Find` 不消费任何东西，run 要等 `GIVEUP_GAME` 才结束。
  改动前这条路付 0，重放无害；现在有害。目前没有客户端会重发的证据，
  且按既有认识客户端多在本地先拦，故列为观察——但这是本次改动新引入的风险面。
- **O-4**：`mirror is null` 早退时只记了 connection 与 character，不再记
  `clearTime` / `DungeonId` / `Rank`（原实现的 `LogInformation` 在早退之后）。
  warning 本身已能定位，只是这一局的其余事实不再留痕。
- **O-5**：`Docs` 第七节说「二十三个固定槽的语义我们一个都没实测」，属实；可补一句：
  上游当前版依客户端反汇编（`CParty::clear_reward` / `getClearRewardBonusExp`）给其中 5 个槽
  写了名字（下标 2 黑钻、5 宠物、7 冒险团、9 成长之契约、18 杀怪成长之契约），
  并注明「槽位 1-13 通关额外奖励、14-25 杀怪额外奖励」。这不是证据，但下一次实测有它当起点
  能省一次通关。另外第七节说「四个头部字段之后跟着二十三个固定槽」，实际中间还隔着一个字节。
- **O-6**：`EncodeClearDungeonReward` 把评分奖励按 `Math.Min(rankBonus, int.MaxValue)` 截断后写成 `i32`。
  以档案现有最高比率 0.2 计上限约 8.6 亿，现在够不着截断；比率一旦调到 0.5 以上，
  超出部分会静默丢在屏上。沿用的是原本 `writer.Write(0)` 的有符号写法，只记一笔。
- **O-7**：`DungeonClearExperienceRules` 在 `Dfo.Server.Abstractions`（与 `MonsterExperienceRules` 同处），
  测试却在 `Dfo.Server.Application.Tests`（`MonsterExperienceRulesTests` 同样如此）。既有惯例，非本次引入。

上一轮的 O-3（mirror 为空时不留日志）本轮已修复，不再列出。

## 与 Success Conditions 的对照

| SC | 判定 | 依据 |
| --- | --- | --- |
| SC-5 结算经同一发放件支付、跨线后果与击杀一致 | 满足 | `GrantClearExperienceCommandHandler` 只依赖 `ICharacterExperienceGrant`；`ALevelCrossedAtSettlementRefreshesTheOfferList` |
| SC-6 结算帧报出的数字与实际支付一致 | 满足 | 申报以 `Award` 为判据；`AnAwardThatDidNotHappenDeclaresNothing` 与 `WithoutASkillMirrorTheClearIsNeitherPaidNorReported` 各经一次变异验证 |
| SC-7 档案的数从档案读 | 满足 | 三张表全部读档案，两个读档案的测试真实执行；额外 `NotEqual` 上游兜底常量 |
| SC-8 全套测试通过、构建零警告零错误 | 满足 | 本轮复跑：build 0/0，1577 通过 / 2 跳过 / 0 失败 |

EV-004（实机通关结算屏截图）按 Spec 与 Docs 的说明本就留待验收，不构成本次审阅的阻塞项；
但前两个字段是否被写反、以及结算屏合计与经验帧总量是否自洽，只有那一次实机能回答。
