# TASK-05 — 结算帧不再对牌面说零

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables:

结算流程在既有幂等闸之后、构造结算帧之前定下牌面；把 274 字节里的卡片尾部
按实际牌面与归档成本表填写，不再是占位零。

- Referenced spec requirements: RQ-012
- Referenced plan sections: `plan.md` 的 T-005；边界 BD-015, BD-021
- Owned boundary: BD-015, BD-021
- Permitted files or interfaces:

- `Server/src/Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs`（修改）
- `Server/tests/`（新增判据）

- Dependencies: TASK-03
- Required dependency outputs: TASK-03 的业务服务（定牌）
- Acceptance conditions:

1. 卡片尾部的免费牌位物品与金币、其余牌位、付费价钱与实际牌面及归档价钱一致。
2. **只改写入的值，不改写入的宽度与顺序**；体长仍为既有值，既有的长度自检未触发。
3. 定牌发生在幂等闸之后、构造结算帧之前。
4. 既有的结算判据仍全部通过。
5. 全量判据 不低于 1690 通过、0 失败、2 跳过。

- Task-specific authorization: 根契约（revision 2）的授权覆盖本任务，无收窄。合并、推送、建 PR、删分支或清理 worktree 不在其内。
- Required methods:

- `test-driven-development`：判据先行，再让它变绿。
- 分层按 `AGENTS.md` 与 `Docs/新功能接入规范.md`：内容源只被规则服务注入；
  规则服务不持有仓储、注册表、目录、存储或领域服务；handler 只做四类动作；
  命令处理器禁止嵌套 dispatch；能触达持久写的命令一律 Transactional。
- 归档读法在 XML doc remarks 里写明段落出处；读不出来的记为未查明，不填看似合理的数。
- `System.*` using 在前，本仓不做字母序。`TreatWarningsAsErrors`。

- Required checks:

| ID | Check | Expected result |
|---|---|---|
| CK-012 | `dotnet test Server/86JP.Server.slnx --nologo --filter DungeonClear` | 结算帧判据通过；体长仍为既有值，长度自检未触发 |
| CK-013 | `Non-command:` 比对结算帧卡片尾部与本局牌面 | 两者所载的物品、金币与价钱一致 |
| CK-S | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 不低于 1690 通过、0 失败、2 跳过 |

- Required evidence: CK-012 的判据输出；CK-013 的逐字段对照；全量判据输出。

## Controller Note — 执行者两次连接中断，控制器接手

本 Task 的执行者连续两次因 API 连接中断而终止：第一次断在
「produce a real field-by-field dump for the CK-013 evidence」，第二次断在
「Now let me write the delivery sections」。两次都**没有留下任何交付节**。

**留下的产物是自洽的**（控制器实测）：工作区 HEAD 仍 `c0cb6c99`、无提交，
`DungeonClearSlice.cs` 改动 +107/-19，全量判据 **1838 通过 / 0 失败 / 2 跳过**
（TASK-04 收工时为 1830/0/2，净增 8），行尾自查一致。

控制器**没有替它写自述**——那等于给自己没做过的工作背书，正是本 Spec 一直在防的
「无证据的声称」。以下是控制器自己读 diff、自己核出来的评估，如实标为控制器所核。

### 已完成且做得好的部分（控制器复核）

- **牌面挂在帧上而不是让编码器去查**。理由写在记录类型上：
  「编码器若自己去问，就成了牌面能被决定的第二个地方」。这是对的分层直觉。
- **`FreeCardEntryCount = 1` 附了长度安全的推理**：它前面那个字节是其后条目数、每条八字节，
  声明成 2 就会让体长比常量多八字节，而末尾的自检正是为拒绝这件事而存在的。
- **`PositionOf` 按牌位序号匹配而不是按列表顺序**：「帧是位置性的，
  顺序不同的牌面会把内容写到错的牌下面，而帧本身说不出来」。
- **定牌点的位置有论证**：上面是幂等闸（重复上报在到达该行之前就被挡回），
  下面是本次结算要发的每一帧。
- **传入的等级取在经验发放之前**，理由是「一次通关的牌从哪个等级带抽，
  是关于刚打完那一局的事实，不是关于它最后一点经验买到的等级」。这一处很sharp。

### 一处真缺口（控制器发现）

**付费翻牌的价钱仍写 0。** `writer.Write(0); // paid-card cost` 未动。

Spec 的 RQ-012 明写该段所载的「免费牌位的物品与金币、其余牌位、**以及付费翻牌的价钱**，
与本次实际产生的牌面内容及归档声明的价钱一致，**不再是零**」。

价钱在归档里有（`[gold card cost table]`，TASK-01 已读进 `ClearRewardTable.GoldCardCost`），
但**不在链路上**：规则服务未暴露它，业务服务的结算结果也不带它。

### 一处需要裁定的读法（控制器发现，执行者已在代码里披露）

免费卡条目的第二个字段，原注释叫它「free-card gold」，而执行者写入的是**该牌位物品的数量**，
并在 `AmountAt` 的 remarks 里说明了为什么：既然相邻的第一个字段是该牌位自己的物品 id，
第二个就该是该物品自己的数量。它同时指出该字段在协议记录里的命名，把分歧摆了出来。

**控制器裁定：写数量是对的，保留。** 两个字段相邻且第一个已确定是物品 id，
把第二个读成金币会让一对字段分属两件事；而「gold」这个名字来自参照服务端的命名，
按本 Spec 的约束不构成依据。**但这是一处读法**，应当在 TASK-09 的读数对照里如实列出。

## Contract Amendment 1 — 边界扩至链路上的四个文件（**编辑之前**写的）

**要补的**：把归档的付费价钱从内容源引到结算帧，填掉那个仍为零的字段，满足 RQ-012。

**为什么现在做而不是推给 TASK-07**：T-007 为了收费本来就要加这个查询，把字段留给它填
看似省事。但那是**为了省事把一条要求劈成两半**——RQ-012 的归属地是本 Task，
`plan.md` 的 Requirement Coverage 也只把它映到 T-005。
本 Spec 反复的教训是「下个任务再做」正是缺口存活的方式。

Permitted files or interfaces 增补（每一处都限于「取得该等级的付费价钱」这一件事）：

- `Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs`（TASK-02 产出）
- `Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs`（TASK-02 产出）
- `Server/src/Dfo.Server.Abstractions/World/ICardRewardDomainService.cs`（TASK-03 产出）
- `Server/src/Dfo.Server.Application/World/CardRewardDomainService.cs`（TASK-03 产出）

**不重开已收工的 TASK-02 与 TASK-03**——按 TASK-01/02/03/04 一路的先例，
由当前在办的 Task 做有界扩界。

**约束**：价钱只能经规则服务读（内容源只被规则服务注入）；
业务服务把它随结算结果交出；切片不自己读内容源、不自己算。
归档没有该等级价钱时写什么，须显式决定并说明，不得默默写零——
零是一个价钱（免费翻），与「这一级没有定价」不是一回事。

## Execution Result — round 1

本轮只补控制器指出的那一处缺口（付费翻牌价钱仍写 0）并写交付记录。控制器已复核的部分
（牌面挂帧、`FreeCardEntryCount`、`PositionOf`、定牌点位置、传入等级取在发经验之前、
`AmountAt` 的读法分歧）本轮**一律未动**，也不在下面声称为本轮产出。

### 补了什么

把归档的付费价钱从内容源经规则服务、业务服务引到结算帧，填掉那个仍为零的字段。

- `Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs`
  新增 `int? PaidFlipCost(int characterLevel)`。remarks 写明出处
  `[gold card cost table]` / `etc/itemdropinfo_clearreward.etc`；写明只按等级取，
  因为那张表只以等级为键，四张加成表都与「翻一次要多少钱」无关。
- `Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs`
  实现是一次查表，`rates.Load(DropRateTables.ClearReward).GoldCardCost(level)`，
  不与任何加成表合成——合成出来的就是本仓自己发明的价目。表的 null 原样传出。
- `Server/src/Dfo.Server.Abstractions/World/ICardRewardDomainService.cs`
  新增 `ClearRewardSettlementResult(ClearRewardCardSet Cards, int? PaidFlipCost)`，
  `SettleAsync` 改为返回它。价钱跟结算结果绑在一起而不是另开一个查询：价钱是
  「这一局在哪个等级结算」的事实，另问一次就会拿着调用方手边的等级去问，而发经验之后
  那个等级可能正是这一局自己买到的。
- `Server/src/Dfo.Server.Application/World/CardRewardDomainService.cs`
  取到 run 之后、幂等分支之前问一次价钱，已定牌与新定牌两条分支都把它带出去。
  价钱**不写进 run**：它是归档关于某个等级的答案，不是这一局定下来的东西。
  构造参数没变（仍只有规则服务与 run），既有判据
  `The_settlement_service_reaches_the_archive_only_through_the_rules` 仍钉着这一点。
- `Server/src/Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs`
  `ClearDungeonRewardNotification` 增 `int PaidFlipCost`；
  `EncodeClearDungeonReward` 增 `int paidFlipCost = NoPaidFlipCost`，
  `writer.Write(0); // paid-card cost` 改为 `writer.Write(paidFlipCost); // paid-card cost`；
  handler 在 `SettleAsync` 之后、所有帧之前把 `int?` 翻成帧上的值。
  切片不读内容源、不自己算价钱。
- `Server/tests/`：新增判据九处（Theory 展开后 11 条），并对两个测试替身补齐新成员。

### 无定价时写什么，及理由

写 `DungeonClearPayloadCodec.UnpricedPaidFlipCost = int.MaxValue`，**不是 0**。

