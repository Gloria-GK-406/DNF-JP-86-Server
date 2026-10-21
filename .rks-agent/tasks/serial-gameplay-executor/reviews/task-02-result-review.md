---
target: tasks/TASK-02.md
verdict: approved
---

# TASK-02 结果快审（Independent Quick Review）

```yaml
kind: rks-review
verdict: changes_required
```

## 审查对象与口径

- 目标：`tasks/TASK-02.md` Task Unit Contract Revision 1，Result 第 1 轮（未提交）。
- 自有改动：`spec.md` 附录 A 末尾与附录 D（`git diff`）；新增 `verification/event-chain.md`、`verification/cross-connection-order.md`、`verification/xo-dispositions.md`。
- 代码基准：工作区 HEAD `f01dc226`。它与证据文件写的 `b76504bb` 之间只差测试工程（`Server/tests` 下的表征测试），`Server/src` 相同，所以证据里的行号仍然有效。
- 依据：spec.md 的 RQ-033、RQ-050～RQ-053、AC-012、AC-016、Definitions「交付」；`research/01-entry-points.md` §4；`tasks/TASK-01.md` Result（XO 到达先后不确定）。
- 采用的判据：reviewing-work 的 Task Unit 快审与 Deliverable 判据，范围只到本单元。
- 只读了产品代码。没有构建，没有运行测试，没有改产品文件。

## 阻断项

### B-1 拆除尾巴少算了 2 跳：L 至少为 10，不是 8；附录 A 的「最多再加 1 跳」不成立

**缺陷。** 附录 A 和 `event-chain.md` 把「关连接 → 生命周期」这条尾巴记为 3 跳（生命周期 → DungeonParticipantLeft → quest.mirror-leave → quest.mirror-notification），又说拆除路径上唯一没有穷尽的是宠物写物品，「最多再加 1 跳」。这两句都不对。拆除路径会通过两塔的断线离开触发结算，结算会加经验、发物品，由此可以走出一条 5 跳的尾巴。

**路径**（基准代码，每一处都是我自己追到的）：

| 尾巴跳 | CMD | 证据 |
|---|---|---|
| 1 | 生命周期 CMD：拆除调用 `bossTower.LeaveAsync(..., true, ...)`（幻影塔同样：`illusionTower.LeaveAsync(..., disconnected: true, ...)`） | `Application/Sessions/SelectCharacterLifecycleServices.cs:163`、`:149-151` |
|  | → `RewardingMutation`。剩下的成员如果全部阵亡，`Leave` 判为 `Defeated`，随即 `rewards.SettleAsync` | `World/BossTower/BossTowerRun.cs:138-148`（`:146`）；`BossTowerDomainService.cs:191`、`:287`、`:349-351`；幻影塔 `IllusionTowerDgn.cs:129-137`、`IllusionTowerDomainService.cs:190`、`:276-278` |
|  | → 结算里调用 `AddExpAsync`；升级时 `EnqueueAsync(CharacterLevelChanged)`；同时 `delivery.DeliverAsync` 往 Main 里 Put，产生隐式边 InventoryContentsChanged，还可能发邮件，产生 MailCreated | `BossTowerRewardDomainService.cs:120`、`:127`；`IllusionTowerRewardDomainService.cs:119`、`:126`；`Characters/PlayingCharacter.Experience.cs:80-82`；`Inventory/ItemDeliveryDomainService.cs:148`、`:168` |
| 2 | achievement.level（R16）→ AchievementCompleted | `AchievementRegistration.cs:39`；`AchievementDomainService.cs:211` → `:123` |
| 3 | title.unlock（R17）→ 称号簿 Put → InventoryContentsChanged | `TitleBookDomainService.cs:123` |
| 4 | achievement.inventory（R13，不考虑过滤，与主链同一口径）→ AchievementProgressChanged | `AchievementDomainService.cs:171` |
| 5 | achievement.noti（L7） | `ProtocolModule.cs:661` |

这条路径在事件类型图上是简单路径：CharacterLevelChanged、AchievementCompleted、InventoryContentsChanged、AchievementProgressChanged 各只经过一次。拆除时 Leave 走的是 `Execute`，`scope.Current` 为空就自开事务（`BossTowerDomainService.cs:56-60`），所以 `EnqueueAsync` 在这条路上可以执行。

**按本单元自己的口径重算。** 主链 5 跳，尾巴接在最深那一跳之后：