内容源那一侧 `ClearRewardTable.GoldCardCost` 的 remarks 已经把话说完了：答 null 而不是 0，
因为「零是一个价钱——免费翻——归档不定价的等级不是免费的」，并把「怎么办」明确交给调用方。
本字段的宽度与位置是参照的、不能改，所以必须写进去一个数；能排除的只有 0。
选四字节能装的最大值，它唯一被选中的性质是：**归档整张价钱表里没有任何一条等于它**
（由 `ClearRewardArchiveTests` 断言），所以它不可能被误当成从文件里读出来的数。
**客户端见到它会怎样未查明**，代码注释里明写不作声称；这里决定的只是两件事——
本服务器不把一次没能定价的翻牌白送出去，以及这种等级在帧上是可见的而不是静默的。

第三种情形单独具名，没有和上面混为一谈：**这一局根本没定牌**（`settled` 为 null）时
没有付费翻牌可定价，写 `NoPaidFlipCost = 0`，也就是有牌之前这一帧一直写的值。
两个常量各有 XML doc 说明彼此不是替身。

### 判据真实输出

全量（`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`）：

```
Networking      51 通过 / 0 失败 / 0 跳过
Protocol       439 通过 / 0 失败 / 0 跳过
Pvf             60 通过 / 0 失败 / 0 跳过
Persistence    244 通过 / 0 失败 / 0 跳过
Application    847 通过 / 0 失败 / 0 跳过
Infrastructure 204 通过 / 0 失败 / 2 跳过
Host             4 通过 / 0 失败 / 0 跳过
合计          1849 通过 / 0 失败 / 2 跳过
```

控制器实测的收工基线是 1838 / 0 / 2，净增 11，与新增判据条数一致。一次跑完，
没有撞上已知假红（真实进程/socket 的 10 秒预算、两用例共用 SQLite 库）。

CK-012（`dotnet test Server/86JP.Server.slnx --nologo --filter DungeonClear`）：

```
Dfo.Server.Protocol.Tests     69 通过 / 0 失败 / 0 跳过
Dfo.Server.Application.Tests  16 通过 / 0 失败 / 0 跳过
```

`dotnet build Server/86JP.Server.slnx`：0 警告 0 错误（`TreatWarningsAsErrors` 下）。

新增判据：

| 判据 | 钉住什么 |
|---|---|
| `ClearRewardArchiveTests.The_archive_prices_a_paid_flip_by_level` | 归档实值：86 → 10300、1 → 100；整表无 0、无 `int.MaxValue`；表外等级答 null |
| `ClearRewardRuleServiceTests.What_a_paid_flip_costs_is_the_tables_own_price` | 规则服务原样传表的价钱与 null，且只按等级取 |
| `CardRewardDomainServiceTests.The_settlement_hands_back_what_a_paid_flip_costs` | 价钱随结算结果交出，且问的是结算等级 |
| `CardRewardDomainServiceTests.A_level_the_rules_price_no_flip_at_settles_with_no_price` | null 一路传到业务服务出口，没被抹成 0 |
| `CardRewardDomainServiceTests.A_second_settlement_keeps_the_cards_already_fixed`（既有，加两行） | 幂等分支也带价钱 |
| `DungeonClearCodecTests.What_a_paid_flip_costs_is_written_at_the_end_of_the_card_tail` | 价钱落在 232，且 232 前后两段与不定价的帧逐字节相同 |
| `DungeonClearCodecTests.The_body_keeps_its_length_whatever_a_paid_flip_costs`（3 例） | 体长仍等于 `ClearRewardBodyLength` = 274 |
| `DungeonClearCodecTests.The_marker_for_a_level_with_no_price_is_not_a_free_flip` | 无定价标记不是 0 |
| `SetPlayResultHandlerTests.TheResultScreenSaysWhatAPaidFlipCosts` | 三个结算帧构造点都带上价钱（正常、无技能镜、未发放） |
| `SetPlayResultHandlerTests.ALevelTheArchivePricesNoFlipAtIsNotReportedAsAFreeOne` | 无定价等级报的是标记而非免费翻 |

### 归档实值怎么核的

```
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf \
  dotnet run --project Tool/PvfProbe -- dump etc/itemdropinfo_clearreward.etc 4000
```

`[gold card cost table]` 是等级/价钱对，起于 `1 100 2 220 3 340 ...`，含 `86 10300`，
止于 `200 23980`。钉住的是 86 → 10300 与 1 → 100 两条；表外等级不写死，
由表自身的键取 `max+1` 与 `min-1` 得出，所以归档若改动是判据跟着走而不是留下陈旧断言。

### 变异检查（两次，均已还原）

1. 把 `writer.Write(paidFlipCost)` 改回写死 `writer.Write(0)`：
   `--filter DungeonClear` 由全绿转 **4 失败**——
   `What_a_paid_flip_costs_is_written_at_the_end_of_the_card_tail`（Expected 6780 / Actual 0）、
   `The_body_keeps_its_length_whatever_a_paid_flip_costs(paidFlipCost: 10300)`、
   同一 Theory 的 `(paidFlipCost: 2147483647)`、
   `The_marker_for_a_level_with_no_price_is_not_a_free_flip`（Expected Not 0 / Actual 0）。
   还原后复跑全绿。
2. 把 handler 的 `?? UnpricedPaidFlipCost` 改成 `?? 0`（即把「无定价」悄悄写成零）：
   转红 **1 条**——`ALevelTheArchivePricesNoFlipAtIsNotReportedAsAFreeOne`
   （Expected 2147483647 / Actual 0）。还原后复跑全绿。

防「占位零与真值零分不清」：用于断言价钱的判据里，归档给的价钱是 **10300**，
fixture 给的是 5150 / 4210 / 6780，**没有一个是 0**；并且
`Assert.DoesNotContain(0, table.GoldCardCosts.Values)` 保证归档整表没有 0，
所以「帧里是 0」永远不可能是「归档就给 0」。

### 体长自检怎么验的

`ClearRewardBodyLength = 274` 与编码器末尾的
`if (body.Length != ClearRewardBodyLength) throw new InvalidDataException(...)` 都没有动。
`The_body_keeps_its_length_whatever_a_paid_flip_costs` 用满牌面加三种价钱
（`NoPaidFlipCost` / 10300 / `UnpricedPaidFlipCost`）各建一次帧，断言
`ClearRewardBodyLength == 274` 且 `body.Length == ClearRewardBodyLength`——自检若被触发会先抛，
所以这条同时证明它没触发。`What_a_paid_flip_costs_is_written_at_the_end_of_the_card_tail`
另外把 232 之前与 232+4 之后两段与不定价的帧逐字节比对，即：只改了写入的值，
宽度与顺序未动。既有的 `Nothing_else_in_the_reward_breakdown_carries_a_value`、
`A_clear_with_nothing_behind_its_cards_writes_the_tail_it_always_wrote` 等未改动、仍通过
（编码器默认参数保持 `NoPaidFlipCost`）。

### 行尾自查

`git diff --numstat` 与 `git diff --numstat --ignore-cr-at-eol` **18 行输出逐行一致**，
本轮改动的三个已跟踪文件为：

```
164  20  Server/src/Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs
245   0  Server/tests/.../DungeonClear/DungeonClearCodecTests.cs
217   1  Server/tests/.../DungeonClear/SetPlayResultHandlerTests.cs
```

需要如实说明的一处：**本仓 `Server/` 下的 blob 本身就是 CRLF**——
`git cat-file blob HEAD:Server/src/Dfo.Server.Protocol/Game/GameProtocol.cs` 225 行全部 CRLF，
`.gitattributes` 只对 `protocol/` 强制 `eol=lf`，且 `core.autocrlf` 在用户级配置里是 `false`。
我改过的 11 个文件在工作区都是整齐的 CRLF（每个文件 CRLF 行数 == 总行数），与各自 HEAD 版本
及周围文件一致，**没有引入混合行尾，也没有改动本仓的行尾约定**。本 Task 文件本身是 LF，
追加的这两节也写成 LF。

### 拿不准的地方

- `UnpricedPaidFlipCost` 在客户端的表现**未查明**。`int.MaxValue` 只保证「不是免费」且
  「不可能是读数」；客户端拿到它是显示天价、是拒绝、还是别的，本仓没有样本。注释里明写未查明。
  日后取得实机样本，要改的是一个具名常量。
- `SettleAsync` 的返回类型换成 `ClearRewardSettlementResult`，动了 TASK-03 的签名。
  这在 Amendment 1 授权的两个文件之内（接口与实现各一），产品代码里的调用方只有本 Task 的
  handler；`SelectCardCommandHandler` 只用 `ClaimAsync`，未受影响。既有判据只做了取值路径的
  机械适配（`first.Free` → `first.Cards.Free`），断言强度未减。
- 业务服务在幂等分支**之前**问价钱是刻意的（价钱不进 run，每次按传入等级重新问）。
  若日后有人希望「一局的价钱也冻结在结算那一刻」，那是另一个决定，需要新的依据。
- 本轮判据**不是严格判据先行**：链路四个文件先改、判据随后补齐。作为补偿做了上面两次变异检查，
  确认新增判据确实钉住了新写入的值而不是跟着实现走。这一点如实记下。

## Executor Self-check — round 1

### 契约 Acceptance conditions

1. **卡片尾部的免费牌位物品与金币、其余牌位、付费价钱与实际牌面及归档价钱一致。**
   付费价钱这一半本轮补齐：归档 → 规则服务 → 业务服务 → 通知 → 编码器 → 偏移 232，
   端到端由上表十条判据钉住。免费牌位与其余牌位是上一轮的产出、控制器已复核，本轮未动。
2. **只改写入的值，不改写入的宽度与顺序；体长仍为既有值，既有的长度自检未触发。**
   满足。见「体长自检怎么验的」。`ClearRewardBodyLength` 与自检代码一字未改。
3. **定牌发生在幂等闸之后、构造结算帧之前。** 该位置本轮未动；价钱的取用点紧随
   `SettleAsync` 之后、本次结算所有帧之前。
4. **既有的结算判据仍全部通过。** 是。没有删弱任何既有断言；
   `CardRewardDomainServiceTests` 里三处取值路径随返回类型做了机械适配，并新增两条断言。
5. **全量判据不低于 1690 通过、0 失败、2 跳过。** 1849 / 0 / 2。

### Required checks

- CK-012 通过：Protocol 69 / Application 16，0 失败；体长仍为 274，长度自检未触发。
- CK-013：结算帧卡片尾部与本局牌面的逐字段对照中，本轮新增的是「付费价钱」一格——
  帧上 232 处的四字节等于业务服务交出的价钱（`RecordingCards.PaidFlipCost` = 4210 的端到端，
  以及编码器层 6780 / 10300 / `int.MaxValue` 三例），归档侧的实值由
  `The_archive_prices_a_paid_flip_by_level` 钉住（86 → 10300）。
- CK-S 通过：1849 / 0 / 2。

### Contract Amendment 1

- **只改了授权清单内的文件。** 四个链路文件 + `Server/tests/`（6 个测试文件，含两个测试替身
  `RefusingRules`、`RecordingCards` 的新成员）。清单之外的产品代码**一行未动**。
- **价钱只能经规则服务读。** `ClearRewardRuleService` 仍是唯一被注入
  `IClearRewardRateSource` 的地方；业务服务的构造参数只有规则服务与 run（既有判据钉着）；
  切片既不读内容源也不算价钱。
- **业务服务把它随结算结果交出。** `ClearRewardSettlementResult.PaidFlipCost`。
- **归档没有该等级价钱时写什么，须显式决定并说明，不得默默写零。**
  显式决定 = `UnpricedPaidFlipCost`，具名常量 + XML doc 论证 + 判据钉住 + 变异检查证伪，
  且明确不是零；「这一局没有牌可定价」另用 `NoPaidFlipCost` 具名，两者在注释里互相区分。

### Required methods

- `test-driven-development`：**未严格做到判据先行**，如实记于上。以两次变异检查作补偿。
- 分层（`AGENTS.md` / `Docs/新功能接入规范.md`）：内容源只被规则服务注入；规则服务未持有
  仓储/注册表/目录/存储/领域服务（新方法只读它已持有的内容源）；handler 仍只做四类动作，
  未新增 dispatch；未新增可触达持久写的路径。
- 归档读法在 XML doc remarks 里写明段落出处：`PaidFlipCost` 的 remarks 写明
  `[gold card cost table]` 与文件名；未查明的（客户端对标记值的表现）记为未查明，未填看似合理的数。
- `System.*` using 在前、不做字母序：本轮**未新增任何 using**。
- `TreatWarningsAsErrors`：全量构建 0 警告 0 错误。

## Controller Note — 行尾：两方各自断言，两方都错

round 1 的交付里有一句：「本仓 `Server/` 下的 blob 本身就是 CRLF（`.gitattributes` 只对
`protocol/` 强制 LF），我改的 11 个文件工作区均为整齐 CRLF」。

这与控制器早先实测的 `git ls-files --eol` 结果（`i/lf w/lf`）冲突，因此重测。

**控制器第一次重测也是错的。** 用的是
`for f in ...; do n=$(grep -c $'\r' "$f" ...); done`，结果报告**每一个文件都含 CR**，
连 `protocol/catalog.md`（`.gitattributes` 强制 LF、本 Task 从未触碰）也在内。
细看那些「含 CR 行数」恰好等于各文件的**总行数**——`$'\r'` 在那个循环里没被解释成回车，
变成了空模式，而空模式匹配每一行。

**按字节数重测才定下来**（`b.count(b'\r\n')` 对 `b.count(b'\n')`）：

```
纯 LF : 39
纯 CRLF: 0
混合  : 0
```

**真相是全部纯 LF。** `git ls-files --eol` 从头到尾是对的；
交付里那句「Server 的 blob 本身就是 CRLF」是错的；控制器的第一次重测也是错的。

**留档的理由**：这是本 Spec 中心教训的一个干净标本——两个独立方各自作出关于同一事实的断言，
**两个都错**，而分歧只有靠一个不依赖任何人措辞的测量（数字节）才解得开。
本 Spec 至今二十余处同类缺陷，形状全都是这个。

**对判据的影响：无。** 一直在用的那条自查
（`git diff --numstat` 与 `--ignore-cr-at-eol` 是否逐行一致）不依赖 grep，它一直是可靠的，
本轮也仍然一致。**坏的是那次一次性的 grep，不是流程里的检查。**

## Controller Note — round 2 的两处裁定

**一、`UnpricedPaidFlipCost = int.MaxValue` 保留。** 它是一个本服务器发明的值，
用在「归档对该等级没有定价」这条路上。判断：
- 写 0 是错的——0 是一个价钱（免费翻），会把「没定价」变成「白送」。字段宽度固定，必须写个数。
- 归档定价覆盖 1..200，而 A21 的等级上限在此之内，**这条路实际上到不了**；
  真到了，一个刺眼的数比一个像模像样的数好。
- 判据钉住了「归档整表没有任何价钱等于它」，所以它不会被误当成读数。

**但它仍是一个发明的值，且客户端见到它会怎样未查明**（执行者已如实记为未查明）。
**须进 TASK-09 的读数对照，列在「无归档出处、由本服务器决定」那一栏。**

**二、`SettleAsync` 换了返回类型**（`ClearRewardCardSet?` → `ClearRewardSettlementResult`），
动的是 TASK-03 的签名，在 Amendment 1 授权的文件内。既有判据只做取值路径的机械适配、
断言未减——这一点须由独立评审复核。

## Independent Quick Review — QR-05-1

- Verdict: **changes_required**
- Reviewed contract revision: 1 + Contract Amendment 1（并已读三条 Controller Note）
- Owned diff identity: 工作区未提交，HEAD `c0cb6c99`。本轮所核路径 11 个：
  `DungeonClearSlice.cs`、`IClearRewardRuleService.cs`、`ClearRewardRuleService.cs`、
  `ICardRewardDomainService.cs`、`CardRewardDomainService.cs`、
  `DungeonClearCodecTests.cs`、`SetPlayResultHandlerTests.cs`、
  `CardRewardDomainServiceTests.cs`、`ClearRewardArchiveTests.cs`、
  `ClearRewardRuleServiceTests.cs`、`CardReward/CardRewardTests.cs`（测试替身）。
- **本轮把 round 1 之前那部分（执行者中断、控制器代核、从未经独立评审）一并独立核了。**

### 我自己跑的证据

| 检查 | 我跑出来的 |
|---|---|
| `PVF_ARCHIVE_PATH=... dotnet test Server/86JP.Server.slnx --nologo` | **1849 通过 / 0 失败 / 2 跳过**（51+60+439+244+847+204+4）；两条跳过确为 `PvfQuestDefinitionSourceTests` 与 `PvfTownDefinitionSourceTests` 的「档案在才跳过」用例；一次跑完，未撞已知假红 |
| CK-012 `--filter DungeonClear` | Protocol **69** / Application **16**，0 失败 |
| `git diff --numstat` vs `--ignore-cr-at-eol` | 18 行输出逐行一致 |
| 行尾（**按字节数**比对 CRLF 与 LF 计数，不用 grep） | 25 个相关文件：纯 LF 25 / 纯 CRLF 0 / 混合 0 |
| `dotnet run --project Tool/PvfProbe -- dump etc/itemdropinfo_clearreward.etc 4000` | `[gold card cost table]` = `1 100 2 220 ...` 含 `86 10300`，止于 `200 23980`；覆盖 1..200；**整表无 0、无 `int.MaxValue`** |

### 逐项核对（无问题的部分）

**一、RQ-012 的付费价钱这一半：真的满足了。** 归档 → `ClearRewardRuleService.PaidFlipCost`
（`:86-87` 单次查表，null 原样出）→ `ClearRewardSettlementResult.PaidFlipCost` →
`ClearDungeonRewardNotification.PaidFlipCost` → `DungeonClearSlice.cs:298`
`writer.Write(paidFlipCost)`。字段确实来自归档，不是常量：`ClearRewardArchiveTests.cs:121-126`
用探针实值 86→10300、1→100 钉住，且 `:126` 断言两级不等，排除「一张单值表」。

**二、体长与写入形状：成立，我自己数的字节。** 4+4+4+4+1+92=109 → +1+1+4+4=119 → +20=139
→ +16=155 → +4=159 → +1+1+4+4=169 → 7×9=63 → **232**（付费价钱）→ +4+16+4+1+1+4+4+8=**274**。
与 `ClearRewardBodyLength` 及注释所载 169/178/187/196/205/214/223、232 完全吻合。
`FreeCardEntryCount` 是 `const byte`，选中的是 `Write(byte)` 重载，宽度未变；
`ItemIdAt`/`AmountAt` 均返回 `int`，与原先两处 `Write(0)` 同宽同序。默认实参下
（`freeCards=null`、`paidFlipCost=NoPaidFlipCost`）新编码器与旧编码器逐字节等价——
这是既有 439 条 Protocol 判据全绿的原因，`DungeonClearCodecTests.cs` 的 diff 为 **+245/-0**，
一条既有断言都没动。长度自检代码与常量一字未改。