- 尾巴：1 ＋ 1 ＋ 1 ＋ 1 ＋ 1 ＝ 5，共 5 跳；
- L ＝ 5 ＋ 5 ＝ **10**；
- 下限 max(2 × 10, 8) ＝ **20**。

其实用不着逐条穷举也能得到这个上界。生命周期 CMD 能发布的事件种类与入站 CMD 一样多，所以它作为源头能起的最长链，就等于任意源头的最长简单路径，也就是 5。只要采用「尾巴接在最深那一跳之后」的口径，L ≤ 5 ＋ 5 ＝ 10 就是由构造保证的上界。

**后果。**

- 常量取 20，碰巧仍然满足 2L。但附录 A 写的 L ＝ 8 和「判据钉住常量 ≥ 2 × 8」都是错的；`TASK-02.md` Remaining 也写着「T-002：判据钉住『常量 ≥ 2 × 8』」。
- RQ-052 和 AC-016 要求判据钉住「常量 ≥ 2 × 最长链」。按现在的写法，判据允许把常量降到 16，而 16 会把这条合法的拆除结算链截断。截断后按 RQ-053(a) 关闭根连接，造成线上误断线。这类错误没有任何现有测试能发现，正是本单元 Result review 要防的那一种。
- 附录 A 声称这里的不确定只有宠物一处，会误导 T-002，让它以为 20 里还有余量。实际上余量是 0。

**最少的改法。**

1. 附录 A 和 `event-chain.md`：把尾巴改成 5 跳（可以直接用上表），L 改为 10，下限改为 20。删掉「最多再加 1 跳 → L ＝ 9」这一句，换成上面那条「由构造保证的上界」理由。宠物写物品那条路径由这个上界吸收，不必再单列。
2. 判据改为钉住「常量 ≥ 2 × 10」。常量取 20 是否保留，由主控方或 T-002 决定。注意 20 已经没有余量，如果还想给未穷尽的部分（例如 LiveOnly 的 `ComposeAsync`）留出空间，应当取得更大。
3. 同步修改 `TASK-02.md` 的 Why、Checks 第 2 项（尾巴的加法）和 Remaining 中 T-002 那一条。

## 非阻断项

### N-1 「关连接 → 生命周期」的口径是澄清，但已经超出「只补表格」的边界，需要主控方认可

- **它与需求不冲突。** RQ-012 区分「由 CMD 发起的关连接请求」；RQ-036 规定交付失败、超时、队列超限时，由发送端关闭连接。也就是说，Spec 本身就把「CMD 引起的关闭」和「传输层的关闭」分开了。RQ-050 的「由 CMD 引起的 CMD……包括关连接引起的生命周期 CMD」，按这个读法本来就只涵盖前一种。客户端断开引起的生命周期 CMD 显然不是由 CMD 引起的；RQ-050 的新根清单（定时、入站、GM、HTTP、启动恢复）漏了它。这是一个空缺，附录 A 把它补上了。
- 与 RQ-053(a) 和 AC-016 也不冲突。生命周期根的来源连接就是已经关闭的那条连接，超限时再关闭它不会产生副作用，「根连接被关闭」照样可以测。
- **但它在实际上新增了一种根来源（「生命周期」），收窄了 RQ-050 的继承范围。** 合同的 Owned boundary 写的是附录 A「只补最长合法链表」「不算修改需求」，而这段口径改变了 T-002 实现链继承的方式。建议主控方在 RQ-050 正文里写明（或在 Spec 变更记录中认可），不要只放在附录 A；在此之前，T-002 不要以附录 A 为唯一依据。
- 另外，「无限拉长」这个理由说过头了。按字面继承，级联的长度也受连接数限制，不是无限的。真正的问题是：它会超过深度上限，被 RQ-053(a) 当成超限处理，误关根连接。理由按这个改写更准确。

### N-2 `xo-dispositions.md` 的处置没有收口，与附录 D 的结论矛盾

- 第 1 节 #53 写 XO-7「候选……倾向不是，待所有者确认」，#60 写 XO-6「待裁定」；第 2 节 `MarriageWeddingSlice.cs:220` 的备注也写「倾向不是」；第 4 节写「本轮逐行处置没有发现新的跨连接顺序要求」「是否为跨连接要求仍待所有者裁定」。
- 而附录 D 和 `cross-connection-order.md` 已经把 XO-6、XO-7 定为保守列入；XO-3～XO-7 相对于原来的附录 D 都是新项。
- 合同 Required check 3 要求每个命中的处置都是「是」或「不是」。请把这三处改成最终处置（「是，保守列入」），并改正第 4 节的结论句。