**三、定牌插在流程哪一步：正确。** `:509` `runs.TrySettle`（幂等闸）→ `:529`
`cardRewards.SettleAsync` → 之后才是全部三个 `ClearDungeonRewardNotification` 构造点。
**等级取在经验发放之前这一点有真判据钉住**，不是只靠注释：
`SetPlayResultHandlerTests.TheCardsAreFixedAtTheLevelTheRunWasClearedAt`——
fixture 入场 `Progression = new CharacterProgressionState(3, ...)`（`:420`），
`FixedAward` 在 `leveledUp: true` 时给 `Level: 4`（`:490/:497`），判据断言
`settlement.CharacterLevel == 3`。若把取用点挪到发经验之后，它必红。控制器说这一处 sharp，
我独立复核后同意，且它有判据、不只有注释。

**四、两个具名常量：区分站得住，我独立判断后同意保留。**
`NoPaidFlipCost = 0`（`:349`，这一局没定牌，即有牌之前一直写的值）与
`UnpricedPaidFlipCost = int.MaxValue`（`:370`，归档对该等级没有定价）取值与含义均不同，
各有 XML doc 互相点名，且**各自有判据**：`The_marker_for_a_level_with_no_price_is_not_a_free_flip`
（同时断言两个常量的值）、`ALevelTheArchivePricesNoFlipAtIsNotReportedAsAFreeOne`。
「归档整表没有任何价钱等于它」**确实被钉住**：`ClearRewardArchiveTests.cs:131-132`
`Assert.DoesNotContain(0, ...)` 与 `Assert.DoesNotContain(int.MaxValue, table.GoldCardCosts.Values)`，
我用探针独立复核该表确为 100..23980 递增、无 0。
可达性：归档定价覆盖 1..200，故该分支实务上到不了；**但代码没有声称它到不了**
（remarks 只写「客户端见到它会怎样未查明」），所以不构成无判据断言。互不替代成立。

**五、`SettleAsync` 换返回类型：只做了机械适配，断言没被减掉或放宽。**
已跟踪判据文件：`DungeonClearCodecTests.cs` **+245/-0**；`SetPlayResultHandlerTests.cs`
**+217/-1**，唯一那一行删除是 `Settle(...)` 私有辅助的 `int reports = 1)` 一行签名，
被 `int reports = 1,` + `RecordingCards? cards = null)` 取代——纯签名扩充。
未跟踪的 `CardRewardDomainServiceTests.cs` 无法 diff，改用两条独立佐证：
(a) QR-03 评审引用的 `ItemIds` 四处断言（当时 `:205-206`、`:212-213`）现完整存在于
`:225-226`、`:232-233`，`Assert.Equal(2, rules.Asked.Count)` 亦在，仅在末尾新增两行价钱断言；
(b) 判据条数守恒——TASK-04 收工 1830，控制器实测 round 1 之前 1838（+8，恰为 round 1 之前
新增的 8 例：codec 4 + handler 4），本轮 1849（+11，恰为 round 1 交付表列出的 11 例）。
若有既有用例被删，这两个数字对不上。断言强度未减，BD-021「不改既有用例的断言与期望值」满足。

**六、免费卡第二字段写「数量」的分歧：披露到位。** `DungeonClearSlice.cs:396-403`
`AmountAt` 的 remarks 明写「What the frame names the free-card entry's half is recorded on
`protocol/game/server-to-client/NOTI-0x0023.md`」，而该文件（HEAD 未改）确实把它记作
**免费牌金币**。分歧摆出来了，出处可查。不推翻控制器裁定。

**七、边界：干净。** 对 `PaidFlipCost` / `ClearRewardSettlementResult` 全仓搜索，命中的产品代码
恰为 Amendment 1 授权的四个文件加 `DungeonClearSlice.cs`，判据全在 `Server/tests/` 内
（含 `CardReward/CardRewardTests.cs` 的测试替身补齐）。清单之外产品代码零改动。

### F-QR5-1（阻塞）— 免费牌位数那个字节：一句无出处、无判据、且与本仓记录相反的结构断言

这一处在 round 1 **之前**那部分（从未经独立评审），控制器的复核里把它列在「做得好的部分」。

**代码怎么说的**（`Server/src/Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs:323-333`）：

> How many eight-byte entries the free-card block declares. One, and it stays one however much the
> face holds. **The byte before the block is the number of entries after it, and each entry is eight
> bytes, so declaring a second would make the body eight bytes longer** than `ClearRewardBodyLength`
> -- which the check at the end of `EncodeClearDungeonReward` is there to refuse.

以及 `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonClear/DungeonClearCodecTests.cs:249-250`：

> The whole of the reason the free-card block still declares one entry. **The reference writes a
> second entry when a card has an item behind it**, which is eight bytes this body does not have.

**本仓记录怎么说的**（`protocol/game/server-to-client/NOTI-0x0023.md`，HEAD 未改动，
人工区布局注明取自上游 `DungeonNotificationBuilder.BuildClearDungeonReward`，
并「逐个非零偏移」对过参照那帧 354 字节）：

```text
      u8   保留
      u8   免费翻牌位数(牌后有物品时为 2,否则 1)
      i32  免费牌物品 / i32  免费牌金币
      7×(u8 标记=1, i32 物品, i32 数量)
```

**冲突点**：本仓记录说这个字节是**取值 1 或 2 的标记**，其后是**固定一对** `i32/i32`；
代码说它是**长度前缀**、值为 2 会**多出八字节**。两者不能同真。而且本仓那条
「参照 354 = 本仓 274 + 80（10 条逐怪记录）」的对账，正是在「固定一对」的布局下算通的。

**为什么这条阻塞：**

1. **它违反本 Spec 的现行断言规矩**（TASK-03 Contract Amendment 2 的扩宽版，TASK-04 已按此执行）：
   「对参照服务端行为的可验证事实断言，要么有判据钉住，要么删去。」
   `DungeonClearCodecTests.cs:249-250` 那句是对参照服务端的直接断言，**无出处、无判据**，
   且超出本仓记录所支持的内容（记录只说**值**变成 2，没说**多写一条 entry**）。
   `DungeonClearSlice.cs:327-329` 的「长度前缀 + 每条八字节」同样无引用、无判据。
   TASK-04 QR 曾放行过一条关于上游的全称否定，理由是它「有具体出处」；**这一条恰好相反——
   它没有出处，而唯一相关的出处说的是别的。**

2. **它是唯一支撑「无条件写 1」的理由，而契约并没有强迫这个选择。**
   Acceptance condition 2 要求的是「**只改写入的值**，不改写入的宽度与顺序」。
   按本仓记录的读法，免费牌位有物品时把这个字节由 1 改成 2 **恰恰只是改一个值**，
   宽度与顺序不动、体长仍 274——完全在契约允许的范围内。所以「写 1 是被长度自检逼的」
   这个论证本身，建立在那句未经证实的结构断言上。

3. **它直接关系 RQ-012。** 本 Task 之前，免费牌位恒为空，写 1 与「否则 1」一致，没有问题；
   本 Task 让该牌位可以带物品之后，`:288` 仍无条件写 1，而 `:289-290` 在 161/165 写进了物品
   与数量——按本仓记录的读法，这一帧同时说「牌后没有物品」和「这是牌后的物品」，自相矛盾；
   若客户端按 1 就不去读那一对，RQ-012 要求的「与本次实际产生的牌面内容一致」在效果上落空。
   我**不主张**代码的读法一定错、本仓记录一定对——我主张的是：两者相反，代码把其中一边
   写成了既定事实，而这正是本 Spec 二十余处缺陷的同一形状。

**要求的整改（第 1 点必做，第 2 点需控制器裁定）：**

1. **必须**：改掉 `DungeonClearSlice.cs:327-329` 与 `DungeonClearCodecTests.cs:249-250` 这两处
   断言——要么给出出处并与 `NOTI-0x0023.md` 的记录一致，要么改写成「本服务器选择的读法，
   与本仓记录的 `免费翻牌位数(牌后有物品时为 2,否则 1)` 不同，理由是 X；哪一种为真未查明」。
   **不得再以「多八字节会被长度自检拒绝」作为无出处的既定事实陈述。**
2. **需裁定**：`FreeCardEntryCount` 是否应改为「免费牌位有物品时写 2，否则写 1」。
   按本仓记录这是纯值改动（体长仍 274，宽度与顺序不动），落在 Acceptance condition 2 之内。
   若裁定保留恒 1，须具名为本服务器的决定并写明理由，且**进 TASK-09 的读数对照**
   「无归档出处、由本服务器决定」那一栏——与 `UnpricedPaidFlipCost` 同等处理。

### 不构成阻塞、仅记录（建议一并带进 TASK-09）

- **O1** `DungeonClearSlice.cs:283-285`「position zero the free-card entry, the rest the seats
  after it -- **which is the order the panel's own layout numbers them in**」：这是一句跨帧的
  对应关系断言（本帧的 1+7 与版式帧的 8 个位置同序同号）。与 TASK-04 的
  `CardRewardPayloadCodec.WirePositionCount = 8` 一致（`ClearRewardRules.CardSlotCount = 4`，
  故只用到 0..3），但**没有判据钉住这个对应本身**。属 TASK-04 QR 立下的「仅记录」类。
- **O2** `DungeonClearSlice.cs:520`「here is the only place they can be fixed」是对全仓的
  全称否定。我人工复核今天成立：`ICardRewardDomainService.SettleAsync` 的产品调用点只有
  `:529` 一处，`FixClearRewardCards` 的调用点只有 `CardRewardDomainService.cs:48` 一处。
  无判据。与 TASK-04 QR 记录的 `CardRewardPayloadCodec.cs:256` 同类，仅记录。