### N-3 Detailed Plan 中的检索词没有全部执行，偏离也没有记录

- 计划列了 `Peers`、`must precede`、`ordering`，实际执行的是 `first`、`then`、`after the`、`before`。`before` 覆盖了 `before the leader`，但前三个词没有执行，结果里也没有说明。
- 我补查了一遍注释中的 `peer(s)`、`preced`、`ordering`、`in order`、`until every`、`only after`、`all members/recipients`，排除了已命中四个旧词的行。新命中只有一处可能相关：`MarriageProposalSlice.cs:260`（「retain the existing lifecycle fence through ACK + peer NOTI」）。它说的是门租约要持有到两次发送都结束，不是跨连接先后。所以结论不受影响，但应在 `cross-connection-order.md` 里把这次口径偏离和补查结果记下来。
- 检索范围只覆盖 Protocol 和 Application，没有覆盖 Networking、HttpApi、GmWeb，也应当写明。`research/01` §5 只指出 `GmCeraEndpoints.cs:52` 有一处单账号直发，影响不大。

### N-4 残留风险：「请求者先、其他人后」的写法按统一判据排除了，T-006 应当知道这一点

- `ProjectAsync` 中有 5 处「先发操作者本人，再发其他人」（#24、#41、#51、#66、#73、#90 中属于这一类的那几处），加上 #36 GroundPickup 的「请求者 Required、其他人 BestEffort」，以及 `DeliverExplicitAsync` 本身的 Required-先 顺序。这些都按「没有注释或测试把它当作要求」排除了。
- 这条判据与 XO-6（有测试）、XO-7（有注释）的列入理由一致，自洽，不构成缺陷。但它们在今天的基线上也是确定的交付顺序；发件箱改造后，跨连接的这种先后会变得不确定。建议在附录 D 下面加一句，明确「这些写法已经判定为不需要跨连接顺序，改造后允许乱序」。这样 T-006 的取舍是显式的，而不是被静默丢掉。

### N-5 Reliable CMD 会不会登记关连接（重点 b）：本轮没有发现问题

- 17 个 Reliable 处理器都不注入关闭器，也不发送消息。
- 产品代码里 12 个关连接调用点（`research/01` §2）都在入站 handler、两塔的投影失败隔离（属于传输侧失败，按附录 A 口径是新根）或选角流程里。
- 即使将来事件 CMD 因 RQ-031(c) 关闭来源连接，附录 A 把尾巴接在最深那一跳之后的上界也已经覆盖了这种情况。

### N-6 定时、GM、HTTP 起点（重点 c）：不会比入站更长

- 定时源（两塔回城与载入超时、DungeonPartyWipe、祭坛调度、过期清理、票券补充、Joust 等）、GM 写操作（发邮件、发 Cera、活动配置、初始化与补给）和启动恢复，都是新根，从第 1 跳算起。
- 它们能发布的事件都在「源头 → 首个事件」表的覆盖范围内。最长的是能产生 CharacterLevelChanged、MonsterDefeated 或 DungeonCompleted 的源头，也是 5 跳，与入站相同。启动器 HTTP 没有发布点。主链 5 跳的结论成立。

## 核对过、没有问题的部分