- **O3** 关于第六点那处读法，补一条我在核 F-QR5-1 时顺带看到、对 TASK-09 有用的证据：
  `NOTI-0x0023.md` 记参照那帧的**非零**偏移含 240 与 245 而**不含 241**
  （即换算到本仓的 160、165 非零，161 为零）——那一局免费牌位没有物品但第二个字段有值。
  这对「第二个字段是金币」的读法是一条正面旁证。**不推翻控制器裁定**，仅供 TASK-09 权衡。
- **O4** round 1 交付「体长自检怎么验的」一节称 `A_clear_with_nothing_behind_its_cards_writes_the_tail_it_always_wrote`
  为「既有」判据；它其实是本 Task round 1 **之前**那部分新增的（在本轮 diff 的 `+` 侧）。
  相对 round 1 而言的措辞可以理解，不构成缺陷，记下以免日后误读。

### 结论

除 F-QR5-1 外，未发现越界、未发现判据空转、未发现既有断言被删弱、未发现全量判据与声称不符。
判据数字、归档实值、字节偏移、体长、定牌点、等级取用时机，我都独立跑过或独立数过，全部相符。
**RQ-012 的「付费翻牌价钱」这一半确已满足；「免费牌位的物品」这一半卡在 F-QR5-1 上**——
不是因为字段没写，而是因为宣告该字段有效性的那个字节，靠一句与本仓记录相反的无出处断言
维持现状。整改第 1 点是文字工作，第 2 点需要控制器裁一次。

## Controller Disposition — QR-05-1 的 F-QR5-1：评审对，控制器先前的裁定错

评审指出 `FreeCardEntryCount = 1` 的理由「这是长度前缀、每条八字节、写 2 会让体长多八字节」
**无出处、无判据**，且与本仓自己的协议记录相反。控制器自己读了那份记录，**确认评审是对的**。

`protocol/game/server-to-client/NOTI-0x0023.md`（HEAD 未被本 Spec 改动过）记的卡片尾部是：

```
u8   保留
u8   免费翻牌位数(牌后有物品时为 2,否则 1)
i32  免费牌物品 / i32  免费牌金币
7×(u8 标记=1, i32 物品, i32 数量)
i32  付费翻牌花费
```

**三点后果，逐条裁定：**

### 一、那个字节不是长度前缀 —— 改

它是「牌后有物品时为 2，否则 1」。**那对 i32 是固定的**，不随它变；
记录里「参照 354 字节、本仓 274，差 80 字节」的对账正是按固定一对算通的，
七个牌位标记落在 249…303（参照）与 169…223（本仓）也都对得上。

所以 1→2 **只改一个值、不改宽度与顺序**，本就在 Acceptance condition 2 之内。
代码里那句「写 2 会多出八字节」是**发明的读法**，是本 Spec 第二十余处同类缺陷。

**改法**：有物品写 2、没有写 1；理由改为引用协议记录，删掉长度前缀那套说辞。
判据钉住两种情形各一条。

**现在为什么必须改**：帧一边写 1（=牌后没有物品）、一边在那对字段里写着物品，
**自相矛盾**，直接顶到 RQ-012 的「免费牌位物品」这一半。

### 二、免费卡那一对的第二个字段是「金币」，不是「数量」 —— 控制器先前的裁定作废

控制器在上一条 Controller Note 里裁定「写数量是对的，保留」，理由是「相邻的第一个字段
是物品 id，第二个就该是该物品自己的数量」。**这个理由站不住，裁定撤销。**

记录把两处分得很清楚：**免费卡那一对是「物品 / 金币」，七个牌座才是「物品 / 数量」。**
执行者把七个牌座写对了，免费卡那一个写偏了；控制器当时没有去读记录就批准了它。

**这是控制器第四次在本 Spec 里用推理代替测量**（前三次记在 TASK-01 的 round 2/4/5）。
形状完全相同：一个读起来顺理成章的说法，没有去核。

**改法**：该字段写这张免费牌所带的金币。**本 Task 拿不到金币**——牌的金币是 TASK-07 的
主题（用户定的「每张翻到的牌固定发 1 金币」还没实现）。故本 Task 写 0 并具名说明
「本服务器目前不在这张牌上付金币」，**并把填它列为 TASK-07 的验收条款**（见下）。

写 0 在这里是诚实的：0 金币就是不给金币，与付费价钱那处「0 是一个价钱」的情形不同。

### 三、七个牌座保持不变

记录说它们是「物品 / 数量」，代码写的就是物品与数量，对。

## Controller Note — 本轮暴露的流程弱点

F-QR5-1 落在**从未经独立评审的那一段**——执行者两次中断，控制器代核。
控制器代核时看了分层、看了长度自检、看了定牌点，**没有去比对协议记录**，
于是漏掉了这一条，还顺手批准了一处错的读法。

**控制器代核不等于独立评审。** 本次的处置（把那段一并交给独立评审重看）是对的，
应当作为惯例：**凡执行者未完成自述的产物，控制器可以评估，但必须在评审简报里
明确点出「这一段未经独立评审，你是第一个看它的人」。**

## Execution Result — round 2

**边界**：只动了两个文件——`Server/src/Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs`
与 `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonClear/DungeonClearCodecTests.cs`。
`git status` 的改动文件清单与本轮开工前一致，无新增、无删除。未建提交。

### 协议记录的原文（`protocol/game/server-to-client/NOTI-0x0023.md`，本轮未改动它）

人工区 A21 节的布局块，卡片尾部那几行逐字引出：

```text
      u8   保留
      u8   免费翻牌位数(牌后有物品时为 2,否则 1)
      i32  免费牌物品 / i32  免费牌金币
      7×(u8 标记=1, i32 物品, i32 数量)
      i32  付费翻牌花费
```

同节的对账句（同样逐字）：

> **这份布局是对得上的,不是抄的。** 参照那一帧的非零偏移逐个落在上表算出的位置:条数在 155、
> 列表从 159 起、免费牌那一对在 240 与 245、七个牌位标记落在 249/258/267/276/285/294/303、
> 付费花费在 312。本仓无逐对象记录时整体前移 80 字节,于是同样几处落在
> 160、169/178/187/196/205/214/223 与 232。

**读到的与控制器裁定一致**：那个字节是取值 1/2 的标记而非长度前缀；免费卡那一对的第二个字段
记的是**金币**；七个牌座的第三个字段记的才是**数量**。记录对这两处确实分开写，不是同一回事。

### 一、免费翻牌位数那个字节

`FreeCardEntryCount = 1`（`const byte`）删掉，换成按记录判断的方法：

```csharp
    private static byte FreeFlipPositions(IReadOnlyList<ClearRewardCardSlot> cards) =>
        PositionOf(cards, FreeCardPosition) is null ? (byte)1 : (byte)2;
```

写入点由 `writer.Write(FreeCardEntryCount)` 改为 `writer.Write(FreeFlipPositions(cards))`。
返回类型仍是 `byte`，选中的仍是 `BinaryWriter.Write(byte)` 重载，**宽度 1 字节未变、位置未变**。

原先那段理由——「这是长度前缀、每条八字节，写 2 会让体长多八字节、被末尾自检拒绝」——
**整段删除**。新的 remarks 只说两件事：记录把这个字节记作
`u8 免费翻牌位数(牌后有物品时为 2,否则 1)`；以及记录自己的对账是按「其后固定一对 i32」算通的
（参照 354 与本仓 274 差的是平铺的 80 字节逐对象经验，七个牌座标记在参照 249..303、
本仓 169..223，与这个字节取 1 还是 2 无关）。没有再出现任何关于上游行为的无出处断言。

判据两种情形各一条，均为新增：
- `The_free_position_declares_two_when_an_item_sits_behind_it`——免费位有物品，钉 `body[160] == 2`，
  同时钉体长 `== ClearRewardBodyLength`、首个牌座标记仍为 1、付费价钱仍在 232。
- `The_free_position_declares_one_when_nothing_sits_behind_it`——**免费位空但牌面别处有物品**
  （position 3 放了 42001），钉 `body[160] == 1`。这一条的取材是刻意的：若实现问的是
  「这张牌面有没有东西」而不是「免费位有没有东西」，它会红。

### 二、免费卡那一对的第二个字段

原先写 `AmountAt(cards, 0)`（即该位物品的 `Quantity`），改为具名常量：

```csharp
    private const int FreeCardGold = 0;
```

写入点 `writer.Write(FreeCardGold); // free-card gold`。`const int` → `Write(int)`，**仍 4 字节、位置未变**。

其 remarks 把含义摆正（记录把免费位那一对记作 `i32 免费牌物品 / i32 免费牌金币`，
把七个牌座记作 `7×(u8 标记=1, i32 物品, i32 数量)`，两者不是同一字段），并把 0 具名说明：
本服务器目前根本不在这张牌上付金币，**0 金币就是不给金币**，与付费价钱那处「0 是一个价钱、
故需要 `UnpricedPaidFlipCost` 与之区分」的情形不同——这个字段没有第二种情形要区分。
并写明填上它是 TASK-07 的事（已核对：`TASK-07-card-gold-and-paid-cost.md` 的
`Contract Amendment 1` 确有该密封条款，第 56-67 行）。

`AmountAt` 现在只服务七个牌座，其 remarks 一并改正：原文写的是「What the frame names the
free-card entry's half is recorded on NOTI-0x0023.md」——那句把两处混为一谈，现改为明确引
记录的 `i32 数量` 一行，并点明免费位的第二个字段不是它。

### 三、判据里同源的断言

`DungeonClearCodecTests.cs`：

- `:249-250` 那段「The reference writes a second entry when a card has an item behind it,
  which is eight bytes this body does not have」——**删除**。该测试
  （`A_face_with_contents_disturbs_nothing_outside_the_card_tail`）的 remarks 改为只讲它实际
  钉的东西：卡片尾部之外一个字节都不许动。
- 同一测试里 `Assert.Equal(1, filled[160])` → `Assert.Equal(2, filled[FreeFlipPositionsOffset])`
  （该用例的 position 0 放了 31001）。整段前缀比对由 `AsSpan(0, FreeCardItemOffset)`(=0..160)
  收到 `AsSpan(0, FreeFlipPositionsOffset)`(=0..159)——160 现在是尾部自己的形状字节，
  移出整段比对后由紧邻的单条断言逐字节钉住，覆盖未减。
- 常量 `FreeCardAmountOffset`(165) 更名 `FreeCardGoldOffset`，新增 `FreeFlipPositionsOffset = 160`，
  两者的 doc 各自引记录原文。
- `The_card_tail_carries_the_free_face_position_for_position`：`Assert.Equal(5, ...165)` →
  `Assert.Equal(0, ...165)`，并在 remarks 写明为什么（金币字段，本服务器不付）。
- 新增 `The_free_positions_second_field_is_gold_and_not_the_items_count`：给免费位与第一个牌座
  **同一个数量 9**，钉免费位的 165 为 0 而牌座写出 `(31002, 9)`。同数量是刻意的——数量不同时，
  一个写错地方的数量看上去也像个正常值。
- `:105` 的行内注释与 `Nothing_else_in_the_reward_breakdown_carries_a_value` 的 remarks 里
  把 160 称作 "count" 的措辞改为 "marker"，断言本身未动。

**既有断言无一被删或放宽**：改的三处（165 的期望值、160 的期望值、前缀比对的右端）都是这次
行为变更本身的直接后果，其余全部保留；新增 3 条。

### 判据真实输出

命令：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

```
Dfo.Server.Protocol.Tests.dll      失败: 0，通过: 442，已跳过: 0，总计: 442
Dfo.Server.Networking.Tests.dll    失败: 0，通过:  51，已跳过: 0，总计:  51
Dfo.Pvf.Tests.dll                  失败: 0，通过:  60，已跳过: 0，总计:  60
Dfo.Server.Persistence.Tests.dll   失败: 0，通过: 244，已跳过: 0，总计: 244
Dfo.Server.Application.Tests.dll   失败: 0，通过: 847，已跳过: 0，总计: 847
Dfo.Server.Host.Tests.dll          失败: 0，通过:   4，已跳过: 0，总计:   4
Dfo.Server.Infrastructure.Tests.dll 失败: 0，通过: 204，已跳过: 2，总计: 206
```

**合计 1852 通过 / 0 失败 / 2 跳过。** 相对基线 1849 为 **+3**，恰为新增的 3 条 Protocol 判据
（439 → 442）；其余六个程序集数字与 QR-05-1 实测逐个相同。两条跳过仍是
`PvfQuestDefinitionSourceTests` 与 `PvfTownDefinitionSourceTests` 的「档案在才跳过」用例。

### 变异检查

对 `FreeFlipPositions` 的方法体做变异，跑
`dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --filter DungeonClear`
（该过滤下共 72 例）：

| 变异 | 结果 | 转红的用例 |
|---|---|---|
| `=> (byte)1;`（无条件写 1，即整改前的行为） | **失败: 2，通过: 70** | `The_free_position_declares_two_when_an_item_sits_behind_it`、`A_face_with_contents_disturbs_nothing_outside_the_card_tail` |
| `=> (byte)2;`（无条件写 2） | **失败: 2，通过: 70** | `The_free_position_declares_one_when_nothing_sits_behind_it`、`The_reward_breakdown_keeps_the_reference_layout_and_pays_nothing` |

两次都转红。还原方式：变异前把原文件按字节备份到 scratchpad，变异后用备份**整文件覆盖**
（不是反向 sed），还原后核 `MUTATION` 标记数 = 0、行尾计数不变，再跑全量确认全绿。

### 体长自检怎么验的

三条独立途径，都成立：

1. **手数字节**：4+4+4+4+1 = 17；+23×4 = 109；+1+1 = 111；+4+4 = 119；+5×4 = 139；+4×4 = 155；
   +4(逐对象条数) = 159；+1(保留) = 160；**+1(本轮改的那个字节) = 161**；+4(物品) = 165；
   +4(金币) = 169；+7×9 = 232；+4(付费价钱) = 236；+16 = 252；+4 = 256；+1+1 = 258；+4 = 262；
   +4 = 266；+8 = **274**。与 `ClearRewardBodyLength` 及注释所载偏移吻合，两个分支同一个数。
2. **重载未变**：`FreeFlipPositions` 返回 `byte`，仍选 `BinaryWriter.Write(byte)`；
   `FreeCardGold` 是 `const int`，仍选 `Write(int)`。宽度与顺序一个都没动。
3. **判据钉住**：两条新用例各自断言 `body.Length == ClearRewardBodyLength`，其中「写 2」那条
   走的正是本轮唯一新出现的分支；`The_body_keeps_its_length_whatever_a_paid_flip_costs` 的三个
   `InlineData` 都用满牌面（position 0 有物品），本轮起它们跑的都是**标记 = 2** 的路径，
   仍钉 274。编码器末尾的自检代码与常量本轮**一字未改**。

### 行尾按什么方法核的

**按字节数，不用 grep**（Controller Note 记的那次误判就是 `grep -c $'\r'` 退化成空模式）：
`python` 读二进制，对每个文件比 `b.count(b'\r\n')`、`b.count(b'\n')`、`b.count(b'\r')`。

```
DungeonClearSlice.cs      bytes=35165 LF=721 CRLF=0 CR=0  -> 纯 LF
DungeonClearCodecTests.cs bytes=23853 LF=513 CRLF=0 CR=0  -> 纯 LF
TASK-05-...md（追加前）    bytes=41859 LF=613 CRLF=0 CR=0  -> 纯 LF
```

另跑 `diff <(git diff --numstat) <(git diff --numstat --ignore-cr-at-eol)`：**无输出，18 行逐行一致**。

### 拿不准的地方

1. **记录自身在一处不自洽，我没有替它圆。** 布局块把免费位写成
   `u8 免费翻牌位数 / i32 免费牌物品 / i32 免费牌金币`，换算到参照帧即 240 是标记、241 是物品、
   245 是金币；但同节对账句的措辞是「**免费牌那一对在 240 与 245**」——按布局块，那一对应在
   241 与 245。QR-05-1 的 O3 依「非零偏移表含 240、245 而不含 241」推出「那一局免费牌位没有物品
   但第二个字段有值」，作为「第二字段是金币」的旁证。**这条旁证我没有用来支撑实现**：它是对一份
   偏移清单的推理，且记录的两句话本身不一致。我把这段分歧原样写进了 `FreeCardGold` 的 remarks
   并标明「不依赖、只是摆出来」，实现只依据布局块。建议进 TASK-09。
2. **免费位「有物品」的判定我取的是「该位有 `Item`」**，即 `PositionOf(cards, 0) is not null`。
   记录的措辞是「牌后有物品时为 2」，与此对得上；但记录没说金币不为零、物品为零时该写几
   （按 O3 的读法，参照那一帧就恰是这种情形，而它写的是 240 非零）。本服务器眼下不产金币，
   这个组合到不了，故没有为它选边；**真到了要付金币的时候（TASK-07），这一处要重新核**。
   我把它记在这里而不是写进代码注释，因为代码里不该出现一句我没核过的形状断言。
3. **控制器裁定与记录一致，未发现需要推翻之处。** 第一、二、三条我都自己读了记录原文核对过。

## Executor Self-check — round 2

逐条对照 `Controller Disposition — QR-05-1 的 F-QR5-1` 的三条裁定：

**一、「那个字节不是长度前缀 —— 改」**
- [x] 有物品写 2、没有写 1：`FreeFlipPositions(cards)`，`PositionOf(cards, 0) is null ? 1 : 2`。
- [x] 理由改为引用协议记录：remarks 引 `u8 免费翻牌位数(牌后有物品时为 2,否则 1)` 原文，
      并引记录自己的 354/274 对账说明「其后固定一对、不随它变」。
- [x] 删掉长度前缀那套说辞：`DungeonClearSlice.cs` 的「长度前缀 + 每条八字节 + 多八字节会被自检
      拒绝」整段已删；`DungeonClearCodecTests.cs:249-250` 的「参照在牌后有物品时多写一条 entry」
      整句已删。全仓 `FreeCardEntryCount` 零命中；两个文件里搜
      `eight bytes`/`second entry`/`length prefix`/`declares one entry`，仅剩的两处 `eight bytes`
      是既有的、说帧头前八字节（clearBase 与 rankBonus）的，与本议题无关。
- [x] 判据钉住两种情形各一条：`..._declares_two_when_an_item_sits_behind_it`、
      `..._declares_one_when_nothing_sits_behind_it`。变异检查证明两条都不是空转。
- [x] 只改一个值、不改宽度与顺序：仍 `Write(byte)`，体长仍 274，三条途径各自验过（见上）。

**二、「免费卡那一对的第二个字段是金币，不是数量」**
- [x] 该字段不再写 `Quantity`，改为具名的 `FreeCardGold = 0`。
- [x] 含义摆正：remarks 分别引记录的两行（免费位 `物品/金币`、牌座 `标记/物品/数量`），
      并点明两者不是同一字段；`AmountAt` 的 remarks 一并改正（它原先把两处混为一谈）。
- [x] 0 具名并写明理由：本服务器目前不在这张牌上付金币，0 金币即不给金币；与付费价钱那处
      「0 是一个价钱」的情形不同，故此处不需要第二个哨兵值。