- **订阅计数**：重新 grep 得 52 行。按文件 17 ＋ 21 ＋ 2 ＋ 4 ＋ 8 ＝ 52；其中 `AddRuntimeEventSubscriber` 13 处全部在 `ApplicationModule`；Reliable 17、LiveOnly 22。与 Result 和 `research/01` 一致。（`research/01` §4 正文里「12 个是 UsageQuota」与表中 D1～D11 共 11 个不一致，那是 research 自身的笔误，不影响本单元。）
- **主链各跳**：抽查了 R1、R3～R10、R13、R17 的处理器，以及 L2、L18 两个 LiveOnly 处理器。发布点与表一致。Reliable 中只有 R17 会写 SQLite 物品；没有 Reliable 加经验、发邮件或完成任务。隐式边的来源只有 `SqliteItemDomainStoreSession`；宠物存储也复用这个 session（`SqliteCreatureDomainStore.cs:39`），易失存储不发事件。环的判定与容器过滤的描述都准确。
- **附录 D 的 7 项**：都有位置和处置理由。逐一回读了 XO-1 `:353-376`、XO-2 `:283-309`、XO-3 `BossTowerProjectionDelivery.cs:175`、XO-4 `IllusionTowerProjectionDelivery.cs:171`、XO-5 `:222-276`、XO-6 `PartyNotificationSlice.cs:798-842`、XO-7 `MarriageWeddingSlice.cs:220-222`，位置与描述相符。XO-5 与 DR-10 互相引用，没有重复登记。
- **S/T/W/V 分类抽查**：回读上下文 39 行，包括 `DungeonClearProjectionDelivery:47`、`DungeonLoadingProjectionDelivery:84/107`、`BossTowerProjectionDelivery:135/150/619/677`、`IllusionTowerProjectionDelivery:145`、`PartyNotificationSlice:518/664/829`、`TownAreaProjectionDelivery:344/484`、`GroundPickupSlice:509/634`、`DungeonEntryProjectionDelivery:332`、`LeaveDungeonSlice:129/133`、`SelectCharacterHandler:296/319/323`、`OnlineUserConnectionObserver:95`、`PartyCommandSlice:321`、`BossTowerPartyCoordination:64/129/198`、`DungeonClearSlice:775/779/868/932/1055`、`StateSynchronizationMessages:150/528`、`FinishLoadingSlice:97`、`CharacterDeathSlice:107`、`DungeonPartyWipeService:33/82`、`OpeningDelivery:27`、`HellPartyProjection:7`。没有发现把跨连接多收件人顺序误判成 S/T/W 的情况。
- **加法**：用脚本复算了 `xo-dispositions.md` 第 2 节：T 183 ＋ H 46 ＋ R 54 ＋ I 27 ＋ W 125 ＋ S 53 ＋ N 2 ＋ X 4 ＋ V 4 ＝ 498，没有重复的行号；关键词 f 145 ＋ t 105 ＋ a 72 ＋ b 204 ＝ 526。第 1 节 95 行 ＝ 是 1 ＋ 待裁定 1 ＋ 候选 1 ＋ 不是 92；`ProjectAsync(` 的 grep 复现为 95。与 Result 的 Checks 一致。
- **「交付」口径**：附录 D 的口径句与 Definitions 一致（交给传输层，即 `SendAsync` 返回），也与 RQ-033 的「已交付或已判定失败」一致。以 TASK-01 的到达测量（`verification/logs/xo-arrival-measurement.txt`；TASK-01 Result Checks 第 4 行和 Remaining 第 1 条）为依据，把 AC-012 放在发送端断言，理由成立。

## Round 2

```yaml
kind: rks-review
verdict: approved
```

### 审查对象与口径

- 目标：TASK-02 Result 第 2 轮（未提交），契约仍为 Revision 1。
- 主控方对第 1 轮的处置：B-1 接受，常量取 32；N-1 写进 RQ-050 正文；N-2、N-3、N-4 照改。
- 审查范围：`spec.md` 的 RQ-050、附录 A、附录 D（`git diff`）；`verification/event-chain.md`「最长合法链」「不确定」两节；`verification/cross-connection-order.md`；`verification/xo-dispositions.md` 的 #53、#60 和第 3、4 节；`tasks/TASK-02.md` Result。
- 只读，没有构建，没有运行测试。产品代码与第 1 轮相同，第 1 轮的代码证据仍然有效。

### B-1：已解决

- **四处一致。** 附录 A、`event-chain.md`、TASK-02 Result（Why、Checks 2、Remaining）、RQ-052 的判据表述，现在都是同一组值：尾巴 5 跳，L ＝ 5 ＋ 5 ＝ 10，下限 max(2 × 10, 8) ＝ 20，常量 32，判据钉住「常量 ≥ 2 × 10」。
- **没有残留旧值。** 在 spec.md、TASK-02.md 和 verification 目录下逐一检索 `2 × 8`、`＝ 8`、`L ＝ 9`、`下限 18`、`常量取 20`、`≥ 16`、`＝ 16`，都没有命中。
- **尾巴表是对的。** 第 6～10 跳与我第 1 轮追到的路径一致，证据行号正确（`SelectCharacterLifecycleServices.cs:149-163`、`BossTowerRun.cs:146`、`IllusionTowerDgn.cs:137`、`BossTowerDomainService.cs:56-60`、`:349-351`、`BossTowerRewardDomainService.cs:127`、`PlayingCharacter.Experience.cs:82`）。「生命周期 CMD 能发出的事件种类和入站 CMD 一样」这条上界理由写进去了。宠物写物品那一项改为由这条上界吸收，也正确。
- **常量 32 符合 RQ-052。** RQ-052 只要求常量 ≥ 2L 且 ≥ 8，32 满足；余量 12，理由也写清楚了。