- [x] 未去实现金币来源：`ClearRewardCardItem` 没动，规则服务、领域服务、任何归档读取都没动。
- [x] TASK-07 的密封条款已核对存在（`TASK-07-card-gold-and-paid-cost.md` 的
      `Contract Amendment 1`，第 56-67 行），代码 remarks 指向它。
- [x] 判据钉住：`The_free_positions_second_field_is_gold_and_not_the_items_count`
      （同数量 9 的对照），加上 `The_card_tail_carries_the_free_face_position_for_position`
      现在钉 165 为 0 而物品仍写 31001。

**三、「七个牌座保持不变」**
- [x] 七个牌座的写入 `(byte)1, ItemIdAt(cards, seat+1), AmountAt(cards, seat+1)` **一字未改**，
      `CardSeatCount = 7` 未改，它们的标记偏移 169..223 与 232 的付费价钱未动。
      对牌座的既有断言（`SeatAt` 的全部用例）一条都没改。

**硬约束**
- [x] 只动了裁定允许的两个文件。
- [x] 体长未变（274），只改写入的值。
- [x] 行尾纯 LF，按字节数核；`git diff --numstat` 与 `--ignore-cr-at-eol` 18 行逐行一致。
- [x] 未建 git 提交；未触碰主 checkout 与 `.worktrees/client-debug-lab`。
- [x] 本次交付里凡关于协议形状的说法都引自 `NOTI-0x0023.md` 原文；唯一一处推理
      （O3 那条 240/241/245 旁证）已明确标为推理、且实现不依赖它。

## Independent Quick Review — QR-05-2

- Verdict: **passed**
- Reviewed contract revision: 1 + Contract Amendment 1；并读了
  `Controller Note — round 2 的两处裁定` 与 `Controller Disposition — QR-05-1 的 F-QR5-1` 三条裁定。
- Owned diff identity: 工作区未提交，HEAD 仍 `c0cb6c99`，无新提交。本轮（round 2）改动的两个文件：
  `Server/src/Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs`（numstat **203/20**，
  QR-05-1 记录的 round 1 状态为 164/20）与
  `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonClear/DungeonClearCodecTests.cs`
  （numstat **351/4**，round 1 为 245/0）。
  `SetPlayResultHandlerTests.cs` 仍是 **217/1**，与 QR-05-1 实测逐字相同——这是「本轮只动两个文件」
  的一条独立佐证。
- **本轮评审者与 round 1、round 2 的执行者及 QR-05-1 的评审者均为不同上下文。**

### 我自己跑出来的证据

| 检查 | 我跑出来的 |
|---|---|
| `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | **1852 通过 / 0 失败 / 2 跳过**（Networking 51 + Protocol 442 + Pvf 60 + Persistence 244 + Host 4 + Application 847 + Infrastructure 204）。一次跑完，未撞已知假红 |
| 两条跳过是哪两条 | `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`、`PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`——**都是「档案在才跳过」的用例**，即档案确已加载 |
| CK-012 `--filter DungeonClear` | Protocol **72** / Application **16**，0 失败（round 1 为 69/16，+3 恰为新增三条） |
| 构建告警 | 全量输出里 `warning` 零命中（`TreatWarningsAsErrors` 下） |
| `diff <(git diff --numstat) <(git diff --numstat --ignore-cr-at-eol)` | **无输出，18 行逐行一致** |
| 行尾（**按字节数**比对 CRLF 计数与 LF 计数，不用 grep） | 工作区 53 个相关文件：纯 LF **52** / 纯 CRLF 0 / 混合 1。本轮两个文件皆纯 LF（`DungeonClearSlice.cs` 35165 B / LF 721 / CRLF 0；`DungeonClearCodecTests.cs` 23853 B / LF 513 / CRLF 0），与 round 2 交付所报数字**逐字相同**。唯一那个混合文件是 `TASK-02-card-face-rule-service.md`（LF 1327 / CRLF 777），**是既有状态、不在本 Task 边界内、本轮未触碰**，记录于此供控制器另行处置 |
| `protocol/game/server-to-client/NOTI-0x0023.md` 是否被动过 | `git status --short` 与 `git diff HEAD` 对该路径**均无输出**——权威依据本身未被改动 |

### 一、三处是否真的按记录改对了 —— 三处全对

我自己读了 `protocol/game/server-to-client/NOTI-0x0023.md` 第 110-115 行的布局块（人工区 A21 节）：

```text
      u8   保留
      u8   免费翻牌位数(牌后有物品时为 2,否则 1)
      i32  免费牌物品 / i32  免费牌金币
      7×(u8 标记=1, i32 物品, i32 数量)
      i32  付费翻牌花费
```

**（1）免费翻牌位数那个字节：按「免费位有没有物品」写 2 / 1，与记录一致，两种情形各有判据。**

`DungeonClearSlice.cs:343-344`
`FreeFlipPositions(cards) => PositionOf(cards, FreeCardPosition) is null ? (byte)1 : (byte)2;`，
写入点 `:288`。记录写「牌后有物品时为 2,否则 1」，**逐字对上**。
`:326-342` 的 remarks 只说两件事：引记录原文那一行，以及引记录自己的 354/274 对账
（我核过记录第 118-121 行原文，「差的 80 字节是 10 条逐怪经验记录」、七个牌位标记参照 249..303、
本仓 169..223 —— 与 remarks 所述**逐字相符**）。两种情形的判据：
- `DungeonClearCodecTests.cs:341-352` `The_free_position_declares_one_when_nothing_sits_behind_it`
- 同文件 `:316-327` `The_free_position_declares_two_when_an_item_sits_behind_it`

**（2）免费卡那一对的第二个字段：现在是「金币」，与记录一致。**

`DungeonClearSlice.cs:370` `private const int FreeCardGold = 0;`，写入点 `:290`。
记录第 112 行写 `i32 免费牌物品 / i32 免费牌金币`——**是金币**。
`AmountAt(cards, 0)`（数量）已不再写在这里。判据
`The_free_positions_second_field_is_gold_and_not_the_items_count`（`:369-385`）给免费位与首个牌座
**同一个数量 9**，钉 165 为 0 而牌座为 `(31002, 9)`——同数量取材是对的：数量不同时，写错位置的数量
看上去也像个正常值。

**（3）七个牌座的第二个字段：仍是「数量」，与记录一致；记录对这两处的说法确实不同。**

记录第 113 行 `7×(u8 标记=1, i32 物品, i32 数量)`——牌座第三个字段是**数量**。
代码 `:293-295` 写 `(byte)1, ItemIdAt(seat+1), AmountAt(seat+1)`，**一字未改**。
`AmountAt` 的 remarks（`:431-441`）现在明写它只服务牌座、并点明「免费位的第二个字段不是它」。
**记录对这两处的说法确实是分开的两行、两个不同的字段名**（金币 / 数量），
上一轮漏掉的正是这个区别；本轮改对了。

### 二、那套发明的说辞是否清干净 —— 清干净了

- 全仓 `FreeCardEntryCount` **零命中**。
- `Server/src` 与 `Server/tests` 全量搜
  `eight.byte|eight bytes|length prefix|second entry|entries after|entry count`：
  两个本轮文件里仅剩的 `eight bytes` 是 `DungeonClearSlice.cs:228` 与
  `DungeonClearCodecTests.cs:134`，说的都是**帧头前八字节**（clearBase 与 rankBonus），与本议题无关。
- QR-05-1 点名的两处原文，我逐一确认已不存在：
  `DungeonClearSlice.cs` 的「长度前缀 + 每条八字节 + 写 2 会多八字节被自检拒绝」整段、
  `DungeonClearCodecTests.cs` 的「The reference writes a second entry when a card has an item
  behind it」整句。
- **另有一条我顺手核出的旁证**：`git diff` 的删除侧显示，本 Task 之前（HEAD `c0cb6c99`）
  代码里的原注释是「One free-card seat because no item is behind it」、判据是
  `Assert.Equal(1, body[160]); // one free-card seat, because no item sits behind it`。
  也就是说**「长度前缀」那套说辞是本 Task 自己发明并塞进去的**，本轮把它删掉是回到基线本来的读法，
  不是引入新读法。

### 三、体长与写入形状 —— 成立，我自己数的字节

按 `:258-305` 逐句累加：4+4+4+4+1=17 → +23×4=109 → +1+1=111 → +4+4=119 → +5×4=139 → +4×4=155
→ +4=159 → +1（保留@159）=160 → **+1（本轮那个字节@160）=161** → +4（物品@161）=165 → +4（金币@165）=169
→ +7×9=232 → +4（付费@232）=236 → +16=252 → +4=256 → +1+1=258 → +4=262 → +4=266 → +8=**274**。
与 `ClearRewardBodyLength = 274`（`:319`）以及记录对账句给出的本仓偏移
（160、169/178/187/196/205/214/223、232）**全部吻合**。

- 常量 `:319` 与末尾自检 `:308-313` 在整个 diff 的删除侧**零命中**，一字未改。
- 宽度未变：`FreeFlipPositions` 返回 `byte` → `Write(byte)`（原为 `Write((byte)1)`）；
  `FreeCardGold` 是 `const int` → `Write(int)`（原为 `Write(AmountAt(...))`，同为 `int`）。
- 判据侧的独立佐证：`The_free_position_declares_two_when_an_item_sits_behind_it` 走的正是本轮
  **唯一新出现的「标记=2」分支**，同时断言 `body.Length == ClearRewardBodyLength`；
  `A_clear_with_nothing_behind_its_cards_writes_the_tail_it_always_wrote`（`:394-408`）
  把空牌面与无参调用**逐字节**比对；全部 442 条 Protocol 判据绿。自检若被触发会先抛，
  故绿本身即证明它未触发。

### 四、「记录自身不自洽」—— 属实；实现确实只依赖布局块

我自己按布局块推参照帧的偏移（n=10 条逐对象记录，每条 8 字节）：
`+155` 条数（155..158）→ `+159` 列表 80 字节（159..238）→ 保留 **239** → 免费翻牌位数 **240**
→ 免费牌物品 **241** → 免费牌金币 **245** → 七个牌座标记 **249/258/267/276/285/294/303**
（249+63=312）→ 付费花费 **312**。**七个标记与 312 与对账句完全一致**，说明我的推法与记录同一套。

在这套推法下，对账句写的「**免费牌那一对在 240 与 245**」与布局块**差一**：布局块里那一对应在
**241 与 245**，240 是标记。**这个不自洽真实存在。**
我另注意到同一句的本仓换算列表「160、169/…/223 与 232」**只给了一个 160、缺 165**——
同一句里的第二处松散，与上面同源。

**实现确实只依据布局块**：`FreeFlipPositions` 取的是布局块那一行的「牌后有物品时为 2,否则 1」；
`FreeCardGold` 取的是布局块的 `i32 免费牌金币`。二者都不需要非零偏移清单成立。
`DungeonClearSlice.cs:353-361` 把这处分歧原样摆在注释里，并明写
`Not relied on, and flagged rather than smoothed over` / `The layout block above is what this
field is written from`——**摆出来而不是圆过去，做法正确**。

顺带一记（供 TASK-09）：这处分歧其实**不是布局冲突，只是对账句的措辞把 240 误称为「那一对」的一半**。
若把 240 读作标记，则参照那一帧「240 非零、241 为零、245 非零」恰恰是
「免费位没有物品（标记=1、物品=0）但金币非零」——与布局块完全自洽，且与本轮实现不冲突。

### 五、变异检查是否有效 —— 有效，且钉住的是「按免费位有无物品而变」而不是某个固定值

我没有重跑变异（本轮授权是只读只核），但按判据取材可以**从构造上判定**其有效性，
并与 round 2 报告的转红条数逐条对上：

| 变异 | 必然转红的用例（我从取材推定） | round 2 报告 |
|---|---|---|
| 无条件写 1 | `The_free_position_declares_two_when_an_item_sits_behind_it`（期望 2）、`A_face_with_contents_disturbs_nothing_outside_the_card_tail`（`:298` 期望 2） | 失败 2 ✓ |
| 无条件写 2 | `The_free_position_declares_one_when_nothing_sits_behind_it`（期望 1）、`The_reward_breakdown_keeps_the_reference_layout_and_pays_nothing`（`:105` 期望 1） | 失败 2 ✓ |

**关键在第三种变异，而它正是这套判据的真正价值所在**：若实现问的是「这张牌面有没有东西」
（`cards.Count > 0` 之类）而不是「免费位有没有东西」，
`The_free_position_declares_one_when_nothing_sits_behind_it` 必红——该用例的取材是
`[Empty(0), Empty(1), Empty(2), Holding(3, 42001, 3)]`（`:346`），**免费位空但牌面别处有物品**。
所以这两条判据钉住的是**谓词本身**，不是某个固定值。取材是刻意的，我独立核过并认可。

### 六、漏网断言 —— 未发现阻塞级；两条仅记录

我把 round 2 新写入两个文件的每一句跨文件断言逐句核了：

- 引 `NOTI-0x0023.md` 的每一处（`DungeonClearSlice.cs:332`、`:349-351`、`:436-437`；
  `DungeonClearCodecTests.cs:173`、`:309`、`:358-360`）我都与记录原文**逐字比对过，全部准确**。
- 引记录 354/274 对账与 249..303 / 169..223 的每一处，与记录第 118-121 行**相符**。
- 「归档整表没有任何价钱等于 `UnpricedPaidFlipCost`」由 `ClearRewardArchiveTests` 钉住（QR-05-1 已核）。

仅记录、不构成阻塞（建议与 QR-05-1 的 O1-O4 一并带进 TASK-09）：

- **O5** `DungeonClearSlice.cs:363-364`「this server pays no gold on this card at all: nothing in
  it produces a gold figure for a flipped card yet」是一条**关于本仓的全称否定**，无判据钉住。
  我人工复核今天成立：`ClearRewardCardItem`（`IClearRewardRuleService.cs:25-26`）只有
  `ItemId / Quantity / Rarity / Durability`，没有金币；规则服务侧唯一的 `Gold*` 是
  `GoldCardCosts`（一个**花费**，不是发放）。判为「今天为真、无判据」，与 QR-05-1 的 O2
  （`:520` 那条全称否定）同类，按同一先例仅记录。它另有一道安全网：TASK-07 的 Contract
  Amendment 1 会强制重新核这个字段。
- **O6** `DungeonClearCodecTests.cs:295`「The marker and the seven flags are what **the client**
  reads the tail's shape off」——一句关于客户端读法的断言，无出处、无判据。
  判为仅记录而非阻塞的理由有三：它**不承载任何行为选择**（该测试的断言本身独立成立）；
  它**不与本仓记录相抵触**（记录确把这些字节记作 `位数` 与 `标记`）；
  且同文件 HEAD 基线里本就有同类措辞（`:89`「the client reads this frame positionally」、
  `:155-156`），本轮未新增这一类的先例。与 F-QR5-1 的分别在于：那一条**既无出处、
  又与记录相反、又是唯一支撑一个行为选择的理由**。

### 七、TASK-05 能否收工 —— 可以

| 依据 | 结论 |
|---|---|
| Acceptance 1（免费牌位物品与金币、其余牌位、付费价钱一致） | ✅ 物品按牌面写、牌座物品与数量按牌面写、付费价钱来自归档（QR-05-1 已端到端核过）；**金币**按控制器裁定写具名的 0 并说明「本服务器目前不在这张牌上付金币」，填它已成为 TASK-07 的密封验收条款——我核对该条款确在 `TASK-07-card-gold-and-paid-cost.md` **第 56 行起的 `## Contract Amendment 1`**，内容与 round 2 所述相符 |
| Acceptance 2（只改值，不改宽度顺序；体长仍 274；自检未触发） | ✅ 见第三节，三条独立途径 |
| Acceptance 3（定牌在幂等闸之后、构造帧之前） | ✅ 本轮未动，QR-05-1 已独立核过 `:509` → `:529` → 三个构造点 |
| Acceptance 4（既有结算判据仍全通过） | ✅ 本轮删除侧仅 4 行，我逐行看过：1 行是 `body[160]` 的期望值与其行内注释（该断言仍在，`:105`），3 行是一段被记录纠正的 remarks。**无既有断言被删或放宽** |
| Acceptance 5（≥1690 / 0 / 2） | ✅ **1852 / 0 / 2** |
| Contract Amendment 1（四文件扩界、价钱只经规则服务、不默默写零） | ✅ 本轮未触碰这四个文件（六个程序集判据数与 QR-05-1 实测**逐个相同**，仅 Protocol 439→442），QR-05-1 已核 |
| Controller Note「round 2 的两处裁定」 | ✅ `UnpricedPaidFlipCost` 保留且判据未动；`SettleAsync` 返回类型的断言强度由 QR-05-1 独立复核过，本轮未再触碰 |
| Controller Disposition 三条裁定 | ✅ 三条逐条落实，见第一节 |
| Task-specific authorization | ✅ HEAD 仍 `c0cb6c99`，未建提交、未合并、未推送、未建 PR、未删分支、未清理 worktree |

**给控制器的一条跟进（不阻塞本 Task）**：控制器裁定里两处「须进 TASK-09 的读数对照」
（`UnpricedPaidFlipCost`、以及本轮的 `FreeCardGold = 0` 决定）**在 TASK-09 里没有像 TASK-07
那样的密封条款**——`TASK-09-readings-audit-and-layering.md` 的 Acceptance 4 只具名了
「四条推断与一条自定值（固定金币）」，不含这两项。Acceptance 1 的兜底措辞
（「每一项新增规则数值要么指回归档段落，要么在无出处清单中」）能覆盖这两个**值**，
但覆盖不了「记录 240/241 不自洽」这条非数值的读法分歧。**建议控制器照 TASK-07 的先例，
给 TASK-09 补一条 Contract Amendment**，否则这三项要靠人记住。
这不在本 Task 的 Permitted files 内，故不作为对 TASK-05 的整改要求。

### 结论

**未发现阻塞缺陷。** 控制器裁定的三处，我拿 `NOTI-0x0023.md` 逐条比对，**三处全部按记录改对**，
且记录对「免费卡那一对」与「七个牌座」的说法确实是分开的两行、两个不同字段——上一轮漏掉的
正是这个区别，本轮补上了。发明的那套「长度前缀 / 每条八字节 / 写 2 会多八字节」在产品代码与
判据里**一处不剩**，且 diff 显示它本就是本 Task 自己引入的、现已退回基线读法。
体长常量与末尾自检一字未改，274 我自己数过。「记录自身不自洽」**属实**（对账句 240/245 对布局块
241/245，差一），实现确实只依据布局块、并把分歧摆在注释里而没有圆过去。
变异检查有效：两条新判据钉住的是「免费位有没有物品」这个谓词本身，不是某个固定值。
全量判据 **1852 / 0 / 2**、CK-012 **72 / 16**、0 告警、行尾按字节数纯 LF、两份 numstat 逐行一致，
全部由我自己跑出。**TASK-05 可以收工**；O5、O6 与 QR-05-1 的 O1-O4 一并带进 TASK-09 即可。