### N-1：RQ-050 的改写仍是澄清，没有改变已批准需求的含义

1. RQ-050 原句的继承范围是「由 CMD 引起的 CMD」。新增的那句把「关连接引起的生命周期 CMD」限定为 CMD 登记进发件箱的关连接请求，与 RQ-012（「由 CMD 发起的关连接请求」）、RQ-036（传输失败由发送端关闭）原有的区分一致。
2. 客户端断开、传输层失败引起的生命周期 CMD，原文的新根清单漏了。现在补成新根，来源种类为「生命周期」，这个种类原本就在 Definitions 的来源种类表里（`spec.md:30`）。所以 Result 说「没有新增来源种类」是对的；我第 1 轮说它「新增了一种根来源」，这句我收回。
3. 与 RQ-053(a)、AC-016 不冲突，理由已经改为「越过深度上限误关根连接」，这句准确。

### N 项落实情况

| 项 | 状态 | 依据 |
|---|---|---|
| N-2 | **大体落实，留两处尾巴（见 R2-N1）** | `xo-dispositions.md` 第 1 节 #53、#60 已不再写「倾向／待裁定／候选」；第 3 节改为「是 3 ＋ 不是 92」，与 Result Checks 3 一致；第 4 节首条改为「XO-1～XO-7 之外没有新的要求」 |
| N-3 | 落实 | `cross-connection-order.md` 补跑了 `Peers` 29 行、`must precede` 4 行、`ordering` 13 行，逐项处置，并写明 Networking、HttpApi、GmWeb 不在范围内的理由。我第 1 轮补查找到的 `MarriageProposalSlice.cs:260` 不含这三个词，不在本次补跑的命中里，但我已判定它不是跨连接先后，结论不受影响 |
| N-4 | 落实，措辞可以再补一类（见 R2-N2） | 附录 D 新增「改造后允许乱序的写法」，点名了两类，并说明单连接顺序仍按 RQ-032 保持 |

### Round 2 非阻断项

**R2-N1 两处旧说法没对齐（N-2 的尾巴）**
- `xo-dispositions.md:428`（第 4 节末条）还写着 XO-6「是否为跨连接要求仍待所有者裁定」，与第 3 节和附录 D 的「保守列入」矛盾。
- `cross-connection-order.md` 的「加法」一节还写着「是 1 ＋ 待裁定 1 ＋ 候选 1 ＋ 不是 92」，与 `xo-dispositions.md` 第 3 节和 Result 的「是 3 ＋ 不是 92」不一致。
- 两处改一句话即可。附录 D 这个下游消费的文件本身是对的，所以不阻断。

**R2-N2 附录 D「允许乱序」少了一类写法**
- `cross-connection-order.md` 的 `Peers` 处置里，把 `TownAreaProjectionDelivery.cs:204-210`、`:575-588` 的「先向同伴发到达、再向本人发同伴」写成「按附录 D『允许乱序的写法』处理」。
- 但附录 D 列出的两类是「先本人、后他人」和「Required 先发」，不包括这种「先同伴、后本人」。我回读了 `:196-214`：这是城镇到达投影的普通扇出，没有注释或测试要求先后，判为允许乱序是成立的。
- 建议在附录 D 那一句里加上这第三类，免得 T-006 照着附录 D 找不到它。

**R2-N3（提醒 T-002，不属于本单元缺陷）执行器自己发起的关闭是否继承链，RQ-050 没有写**
- 改写后的 RQ-050 只区分了「CMD 登记进发件箱的关闭」和「传输层自发的关闭」，没有说 RQ-053(a) 超限时关闭根连接这一下该怎么算。
- 如果把这次关闭当作继承链，它引起的生命周期 CMD 深度就会超过上限，本身被拒绝入队，拆除永远不会执行。这是一个状态泄漏风险。
- 这个缺口在改写前就有（原文更宽，要求全部继承）。改写没有让它变坏，但也没有消除它。建议主控方在 T-002 开始前写明：超限关闭引起的生命周期 CMD 是新根，或者豁免深度检查，与 RQ-053(b) 的系统 CMD 处理方式相同。

### Round 2 结论

第 1 轮的阻断项 B-1 已经正确、完整地修好，没有残留旧值；RQ-050 的改写属于澄清；没有新的阻断项。R2-N1、R2-N2 是文字对齐，可以在 Task 提交前顺手改；R2-N3 交给主控方在 T-002 前处置。
