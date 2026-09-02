# TASK-07 — 金币两侧——发出去的与收进来的

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables:

给账号钱包增一条「扣款，不足则失败且不改变余额」的能力；翻到有东西的牌时按
固定一份计入账号；付费翻牌按归档成本表先收费后发奖，收费失败则拒绝不发奖。

- Referenced spec requirements: RQ-006, RQ-007
- Referenced plan sections: `plan.md` 的 T-007；边界 BD-017, BD-013, BD-021
- Owned boundary: BD-017, BD-013, BD-021
- Permitted files or interfaces:

- `Server/src/Dfo.Server.Abstractions/Inventory/IAccountMoneyService.cs`（扩展）
- `Server/src/Dfo.Server.Application/Inventory/AccountMoneyService.cs`（扩展）
- `Server/src/Dfo.Server.Abstractions/Persistence/InventoryTables.cs`（扩展）
- `Server/src/Dfo.Server.Persistence/Inventory/InventoryStateTables.cs`（扩展）
- `Server/src/Dfo.Server.Protocol/Game/CardReward/`（收费与发金币）
- `Server/tests/`（新增判据）

- Dependencies: TASK-06
- Required dependency outputs: TASK-06 的翻牌到货链路
- Acceptance conditions:

1. 新增的扣款能力在余额不足时**失败且不改变余额**。既有的加款把结果夹在零、
   不足时静默夹到零而不失败，用它收费会把付不起的玩家算成付过了——不得沿用。
2. 固定金币值**单独具名**，其所在之处写明：这是本服务器的决定不是归档读数，
   归档说翻牌不出金币也没有算金额的段落；日后定真规则时只改这一处。
3. 付费翻牌按归档成本表取该等级价钱，先收费后发奖。
4. 余额不足者被拒，金币与背包均未变。
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
| CK-016 | `dotnet test Server/86JP.Server.slnx --nologo --filter AccountMoney` | 扣款成功与不足失败两条判据通过；不足时余额未变 |
| CK-017 | `Non-command:` 端到端付费翻牌两例 | 充足者按归档该等级价钱扣减并得到内容；不足者被拒且余额与背包均未变 |
| CK-018 | `Non-command:` 核对固定金币值 | 它单独具名，所在之处写明是本服务器的假设及其理由 |
| CK-S | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 不低于 1690 通过、0 失败、2 跳过 |

- Required evidence: CK-016 的判据输出；CK-017 的两例观察；CK-018 的具名位置；全量判据输出。

## Contract Amendment 1 — 免费牌那一对的金币字段由本 Task 填

**来源**：TASK-05 的 QR-05-1 与其后的控制器裁定。

结算帧的卡片尾部里，免费卡那一对是「物品 / **金币**」
（`protocol/game/server-to-client/NOTI-0x0023.md` 的记载）。
TASK-05 已把物品填对，**金币暂写 0**，因为牌的金币是本 Task 的主题、当时还不存在。

**本 Task 的 Acceptance conditions 增补一条**：

> 结算帧免费卡那一对的金币字段，与本服务器实际在该牌上付出的金币一致，
> 并有判据钉住。若仍决定不在该牌上付金币，则 0 是对的，但须具名说明而不是留着不管。

**允许为此改动** `Server/src/Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs`
（仅该字段及其取值来源），不需另行扩界。

**记这一条的理由**：本 Spec 拒绝过一次「把一条要求劈成两半留给下个任务」
（TASK-05 的付费价钱缺口，控制器当时选择在归属地做完）。这一次之所以允许跨任务，
是因为**该字段要报的东西本身尚不存在**，不是为了省事；且它被写成了本 Task 的密封验收条款，
而不是一句「后续再说」。

## Execution Result — round 1

### 改了哪些文件（全部在许可清单内）

源：

- `Server/src/Dfo.Server.Abstractions/Inventory/IAccountMoneyService.cs`：加 `TrySpendAsync`。
- `Server/src/Dfo.Server.Application/Inventory/AccountMoneyService.cs`：实现之，并挡住负数。
- `Server/src/Dfo.Server.Abstractions/Persistence/InventoryTables.cs`：`IAccountCargoStateTable` 加
  `TryTakeValue32Async`。
- `Server/src/Dfo.Server.Persistence/Inventory/InventoryStateTables.cs`：带条件的一条 UPDATE。
- `Server/src/Dfo.Server.Protocol/Game/CardReward/SelectCardCommand.cs`：两个新拒绝理由、
  命令加账号与等级、结果加两个金额、收费与发金币、具名常量 `ClearRewardCardGold.PerFlippedCard`。
- `Server/src/Dfo.Server.Protocol/Game/CardReward/CardRewardHandlers.cs`：`SelectCardHandler`
  多取一份已发布会话，把账号与等级填进命令。
- `Server/src/Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs`：Amendment 1 那个字段，
  由常量 `FreeCardGold` 改为取自同一位置查找的 `FreeCardGoldAt(cards)`；加一行 using。

判据：

- `Server/tests/Dfo.Server.Persistence.Tests/Inventory/SqliteAccountCargoStateTableTests.cs`（+4 条）
- `Server/tests/Dfo.Server.Application.Tests/Inventory/AccountMoneyServiceTests.cs`（新文件，3 条）
- `Server/tests/Dfo.Server.Application.Tests/World/PaidCardFlipCostTests.cs`（新文件，2 条，读真档案）
- `Server/tests/Dfo.Server.Protocol.Tests/Game/CardReward/CardRewardTests.cs`（+10 条，含 fixture 扩写）
- `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonClear/DungeonClearCodecTests.cs`（+3 条，
  并按 Amendment 1 改了两处既有期望值，见下）
- `Server/tests/Dfo.Server.Protocol.Tests/Game/GroundPickup/GroundPickupTests.cs`：`FakePurse`
  补上新接口成员（接口加成员逼出来的编译修复，没动任何断言）

### 扣款不足怎么失败的

存储层一条语句，条件与减法同在一句：

```sql
UPDATE account_cargo_state SET value32 = value32 - @amount, updated_at = CURRENT_TIMESTAMP
WHERE account_id = @accountId AND value32 >= @amount
RETURNING value32;
```

没有行返回就是「付不起」，而且**什么都没写**。`TryTakeValue32Async` 因此答 `null`，
`IAccountMoneyService.TrySpendAsync` 原样传出 `null`，命令翻成
`SelectCardStatus.NotEnoughGold` 并且不认领牌位。既有的 `AddValue32Async` 依旧夹在零——
两条路的差别本身被一条判据钉住（`An_account_short_of_the_price_keeps_every_coin_where_adding_would_empty_it`
在同一个钱包上先扣 101 被拒、余额仍是 100，再加 -101 被夹成 0）。

两个附带决定，都写在代码 remarks 里：**负数一律抛**（负的「扣」就是发钱，
收费接缝不该能因为一个符号写反而发钱）；**没有钱包行的账号只能被扣零**
（行不存在即持有零，扣零是扣得起的，扣任何正数扣不起）。

### 固定金币值具名在哪

`Server/src/Dfo.Server.Protocol/Game/CardReward/SelectCardCommand.cs` 里的
`internal static class ClearRewardCardGold` 的 `PerFlippedCard = 1`，全仓唯一一处字面值。
它的 XML remarks 写明：档案里翻牌不出金币、也没有把金币命中折算成金额的段落，
所以没有内容源可读，这个 1 是本服务器的占位决定；并写明它踩在
「两个类别概率槽读成物品满值/金币为零」这条推断的反面上。

覆盖它的判据：`DungeonClearCodecTests.The_card_gold_this_server_pays_is_one_and_is_this_servers_own_decision`
直接断言它是 1（改成 2 会红）。其余用到金额的判据一律经这个名字取值，
所以「日后定真规则」只改这一处 + 那一条钉值判据。

**放在切片里而不是规则服务里，是本次最拿不准的取舍**，见下方「拿不准的地方」。

### 免费卡金币字段最后填了什么及理由

填**该牌实际付出的金币**：免费面 0 号位有东西时写 `ClearRewardCardGold.PerFlippedCard`，
没东西时写 0。取值由 `FreeCardGoldAt(cards)` 算出，用的是**与旁边那个物品字段、
以及前面那个标记字节同一个位置查找**（`PositionOf(cards, FreeCardPosition)`）。

理由：RQ-006 说「翻到一张有东西的牌，账号金币增加固定的一份」，免费面 0 号位正是结算帧
这一对所描述的那张牌；本服务器确实会在翻它时付这一份，所以按 Amendment 1 的条款，
0 不再是对的。它也不是对该牌的第二份独立主张——帧只是报告将要发生的支付。

### 「金币非零而物品为零」怎么判断的

**判定：到不了，而且是结构上到不了，不是碰巧。** 金币的条件与物品、标记字节共用同一个
位置查找：0 号位有东西 → 标记 2、物品非零、金币 = 1；没东西 → 标记 1、物品 0、金币 0。
两者不可能分家。协议记录（`protocol/game/server-to-client/NOTI-0x0023.md` 的布局块，
`u8 免费翻牌位数(牌后有物品时为 2,否则 1)` 与 `i32 免费牌物品 / i32 免费牌金币`）
没有描述过「金币非零而物品为零」这一种，本实现因此也不产生它。

这条判定被 `Gold_behind_the_free_position_never_arrives_without_an_item` 钉住：它跑遍
四个位置的四种有无组合，只要金币非零就要求物品非零且标记为 2，反之亦然。
把 `FreeCardGoldAt` 改成无条件付款会让它红（实测见变异 5）。

一处诚实的补充：这只保证「物品字段为零时金币也为零」。若某张牌的档案物品 id 本身是 0，
物品字段会写 0 而 `Item` 非 null，金币仍会是 1——那是内容侧的缺陷，不是本帧的判断，
本任务不越界去查证档案里是否存在 id 为 0 的可掉落物。

### 判据真实输出

基线（本任务动手前，同一命令）：

```
Networking 51 / Protocol 462 / Pvf 60 / Persistence 244 / Application 848 / Host 4 / Infrastructure 204(+2 skip)
合计 1873 通过、0 失败、2 跳过
```

改完（`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`）：

```
已通过! - 失败: 0，通过:   60，已跳过: 0，总计:   60 - Dfo.Pvf.Tests.dll
已通过! - 失败: 0，通过:  479，已跳过: 0，总计:  479 - Dfo.Server.Protocol.Tests.dll
已通过! - 失败: 0，通过:   51，已跳过: 0，总计:   51 - Dfo.Server.Networking.Tests.dll
已通过! - 失败: 0，通过:  248，已跳过: 0，总计:  248 - Dfo.Server.Persistence.Tests.dll
已通过! - 失败: 0，通过:  853，已跳过: 0，总计:  853 - Dfo.Server.Application.Tests.dll
已通过! - 失败: 0，通过:    4，已跳过: 0，总计:    4 - Dfo.Server.Host.Tests.dll
已通过! - 失败: 0，通过:  204，已跳过: 2，总计:  206 - Dfo.Server.Infrastructure.Tests.dll
```

合计 **1899 通过 / 0 失败 / 2 跳过**（+26）。该结果是在把全部变异还原之后重跑得到的。

CK-016（`dotnet test Server/86JP.Server.slnx --nologo --filter AccountMoney`）：

```
已通过! - 失败: 0，通过: 3，已跳过: 0，总计: 3，持续时间: 166 ms - Dfo.Server.Application.Tests.dll
```

三条即 `AccountMoneyServiceTests` 的扣款成功、余额不足被拒且余额未变、负数被拒。
（过滤串 `AccountMoney` 按全限定名匹配，存储层那四条在 `SqliteAccountCargoStateTableTests` 里，
名字不含 `AccountMoney`，所以不在这 3 条内，它们由全量判据覆盖。）

CK-017（端到端付费翻牌两例，读真实档案）：
`PaidCardFlipCostTests` 两条都真的跑起来了（不是跳过——变异 6 时它们红过）。

- 充足者：账号先存 `档案价 + 500`，翻付费面 0 号位 → `Flipped`，`GoldCharged` 等于
  `ClearRewardRuleService.PaidFlipCost(86)` 当场从档案读出的那个数（测试里不写字面值），
  账号行余额 = `500 + GoldPaid`，牌上的装备进了容器。
- 不足者：账号只存 `档案价 - 1` → `NotEnoughGold`，`GoldCharged`/`GoldPaid` 都是 0，
  `Slots` 为空，**回读 SQLite 那一行仍是 `档案价 - 1`**，容器里什么都没有。

档案在该等级上到底是多少（10300），由既有的 `ClearRewardArchiveTests` 对档案断言，
本任务不重复转写这个数。

CK-018：见上「固定金币值具名在哪」。

### 变异检查结果（含怎么确认工具没骗我）

做法：`scratchpad/mutate.py`，**改与还原都是重写文件内容**（不 move、不 copy 文件），
写完显式 `os.utime` 把 mtime 推到当前时刻，所以还原后的 mtime 一定比变异时更新，
MSBuild 不可能沿用带变异的程序集。脚本还检查待替换串在文件里恰好出现一次，
出现零次或多次直接失败——避免「以为改了其实没改」这种假绿。

**第一个变异就真的红了**（下表 1），所以这不是一个从头到尾没红过的空循环。

| # | 变异 | 结果 |
|---|---|---|
| 1 | 去掉扣款 SQL 的 `AND value32 >= @amount` | 红：`SqliteAccountCargoStateTableTests.An_account_short_of_the_price_keeps_every_coin_where_adding_would_empty_it`（1 失败 / 247 通过） |
| 2 | 去掉 `TrySpendAsync` 的负数守卫 | 红：`AccountMoneyServiceTests.A_charge_below_zero_is_refused_rather_than_paid_out`（1 失败 / 2 通过） |
| 3 | 空牌也发金币（`paid` 恒为固定值） | 红：`CardRewardTests.A_position_that_held_something_pays_the_cards_gold(index: 1, holds: False)` |
| 4 | `PerFlippedCard` 由 1 改成 2 | 红：`DungeonClearCodecTests.The_card_gold_this_server_pays_is_one_and_is_this_servers_own_decision` |
| 5 | `FreeCardGoldAt` 无条件付款 | 红 5 条：`Gold_behind_the_free_position_never_arrives_without_an_item` 的两个组合、`A_position_at_the_end_of_the_face_is_written_at_the_end_of_the_tail`、`Nothing_else_in_the_reward_breakdown_carries_a_value`、`The_free_positions_gold_is_what_a_flip_of_that_position_pays` |
| 6 | 付费翻牌不收费（`ChargeAsync` 直接放行） | 红：读真档案的 `PaidCardFlipCostTests` 两条全红（2 失败 / 0 通过） |
| 7 | 认领失败时不退款（退款条件取反） | 红：`CardRewardTests.A_paid_flip_that_lost_its_position_is_given_its_money_back` |
| 8 | 「档案没定价」当成免费放行 | 红：`CardRewardTests.A_paid_flip_at_an_unpriced_level_is_refused_rather_than_free` |

八个变异全部按预期变红，没有一个静默通过。

变异 3 那一轮里 `ItemPathLiveTests.AnItemDraggedOverASocketLandsInTheDatabaseAndIsAcknowledgedByteForByte`
也红了一次。它与本任务无关，属 `AGENTS.md` 记过的那类假红（真 socket / 先读后写同库），
在此前后的两次全量判据里都通过，未再复现。

### 行尾按什么方法核的

两道，都过：

1. `diff <(git diff --numstat) <(git diff --numstat --ignore-cr-at-eol)` → 完全一致。
2. 按字节核：脚本遍历 `git status --porcelain -uall` 列出的每个 `.cs/.md/...`，
   比 `b.count(b'\r\n')` 与 `b.count(b'\n')`。72 个文件里只有 1 个含 CRLF，
   是 `.rks-agent/.../tasks/TASK-02-card-face-rule-service.md`（777 / 1327），
   **本任务没有碰过它**，是既有产出带进来的。本任务新增与修改的每一个文件 CRLF 计数都是 0。
   没有用 `grep -c` 那个会退化的写法。

### 意外与处理

1. **两处既有期望值必须改。** `DungeonClearCodecTests` 有两条断言在免费位有物品时要求金币字段为 0
   （`The_card_tail_carries_the_free_face_position_for_position` 与
   `The_free_positions_second_field_is_gold_and_not_the_items_count`）。Amendment 1 恰恰要求
   这个字段不再是 0，所以这两处期望值改成了具名常量。后一条的用意（「数量不会写到金币里」）
   没有被削弱反而更强：它传入的数量是 9，金币是 1，我另加了一条 `Assert.NotEqual(9, ...)`。
   除这两处外，没有改动任何既有断言或期望值。
2. **既有用例 `Turning_a_card_over_never_asks_the_rules_anything` 仍然通过**，因为它翻的是免费牌，
   而只有付费牌才会去问价钱。我没有动它（含它的注释），另加了一条
   `A_paid_flip_asks_the_rules_for_its_price_and_nothing_else` 作为对面。
   诚实提示：它的注释「Nothing on this path asks the rules what a card is worth」现在窄于它的名字，
   我按 BD-021「不改既有用例」选择不动，请评审者决定是否要改这句话。
3. **fixture `RefusingRules` 的 `PaidFlipCost` 由「抛」改成「答」**。既有用例
   `Taking_a_position_of_one_face_leaves_the_other_face_alone` 翻的就是付费牌，不改它会崩。
   改的是 fixture 行为，不是任何断言；它仍然在 `ChanceOf`/`FaceFor` 上抛，计数照旧。
4. **接口加成员逼出 `GroundPickupTests.FakePurse` 的编译修复**，补了 `TrySpendAsync`
   （按真实语义实现：不够就拒）。没有动那个文件的任何断言。
5. 命令处理器没有嵌套 dispatch、没有直接问规则服务：价钱经既有的
   `ICardRewardDomainService.SettleAsync` 拿。它在「牌已定下」这个判定**之后**才被调用，
   所以永远不会是它把牌定下来的那一次（代码 remarks 里写明了这个次序的必要性）。
6. 收费与认领的次序：先查「未被认领」→ 收费 → 认领。认领若在这之间被别人抢走，
   把钱原样退回并答 `AlreadyFlipped`；反过来（先认领后收费）没法退，因为牌已经亮给玩家了。

### 拿不准的地方

1. **具名常量落在 CardReward 切片、由 DungeonClear 切片引用。** 按 `AGENTS.md`，
   「表达归档或游戏语义的常量」属于规则输入、应由拥有该主题的规则服务持有——那会是
   `IClearRewardRuleService` / `ClearRewardRules`，**不在本任务许可清单内**；而
   「切片内不跨协议引用」又说共用要上提到 endpoint 层（`Game/GameProtocol.cs`，同样不在清单内）。
   我没有扩界，选了清单内唯一能做到「只写一处」的摆法，并注意到：
   (a) 契约把 `CardReward/` 明确写成「收费与**发金币**」的所在；
   (b) Amendment 1 允许改 DungeonClearSlice 的「该字段**及其取值来源**」，字面上就是这条引用；
   (c) `DungeonClearSlice.cs` 本来就 `using` 了 `MonsterDeath` 与 `StateSynchronization`
   两个兄弟切片，这不是本仓第一例。
   **但这仍是一处与 `AGENTS.md` 有张力的摆放**；若评审认为该常量必须进规则服务，
   那就是一次扩界，应由控制器裁定而不是我自行决定。
2. **付费价钱按「角色当前等级」取，而不是「该局结算时的等级」。**
   `ClearRewardSettlementResult` 的注释本身警告过这个差别：结算帧上的价钱是发经验**之前**
   的等级算的，而翻牌发生在发经验之后；若这一局把角色顶过成本表的一个等级段，
   实收价可能与结算帧公布的价不同。要消掉它得让 run 记住结算时的价钱，那是
   `IDungeonRunDomainService` / `ICardRewardDomainService` 的形状，不在本任务清单内。
   我把这一条写进了 `SelectCardCommand.CharacterLevel` 的文档，而不是掩盖它。
3. **`TrySpendAsync` 扣零的语义**：没有钱包行的账号被扣零算成功（持有零、扣走零）。
   本任务的牌路走不到（价钱为零时照常调用，且此时任何钱包都付得起），
   但这是我定的接口语义，不是从任何地方读来的。
4. **付费翻牌被拒时服务器保持沉默**（沿用 TASK-06 立下的「翻不动就不回帧」）。
   协议记录里没有「余额不足」的应答形状，我没有发明一个。客户端会看到牌没翻开。
5. `SelectCardHandler` 现在还要求会话**已发布**（为了拿等级）。免费翻牌因此也多了一条
   拒绝路径。已发布是翻牌的正常前提（发布是选人流程的一步），但这是行为的扩大，
   我加了 `A_flip_without_a_published_entry_never_reaches_the_run` 把它钉住并写明理由。

## Executor Self-check — round 1

### 对照 Task Unit Contract 的 Acceptance conditions

1. **「新增的扣款能力在余额不足时失败且不改变余额；不得沿用加款」** —— 成立。
   新增 `TrySpendAsync` / `TryTakeValue32Async`，条件与减法同一条 UPDATE，不足时零行返回、
   零字节写入、答 `null`。判据：`SqliteAccountCargoStateTableTests` 四条
   （能付／付光／付不起且加法会清零／无钱包行）与 `AccountMoneyServiceTests` 三条。
   「不得沿用加款」被
   `An_account_short_of_the_price_keeps_every_coin_where_adding_would_empty_it` 正面钉住：
   同一钱包上，扣拒绝而加清零。变异 1 与 2 均变红。
2. **「固定金币值单独具名，写明是本服务器的决定而非归档读数，日后只改一处」** —— 成立。
   `ClearRewardCardGold.PerFlippedCard = 1`，全仓唯一字面值，remarks 写明档案不出金币、
   没有算金额的段落、这是用户知情后的占位决定，并点出它踩在类别概率读法的反面。
   判据 `The_card_gold_this_server_pays_is_one_and_is_this_servers_own_decision` 钉住取值
   （变异 4 变红），其余判据一律经这个名字取值。
3. **「付费翻牌按归档成本表取该等级价钱，先收费后发奖」** —— 成立。
   价钱经 `ICardRewardDomainService.SettleAsync` → `IClearRewardRuleService.PaidFlipCost(level)`
   取得（不重读内容源，命令处理器不直接问规则服务）；次序是查未认领 → 收费 → 认领 → 发金币 → 进包。
   `PaidCardFlipCostTests.A_paid_flip_costs_the_archives_price_for_that_level` 用真档案跑通，
   扣减额当场与 `PaidFlipCost(86)` 相比。变异 6 变红。
4. **「余额不足者被拒，金币与背包均未变」** —— 成立。
   `CardRewardTests.An_account_that_cannot_afford_a_paid_flip_keeps_its_money_and_its_card`
   （差一枚：拒绝、钱包不变、背包空、补一枚后同一位置仍可翻——证明没被认领）与
   `PaidCardFlipCostTests.An_account_a_coin_short_of_the_archives_price_is_refused_and_keeps_it`
   （回读 SQLite 行确认余额未变）。
5. **「全量判据不低于 1690 通过、0 失败、2 跳过」** —— 成立：**1899 / 0 / 2**（基线 1873 / 0 / 2）。

### 对照 Contract Amendment 1

> 结算帧免费卡那一对的金币字段，与本服务器实际在该牌上付出的金币一致，并有判据钉住。
> 若仍决定不在该牌上付金币，则 0 是对的，但须具名说明而不是留着不管。

—— 成立，且不是取 0：本服务器确实在该牌上付金币，所以该字段在 0 号位有东西时写
`ClearRewardCardGold.PerFlippedCard`、没东西时写 0，取值经与物品字段同一个位置查找算出。
钉住它的判据：`The_free_positions_gold_is_what_a_flip_of_that_position_pays`（有／无两例）、
`Gold_behind_the_free_position_never_arrives_without_an_item`（四种组合的不变量）、
以及原有两条按此更新期望值的用例。变异 5 让其中 5 条变红。
改动只落在 `DungeonClearSlice.cs` 的该字段及其取值来源（一个 `private static` 方法 + 一行 using），
没有动该文件其他任何一处。

### 对照 Required checks

- **CK-016** `--filter AccountMoney`：0 失败 / 3 通过；含扣款成功与不足失败，后者断言余额未变。✔
- **CK-017** 端到端付费翻牌两例：`PaidCardFlipCostTests` 两条，读真实档案，
  充足者按档案该等级价钱扣减并拿到内容，不足者被拒且金币与容器均未变。✔
- **CK-018** 固定金币值具名核对：见上，位置、理由、判据齐备。✔
- **CK-S** 全量：1899 / 0 / 2。✔

### 对照 Required methods

- **TDD**：判据先写，先经历不编译／红（变异循环里每条新判据都被单独验证过会红），再变绿。
- **分层**：内容源只被规则服务读；命令处理器只调领域服务（`ICardRewardDomainService`），
  不调规则服务、不嵌套 dispatch；handler 只做四类动作（解身份、只读查询、发命令、发送）。
  `SelectCardCommand` 仍是 `Transactional`，钱包写入落在同一个边界内。
- **归档读法写在 remarks 里**：帧字段引 `protocol/game/server-to-client/NOTI-0x0023.md` 的布局块；
  价钱引 `[gold card cost table]`（经既有的规则服务，本任务没有新增档案读法）；
  金币那个 1 明确标为**不是**档案读数。
- **using 次序**：无 `System.*` 新增；新 using 按本仓既有惯例插入，未做字母序重排。
- **`TreatWarningsAsErrors`**：整解决方案 0 警告 0 错误。
- **`ValueTask` 全覆盖**：新增的 `TrySpendAsync` / `TryTakeValue32Async` / `ChargeAsync` 均为 `ValueTask`。

### 边界自查

本轮改动的文件全部落在许可清单内（源 7 个 + 判据 6 个，判据全在 `Server/tests/`）。
没有碰 `ProtocolModule.cs`、`ICardRewardDomainService.cs`、`IClearRewardRuleService.cs`、
`ClearRewardRules.cs`、`Game/GameProtocol.cs`、Schema、或 `protocol/` 下任何记录。
未建 git 提交。上面第 1 条「拿不准的地方」是唯一一处我认为评审可能判成需要扩界的取舍，
按要求没有自行扩大，而是写在这里交给控制器。

## Controller Disposition — round 1 报的两处，一处批准扩界，一处判为缺陷须修

执行者两处都**没有自行扩界**，把问题报了上来。**两处都做对了。**

### 一、共用常量的家 —— 批准扩界，上提到端点层

`ClearRewardCardGold.PerFlippedCard` 落在 `CardReward` 切片，却被 `DungeonClear` 引用。
执行者指出架构上它的家应是端点层，而端点层不在许可清单内，故未动。

**裁定：上提到 `Server/src/Dfo.Server.Protocol/Game/GameProtocol.cs`。**

先例明确：`AGENTS.md` 的「切片内不跨协议引用，要共用就上提到 endpoint 层」，
而本仓 `GameProtocol.SecretShopNpcId` 正是上一个重构因「两个切片都要它」而上提的同类。
`DungeonClearSlice` 现有的几个兄弟切片 `using` 引的是**消息类型**（上一个重构记为已知偏差），
**共用常量不属于那一类**。

Permitted files or interfaces 增补：
`Server/src/Dfo.Server.Protocol/Game/GameProtocol.cs`（仅这一个常量及其注释）。

### 二、公布价与实收价可能不同 —— 判为缺陷，须修

执行者如实记下：付费价钱按角色**当前**等级取，而结算帧公布的价按**结算时**等级，
「跨段升级时可能不同」。

**控制器查证后判定：这不是边角情况，是常态缺陷。**

`CardRewardDomainService.SettleAsync` 在幂等判断**之前**就用本次调用传入的等级重算价钱，
幂等路径把**新算的价**配上**已定死的牌面**返回。而两个调用方传的等级不同：

- 结算路径传的是**发经验之前**的等级（TASK-05 刻意如此，理由是牌该从「刚打完那局」的
  等级带抽，那条本身是对的）；
- 翻牌路径传的是**当前**等级，即发经验**之后**的。

付费成本表逐级定价（等差 120），因此**只要这一局让角色升了级，公布价与实收价必然不同**。
通关升级是常事，不是边角。**服务器给玩家看一个价、收另一个价。**

执行者在代码注释里为「重算」给的理由是「价钱是档案对某个等级的回答，不该在本局里存一份
可能过期的拷贝」。这个顾虑真实，**但比不过一致性**：帧已经把价告诉玩家了，
一份「可能过期的拷贝」远好过「说一个数收另一个数」。

**修法**：价钱与牌面同时定死——本局结算时定一次，此后翻牌收的就是公布的那个价。
这与牌面的设计一致（结算时定死、点击只是揭示），也正是 RQ-002 的立场。
**允许为此改动** `Server/src/Dfo.Server.Abstractions/World/IDungeonRunDomainService.cs`、
`Server/src/Dfo.Server.Application/World/DungeonRunDomainService.cs`、
`Server/src/Dfo.Server.Abstractions/World/ICardRewardDomainService.cs`、
`Server/src/Dfo.Server.Application/World/CardRewardDomainService.cs`。

**必须有判据钉住**：同一局在角色升级之后再翻牌，收的价与结算时公布的价相同。
**这条判据要能在「重算」的实现下变红**——否则它没钉住任何东西。

### 记一句给流程的话

这两处都是执行者**发现了、没自行处理、报上来**的。其中第二处若它自行"顺手改掉"，
控制器不会知道曾有过这个选择；若它自行扩界改常量的家，也就没人复核那个先例对不对。
**把选择摆出来而不是替人做掉，是这两轮里最有价值的行为。**

## Execution Result — round 2

### 中断说明：断在哪一步

本轮会话在改到一半时因连接中断挂过一次，恢复时工作区编译不过。恢复后先核了实际状态：

- **常量上提的产品侧已经落地**：`GameProtocol.cs` 第 45 行已有
  `public const int ClearRewardCardGold = 1;`，`SelectCardCommand.cs` 与
  `DungeonClearSlice.cs` 两个引用点也已改名。
  （协调者转达的状态说「新家还没建」，实测不是这样，新家已建；核实后按实际状态续做，未重做。）
- **断点在判据侧**：测试文件仍按旧名 `ClearRewardCardGold.PerFlippedCard` 引用，
  且 `CardRewardTests.cs:1052`、`PaidCardFlipCostTests.cs:172` 两处 `FixClearRewardCards`
  还没补上新参数 —— 这就是那 4 个编译错误。
- **那条必须有的判据当时还没写**。

恢复后从这三点续做，全部做完。

### 一、共用常量上提到端点层

`ClearRewardCardGold` 那个 `internal static class` 从
`Server/src/Dfo.Server.Protocol/Game/CardReward/SelectCardCommand.cs` 整体删除，
成为 `Server/src/Dfo.Server.Protocol/Game/GameProtocol.cs` 里的一个常量成员：

```csharp
public const int ClearRewardCardGold = 1;
```

**写法照 `GameProtocol.SecretShopNpcId`**：那处的 remarks 是「点名哪两个切片要它 + 为什么
放这儿（切片不得伸进另一个切片）」。本常量的 remarks 第一段就是这个句式
（"the card slice, which pays it when a position is turned over, and the clear frame,
which announces beside that position what turning it over will pay ... because a slice may not
reach into another -- the same reason `SecretShopNpcId` is here"），
后面三段是原来那套「这是本服务器的决定不是归档读数」的论证，原样搬过去没有删减。
所以裁定里「仅那一个常量及其注释」是字面做到的：不是把一个类挪过去，是那一个常量加它的注释。

上提后：

- 全仓 `PerFlippedCard` 零命中，`ClearRewardCardGold` 只有 `GameProtocol.cs` 一处定义。
- `DungeonClearSlice.cs` 上一轮为此加的
  `using Dfo.Server.Protocol.Game.CardReward;` **已删掉**——那是上一轮唯一的跨切片引用，
  现在这个切片不再伸进任何兄弟切片。这正是裁定引 `AGENTS.md`「切片内不跨协议引用」
  想要的结果，也解掉了上一轮「拿不准的地方 1」。
- 两个引用点（`SelectCardCommand.cs` 的 `paid`、`DungeonClearSlice.cs` 的
  `FreeCardGoldAt`）都指向端点层那一个。

### 二、公布价与实收价：价钱与牌面同时定死

**价钱定死在哪一步**：定死在 `DungeonRunDomainService.FixClearRewardCards` 那一次
compare-and-swap 里——**与牌面同一次交换**，不是牌面之后再补一步。

具体形状：

1. `ClearRewardCardSet` 加第四个位置参数 `int? PaidFlipCost`（无默认值，故意的：
   给默认值就等于允许有人静默造出一副「有牌面没价钱」的牌）。
2. `IDungeonRunDomainService.FixClearRewardCards` 签名加 `int? paidFlipCost`，
   实现把它放进 `new ClearRewardCardSet(free, paid, Empty, paidFlipCost)`，
   与牌面在同一个 `with` 里、同一次 `TryUpdate` 里落地。
   幂等分支（`current.ClearRewardCards is not null` 即 `return current`）原样不动，
   所以「第二次到达保留第一次的牌面」自动变成「也保留第一次的价钱」。
3. `CardRewardDomainService.SettleAsync` 的次序**颠倒过来**了，这是修复的核心：
   - 旧：先 `var cost = cardRules.PaidFlipCost(settlement.CharacterLevel);` 再判幂等，
     于是幂等路径把**新算的价**配上**已定死的牌面**。
   - 新：**先判幂等**，命中就 `new ClearRewardSettlementResult(already, already.PaidFlipCost)`
     ——不碰规则服务；只有真正要定牌面时才 `cardRules.PaidFlipCost(...)` 算一次。
   - 定完之后答案里的价**从 `fixedCards.PaidFlipCost` 读回**，不是用局部变量 `cost`：
     两个结算并发时输的那一方要报赢家的价配赢家的牌，而不是自己的价配别人的牌。

于是「结算时定一次，此后翻牌收的就是公布的那个价」在两个层面都成立：
run 层面一次交换定死两样东西，seam 层面幂等路径永不重算。

### 取舍写在哪：没有假装从来没有过另一种选择

裁定要求把上一轮「重算」的理由写出来而不是抹掉。写在三处：

- `CardRewardDomainService.SettleAsync` 幂等分支上方的整段注释，明说这是
  **a reversal of an earlier reading of this seam**，把旧理由原样复述
  （"a price is the archive's answer about a level, so asking again rather than keeping a
  copy means no copy can outlive the file it came from"），然后写它输给了什么
  （"True, and beaten by consistency" 加上两个等级取自发经验前后，于是报一个价收另一个价），
  最后写为什么这个代价可接受（"Within one run the file cannot move; the announced figure
  can, and must not"）。
- `ClearRewardCardSet.PaidFlipCost` 的 `<param>`：明写它是归档答案的一份拷贝、
  **可能过期是真实成本**，以及为什么仍然选它。
- `CardRewardDomainServiceTests.A_second_settlement_keeps_the_cards_already_fixed` 里
  那两行断言旁的注释——上一轮那句 "because it is the archive's answer about a level rather
  than something the first settlement decided and the run then kept" 现在是错的，
  改成了记录这次反转与其理由，而不是删掉了事。

### 那条「升级后仍收公布价」的判据怎么写的

主判据（读真档案、走真命令处理器、真 SQLite 钱包）：

`Server/tests/Dfo.Server.Application.Tests/World/PaidCardFlipCostTests.cs`
的 `A_clear_that_levelled_the_character_still_charges_what_it_announced`。

```
announced      = 档案在 LevelBeforeTheAward (= Level - 1) 的价
afterTheAward  = 档案在 Level 的价
Assert.NotEqual(announced, afterTheAward)                          先钉住两者确实不同
world = new Flip(provider, funded: announced + afterTheAward, settleAtLevel: LevelBeforeTheAward)
Assert.Equal(announced, world.Announced)                           结算时公布的价
Assert.Equal(announced, await world.PriceTheSettlementAnswersAt(Level))   升级后再问结算，仍是它
result = await world.Turn(Paid, 0, standingAt: Level)              升级之后才翻牌
Assert.Equal(announced, result.GoldCharged)                        收的就是公布的那个
Assert.Equal(funded - announced + result.GoldPaid, await world.Purse())   回读 SQLite 那一行
```

四个设计上的讲究：

1. **公布价不是测试自己写下来再和自己比。** `settleAtLevel` 这条路让 fixture 走
   `CardRewardDomainService.SettleAsync` 真结算（牌面是真抽的），`world.Announced`
   是那次结算答出来的 `PaidFlipCost`。测试里不出现任何价钱字面值。
2. **账号按 `announced + afterTheAward` 充值，两个价都付得起。** 这样「重算」实现下失败的
   是「收了多少」这条断言，而不是「余额不足被拒」这个副作用——失败要指着缺陷本身。
3. **两个等级的价必须不同，先断言。** 若哪天档案把这两级定成同价，这条判据就钉不住东西了，
   `Assert.NotEqual` 会当场变红而不是静默放行。
4. `LevelBeforeTheAward = Level - 1` 是常态而非精心挑选的边角：结算取的是发经验**前**的等级，
   翻牌发生在发经验**后**，通关升一级就是差一。

配套的 seam 级判据（不依赖档案，任何环境都跑）：
`CardRewardDomainServiceTests.A_clear_asked_again_after_a_level_up_keeps_the_price_it_settled_at`
——在 `Level` 结算，再用 `Level + 1` 问一次，要求两次答同一个价、且
`rules.Priced` 恰好是 `[Level]`（规则服务只被问过那一次价）。
fixture 只在 `Level` 定价、别处答 null，所以「重算」实现下它不只是答错数，是答 null。

run 层的第三条：`DungeonRunClearRewardCardTests.Cards_are_fixed_once_and_a_second_fix_leaves_the_first_pair_standing`
扩写了——第二次 `FixClearRewardCards` 带 `OtherPrice` 到达，断言 run 留的仍是 `Price`，
并 `Assert.NotEqual(Price, OtherPrice)` 防止两个相等的数让这条断言变成废话。

### 把它改回「重算」，是不是真的变红——是

变异做法沿用上一轮的 `scratchpad/mutate.py`：改与还原**都是重写文件内容**（不 move、不 copy），
写完显式 `os.utime` 推 mtime；替换串必须在文件里恰好出现一次，否则脚本直接失败。

**变异 1（裁定点名要求的那一个）**：把幂等分支改回按本次调用的等级重算——

```csharp
// 改前
new ClearRewardSettlementResult(already, already.PaidFlipCost));
// 改后
new ClearRewardSettlementResult(
    already, cardRules.PaidFlipCost(settlement.CharacterLevel)));
```

全量判据结果：**失败 9 条**（Protocol.Tests 6 失败 / 473 通过，Application.Tests 3 失败 / 852 通过）。
主判据的真实失败消息：

```
失败 Dfo.Server.Application.Tests.World.PaidCardFlipCostTests.A_clear_that_levelled_the_character_still_charges_what_it_announced
  错误消息:
   Assert.Equal() Failure: Values differ
   Expected: 10180
   Actual:   10300
```

即「公布 10180、实收 10300」——**正是裁定描述的那个缺陷本身**，不是因为抛异常或余额不足而红。
seam 级那条同时红：

```
失败 Dfo.Server.Application.Tests.World.CardRewardDomainServiceTests.A_clear_asked_again_after_a_level_up_keeps_the_price_it_settled_at
  错误消息:
   Assert.Equal() Failure: Values differ
   Expected: 5150
   Actual:   null
```

其余 7 条是同一变异的连带：`CardRewardTests` 里 6 条付费翻牌用例撞上
`RefusingRules.PaidFlipCost` 的抛出（重算实现会去问规则服务，而本轮起翻牌路径不该问），
以及 `A_second_settlement_keeps_the_cards_already_fixed` 的 `rules.Priced` 断言。

本轮另外三个变异，都在还原前先观察到真的变红：

| # | 变异 | 结果 |
|---|---|---|
| 1 | 幂等路径改回按本次调用等级重算 | 红 9 条，见上（主判据 Expected 10180 / Actual 10300） |
| 2 | `GameProtocol.ClearRewardCardGold` 由 1 改成 2 | 红：`DungeonClearCodecTests.The_card_gold_this_server_pays_is_one_and_is_this_servers_own_decision`（2 失败 / 477 通过，另一条是既有假红 `ItemPathLiveTests`） |
| 3 | `FreeCardGoldAt` 无条件付款 | 红 5 条：`Gold_behind_the_free_position_never_arrives_without_an_item` 两个组合、`A_position_at_the_end_of_the_face_is_written_at_the_end_of_the_tail`、`Nothing_else_in_the_reward_breakdown_carries_a_value`、`The_free_positions_gold_is_what_a_flip_of_that_position_pays`——确认常量改名后 DungeonClear 侧仍被钉住 |
| 4 | `FixClearRewardCards` 丢掉传入的价、固定写 null | 红 7 条：`Cards_are_fixed_once_and_a_second_fix_leaves_the_first_pair_standing`、`The_settlement_hands_back_what_a_paid_flip_costs`、`A_second_settlement_keeps_the_cards_already_fixed`、`A_clear_asked_again_after_a_level_up_keeps_the_price_it_settled_at`、`PaidCardFlipCostTests` 三条全红 |

四个全部按预期变红，没有一个静默通过。还原后 `mutation-state.json` 已不存在，
两个被改过的源文件回读确认内容正确（`already.PaidFlipCost`、`ClearRewardCardGold = 1`）。

上一轮的另外 6 个变异本轮没有重跑，它们覆盖的是本轮没有改动的代码（扣款 SQL、负数守卫、
空牌不发金币、认领失败退款、未定价拒绝）；本轮重跑的是与本轮改动相关的那几处。

### 判据真实输出

全量（`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`，
在全部变异还原之后重跑）：

```
已通过! - 失败:     0，通过:   479，已跳过:     0，总计:   479 - Dfo.Server.Protocol.Tests.dll
已通过! - 失败:     0，通过:    60，已跳过:     0，总计:    60 - Dfo.Pvf.Tests.dll
已通过! - 失败:     0，通过:    51，已跳过:     0，总计:    51 - Dfo.Server.Networking.Tests.dll
已通过! - 失败:     0，通过:   248，已跳过:     0，总计:   248 - Dfo.Server.Persistence.Tests.dll
已通过! - 失败:     0，通过:     4，已跳过:     0，总计:     4 - Dfo.Server.Host.Tests.dll
已通过! - 失败:     0，通过:   855，已跳过:     0，总计:   855 - Dfo.Server.Application.Tests.dll
已通过! - 失败:     0，通过:   204，已跳过:     2，总计:   206 - Dfo.Server.Infrastructure.Tests.dll
```

合计 **1901 通过 / 0 失败 / 2 跳过**。round 1 收工是 1899/0/2，**+2**，失败 0，跳过仍为 2
（那 2 条是 `PvfQuestDefinitionSourceTests` 与 `PvfTownDefinitionSourceTests` 的缺档案用例，与本任务无关）。

+2 的来源：Application.Tests 853 到 855，即本轮新增的两条判据
（`A_clear_that_levelled_the_character_still_charges_what_it_announced` 与
`A_clear_asked_again_after_a_level_up_keeps_the_price_it_settled_at`）。
Protocol.Tests 仍是 479：本轮在那里没有新增用例，只把上一轮的
`A_paid_flip_asks_the_rules_for_its_price_and_nothing_else` 改写成
`A_paid_flip_asks_the_rules_nothing_because_its_price_was_already_fixed`（见下）。

CK-016（`dotnet test Server/86JP.Server.slnx --nologo --filter AccountMoney`）本轮未受影响，复跑：

```
已通过! - 失败: 0，通过: 3，已跳过: 0，总计: 3，持续时间: 154 ms - Dfo.Server.Application.Tests.dll
```

`dotnet build Server/86JP.Server.slnx --nologo` 干净通过，无 warning
（本仓 `TreatWarningsAsErrors`，删掉那条不再需要的 using 后也没有新的未使用 using 告警）。

### 行尾怎么核的

两道，都过：

1. **按字节核**：脚本遍历 `git status --porcelain -uall` 列出的每个文件，比
   `b.count(b'\r\n')` 与 `b.count(b'\n')`。72 个文件里只有 1 个含 CRLF，是
   `.rks-agent/.../tasks/TASK-02-card-face-rule-service.md`（777 / 1327）——
   **本轮与上一轮都没有碰过它**，是既有产出带进来的。
   本轮新增与修改的每一个文件 CRLF 计数都是 0。没有用 `grep -c` 那种会退化的写法。
2. `diff <(git diff --numstat) <(git diff --numstat --ignore-cr-at-eol)` 完全一致，逐行相同。

### 改了哪些文件（全部在裁定的许可清单内）

源（7 个）：

- `Server/src/Dfo.Server.Protocol/Game/GameProtocol.cs`：新增那一个常量及其注释（仅此）。
- `Server/src/Dfo.Server.Protocol/Game/CardReward/SelectCardCommand.cs`：删掉旧的
  `ClearRewardCardGold` 类；一处引用改名；`CharacterLevel` 的文档重写；
  `ChargeAsync` 的 remarks 重写。
- `Server/src/Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs`：删掉那行跨切片
  using；两处引用改名（一处在 `<see cref>` 里）。
- `Server/src/Dfo.Server.Abstractions/World/IDungeonRunDomainService.cs`：
  `ClearRewardCardSet` 加 `int? PaidFlipCost` 及其 `<param>`；`FixClearRewardCards` 签名加参数。
- `Server/src/Dfo.Server.Application/World/DungeonRunDomainService.cs`：价钱进同一次交换。
- `Server/src/Dfo.Server.Abstractions/World/ICardRewardDomainService.cs`：
  `ClearRewardSettlementResult` 与 `SettleAsync` 的 remarks 跟上新语义。
- `Server/src/Dfo.Server.Application/World/CardRewardDomainService.cs`：本次修复的核心。

判据（7 个，均在 `Server/tests/`）：

- `Server/tests/Dfo.Server.Application.Tests/World/PaidCardFlipCostTests.cs`：新判据 1 条，
  外加 `Flip` fixture 增设 `settleAtLevel` / `Announced` / `PriceTheSettlementAnswersAt` /
  `Turn(..., standingAt)`。
- `Server/tests/Dfo.Server.Application.Tests/World/CardRewardDomainServiceTests.cs`：新判据 1 条，
  外加修掉那句已经变错的注释、并把 `rules.Priced` 断言加进既有用例。
- `Server/tests/Dfo.Server.Application.Tests/World/DungeonRunClearRewardCardTests.cs`：
  8 个调用点补参数；「定一次」那条扩写为也覆盖价钱。
- `Server/tests/Dfo.Server.Protocol.Tests/Game/CardReward/CardRewardTests.cs`：改名 4 处；
  `World` 把价钱随牌面定下；`RefusingRules` 恢复为全员抛出；一条用例改写（见下）。
- `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonClear/DungeonClearCodecTests.cs`：改名 7 处。
- `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonClear/LeaveDungeonTests.cs`、
  `ResultScreenTests.cs`、`SetPlayResultHandlerTests.cs`：
  接口加参数逼出来的 fixture 编译修复；`SetPlayResultHandlerTests` 的
  `new ClearRewardCardSet(...)` 补第四个实参。没有动这三个文件的任何断言。

### 意外与处理

1. **上一轮的一条判据必须改写，这是修复的必然结果而不是推翻。**
   上一轮加的 `A_paid_flip_asks_the_rules_for_its_price_and_nothing_else` 断言
   `RuleQuestions == 1`。价钱定死之后，幂等路径根本不问规则服务，**付费翻牌问规则的次数是 0**。
   改写成 `A_paid_flip_asks_the_rules_nothing_because_its_price_was_already_fixed`，
   断言 `RuleQuestions == 0` **并且** `GoldCharged == FixturePrice`——加后半句是为了它不能被
   「这次翻牌压根没收费」满足。这比原来严格。
2. **顺带还掉了上一轮的一笔账。** 上一轮「意外 3」记过：`RefusingRules.PaidFlipCost`
   为了迁就重算实现，从「抛」改成了「答」，于是既有用例
   `Turning_a_card_over_never_asks_the_rules_anything` 的注释
   （"The fixture's rule service throws on every member"）变得比它的名字窄。
   本轮把 `RefusingRules` 恢复成**全员抛出**，那句注释重新为真，
   而 `World` 需要的价钱改由 fixture 直接随牌面定下。上一轮记的那条张力就此消掉，
   **我没有改动那条既有用例的任何一个字**。
3. **`SelectCardCommand.CharacterLevel` 现在不再决定价钱，但我没有删它。** 见下方「拿不准的地方 1」。
4. 上一轮「拿不准的地方 1」（常量落在 CardReward 切片、与 `AGENTS.md` 有张力）
   随本轮第一件事一并消解：常量在端点层，切片不再跨引用。
5. 上一轮「拿不准的地方 2」（付费价按当前等级取）正是本轮第二件事修掉的东西。

### 拿不准的地方

1. **`SelectCardCommand.CharacterLevel` 现在在这条路径上什么都不决定，我保留了它。**
   价钱来自已定死的牌面，等级只会被「还没定牌的结算」用到，而调用方在收费之前已经因
   `NoCardsFixed` 拒绝了那种情况。所以它是惰性的。
   保留的理由写在它的文档里：价钱仍然经 `ICardRewardDomainService.SettleAsync` 这个 seam 到达，
   而这个 seam 是按等级键入的。
   **删掉它是可以的，但代价是连带删掉上一轮刚立的「会话必须已发布」这条前提**
   （`SelectCardHandler` 取等级才需要 `runtimeSessions`；账号 id 不需要），
   连同上一轮为它写的判据 `A_flip_without_a_published_entry_never_reaches_the_run`。
   那是推翻上一轮的产出，超出本次「边界明确的整改」，所以我把选择摆出来而不是替人做掉。
   若评审认为该删，那是下一轮的事。
2. **`ClearRewardSettlementResult` 现在有两处同一个价**（它自己的 `PaidFlipCost` 与
   `Cards.PaidFlipCost`）。二者只在一个地方构造、且后者永远是前者的来源，构造不出分歧；
   但形状上确实冗余。去掉外层那个要动 `DungeonClearSlice` 与 `SelectCardCommand` 两个读点，
   我判断收益不抵改动面，保留并在文档里写明它永远等于 `Cards` 的那一个。
3. **拷贝过期的窗口。** 价钱现在是归档答案在本局内的一份拷贝。窗口是一次 run 的生命周期
   （run 结束即消失），期间归档文件不会被换掉。这是裁定接受的代价，但它是真实存在的，
   记在这里而不是当它不存在。
4. **`ClearRewardCardSet` 的第四个参数没给默认值**，所以每个构造点都被编译器逼着表态。
   代价是三个测试构造点必须改。我认为这个代价该付：给默认 null 就等于允许静默造出
   「有牌面没价钱」的牌集。
5. `PaidCardFlipCostTests` 的新判据用**真抽的牌面**（走真 `SettleAsync`），
   而既有两条仍用摆好的牌面（它们要断言背包里落了什么，抽出来的位可能是空的）。
   两种 fixture 路径并存由 `settleAtLevel` 是否为 null 分开，
   分支的理由写在那个参数的 `<param>` 里。

## Executor Self-check — round 2

逐条对照 `Controller Disposition — round 1` 的两条裁定：

### 裁定一：上提到 `Server/src/Dfo.Server.Protocol/Game/GameProtocol.cs`

| 裁定要求 | 做到没有 | 证据 |
|---|---|---|
| 上提到 `GameProtocol.cs` | 是 | `GameProtocol.cs:45` `public const int ClearRewardCardGold = 1;` |
| 仅那一个常量及其注释 | 是 | 该文件本轮只多出这一个成员及其 XML 注释；未动任何既有成员 |
| 照 `SecretShopNpcId` 的写法 | 是 | remarks 第一段照它的句式点名两个切片，加上 "a slice may not reach into another"，并 `<see cref="SecretShopNpcId"/>` 指回先例 |
| 原处不应再有第二份定义 | 是 | 全仓 `PerFlippedCard` 零命中；`ClearRewardCardGold` 仅 `GameProtocol.cs` 一处定义 |
| 两个引用点都指向端点层那一个 | 是 | `SelectCardCommand.cs:216`、`DungeonClearSlice.cs:380`（及 `:365` 的 `<see cref>`）均为 `GameProtocol.ClearRewardCardGold` |
| （裁定的动机）切片不跨协议引用 | 是 | `DungeonClearSlice.cs` 的 `using Dfo.Server.Protocol.Game.CardReward;` 已删除 |
| 该常量仍被判据钉住 | 是 | 变异 2：改成 2，`The_card_gold_this_server_pays_is_one_and_is_this_servers_own_decision` 变红 |

### 裁定二：价钱与牌面同时定死，且必须有判据钉住、且该判据要能在「重算」下变红

| 裁定要求 | 做到没有 | 证据 |
|---|---|---|
| 价钱与牌面**同时**定死 | 是 | 同一次 `TryUpdate`：`ClearRewardCardSet(free, paid, Empty, paidFlipCost)`；不存在「有牌面没价钱」的中间状态 |
| 本局结算时定一次 | 是 | `SettleAsync` 先判幂等再问规则；`rules.Priced == [Level]` 被判据钉住 |
| 此后翻牌收的就是公布的那个价 | 是 | `A_clear_that_levelled_the_character_still_charges_what_it_announced`：升级后翻牌 `GoldCharged == announced`，且回读 SQLite 行确认 |
| 只允许改裁定列出的 4 个文件 | 是 | 改的正是 `IDungeonRunDomainService.cs` / `DungeonRunDomainService.cs` / `ICardRewardDomainService.cs` / `CardRewardDomainService.cs`，无第五个 |
| **必须有判据钉住** | 是 | 主判据（端到端读真档案）、seam 级（`CardRewardDomainServiceTests`）、run 级（`DungeonRunClearRewardCardTests` 扩写），共三层 |
| **该判据要能在「重算」下变红** | **是，实测** | 变异 1 把幂等路径改回按本次调用等级重算，主判据红：`Expected: 10180 / Actual: 10300`；seam 级红：`Expected: 5150 / Actual: null`；全量 9 失败 |
| 改注释时把取舍写出来，不假装没有过另一种选择 | 是 | `CardRewardDomainService` 幂等分支上方整段写明这是 "a reversal of an earlier reading" 并复述旧理由；`ClearRewardCardSet.PaidFlipCost` 的 `<param>` 写明拷贝可能过期是真实成本；测试里那句已变错的注释也改成了记录反转而非删除 |

### 其余判据要求

| 要求 | 结果 |
|---|---|
| 扣款成功与不足失败（后者断言余额未变） | 通过，本轮未改动；CK-016 复跑 3/3 |
| 端到端付费翻牌两例 | 通过，`PaidCardFlipCostTests` 原两条加本轮新增 1 条共 3 条，均真跑（变异 4 时全红，非跳过） |
| 断言必须由判据钉住或删去 | 本轮新增的每条断言都在变异 1 或变异 4 下被观察到变红 |
| 协议形状出处只能是 `protocol/` 下的记录 | 本轮未新增任何协议形状断言；未从简报抄任何数字（10180 / 10300 / 5150 均为运行输出与既有档案判据，测试代码里不含这些字面值） |
| 行尾一律 LF，按字节数核 | 通过：72 个文件仅 1 个含 CRLF，是本轮与上轮都未触碰的 TASK-02.md |
| `git diff --numstat` 与 `--ignore-cr-at-eol` 逐行一致 | 通过，`diff` 无输出 |
| 通过数应上升、失败 0、跳过仍为 2 | 通过：1899 到 **1901**，失败 0，跳过 2 |
| 不超出许可清单，超出就停下写 Blocker Report | 未超出，无 Blocker |
| 不创建 git 提交、不动主 checkout 与其他 worktree | 未创建提交；所有编辑路径均带 `.worktrees/flipping-a-clear-reward-card/` |

## Controller Note — 恢复中断时控制器又测错一次

恢复该执行者时，控制器在消息里断言「常量上提卡在半空：`PerFlippedCard` 在
`GameProtocol.cs` 和 `Game/CardReward/` 下都搜不到，旧家已删、新家还没建」。

**这是错的。** 执行者核实后回报：常量的产品侧其实**已经落地**，
`GameProtocol.cs:45` 已有它、两个引用点也已改名；断点只在判据侧。它按实际续做，未重做。

**控制器错在哪**：搜的是**旧名** `PerFlippedCard`，而上提时**连名字一起改了**
（现在叫 `ClearRewardCardGold`，与端点层同类常量的命名一致）。
搜不到旧名，就被当成了「不存在」。

**这是控制器在本 Task 内第二次测错**（第一次是行尾那次 `grep -c $'\r'` 退化成空模式），
两次形状相同：**搜一个已经不适用的词，把「搜不到」当成「不存在」**。

**代价**：若执行者照单全收去「重做」上提，会在端点层写出第二份定义或把已对的东西改坏。
**它没有照单全收，而是先核实**——这正是本 Spec 一路在要求的行为，
这次是执行者对控制器用上了它。

**给控制器的做法**：断言「某物不存在」之前，至少换一个搜法（搜类型名、搜值、搜引用点），
或直接读那个文件。**一次 grep 的阴性结果不构成不存在的证据**——
尤其是在一次改动可能连带改名的时候。

## Controller Disposition — 惰性参数：保留并具名，进 TASK-09

执行者摆出一个选择：`SelectCardCommand.CharacterLevel` 现在不再决定价钱，成了惰性参数；
删掉它会连带删掉上一轮刚立的「会话必须已发布」前提及其判据，属于推翻上一轮。

**裁定：保留，但必须在代码里点明它在翻牌这一侧是惰性的。**

理由：它并非彻底无用——`SettleAsync` 的**非幂等**路径仍会用它，
只是翻牌永远发生在结算之后，所以那条路在正常时序下到不了。
一个「只在不该发生的时序下才起作用」的参数，读代码的人会以为它决定着价钱。

**所以要写明**：在翻牌这一侧它只在「翻牌先于结算到达」时才有影响，
而协议时序不产生那种情况；价钱由结算时定死的那一个决定。

**同时进 TASK-09 的读数对照**，归入「本服务器决定 / 残留」一栏：
一个签名允许但时序排除的入参，与上一个重构留下的
「`MoveAsync` 的签名允许传入不匹配的角色与本局」是同一类残留。

## Independent Quick Review — QR-07-1

- Verdict: **passed**
- Reviewed contract revision: 1，含 `Contract Amendment 1`，以及 `Controller Disposition — round 1`
  的两条裁定与 `Controller Disposition — 惰性参数` 一条
- Owned diff identity: 工作区未提交，基线 `c0cb6c99`；本轮所核产品文件的 diff 摘要
  `sha256:f66b6e1e...`（`IAccountMoneyService.cs`、`AccountMoneyService.cs`、`InventoryTables.cs`、
  `InventoryStateTables.cs`、`Game/GameProtocol.cs`、`Game/CardReward/`、`DungeonClearSlice.cs`、
  `IDungeonRunDomainService.cs`、`DungeonRunDomainService.cs`、`ICardRewardDomainService.cs`、
  `CardRewardDomainService.cs` 及 `Server/tests/` 下相关判据）
- 评审者自跑的证据（不采信执行记录中的任何转述）：

| 检查 | 命令 | 结果 |
|---|---|---|
| CK-S 全量 | `PVF_ARCHIVE_PATH=.../Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | **1901 通过 / 0 失败 / 2 跳过**（Networking 51、Protocol 479、Pvf 60、Persistence 248、Application 855、Host 4、Infrastructure 204+2 跳过） |
| 跳过的两条是否因档案在而跳过 | 同上输出 | 是：`PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` 与 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`——两条都是「档案缺失时应报错」的用例，档案在才跳过 |
| CK-016 | `dotnet test Server/86JP.Server.slnx --nologo --filter AccountMoney` | 0 失败 / 3 通过 |
| 行尾（numstat） | `diff <(git diff --numstat) <(git diff --numstat --ignore-cr-at-eol)` | 无输出，逐行一致 |
| 行尾（按字节） | 脚本遍历 `git status --porcelain -uall` 的 72 个文件，比 CRLF 字节数对 LF 字节数 | 仅 1 个含 CRLF：`TASK-02-card-face-rule-service.md`（777 / 1327），本 Task 未触碰；其余 71 个 CRLF 计数为 0。**未使用会退化成空模式的 `grep -c` 写法** |

### 一、余额不足是否真的失败且不改变余额 —— 成立

自读 SQL（`InventoryStateTables.cs:113-121`）：

```sql
UPDATE account_cargo_state SET
    value32 = value32 - @amount,
    updated_at = CURRENT_TIMESTAMP
WHERE account_id = @accountId AND value32 >= @amount
RETURNING value32;
```

条件与减法同为一条语句，SQLite 单语句原子。付不起时无行匹配，**没有任何一个字节被写**——
不是「先写后回滚」，是根本没进入 SET。零行返回后 `TryTakeValue32Async` 答
`amount == 0 ? 0 : null`（`InventoryStateTables.cs:129`），无钱包行的账号只扣得起零，语义正确。

判据的对照确在**同一个钱包**上：`SqliteAccountCargoStateTableTests`
的 `An_account_short_of_the_price_keeps_every_coin_where_adding_would_empty_it`（同文件 :191-208）
在同一个 `table`、同一个 `Account`、同一条测试里依次做完三件事——存 100 →
`TryTakeValue32Async(101)` 断言为 `null` → **回读该行断言仍是 100** →
`AddValue32Async(-101)` 断言答 0。「扣款被拒余额不变」与「加负数被夹到 0」
不是两条各说各话的判据，是同一副钱包上的两句相邻断言。

附带核了负数守卫的必要性：SQL 的 `value32 >= @amount` 对负 `@amount` 恒真，减去负数就是发钱，
所以 `AccountMoneyService.TrySpendAsync` 的 `ArgumentOutOfRangeException.ThrowIfNegative`
不是装饰，是这条 SQL 形状必需的对手。它由 `A_charge_below_zero_is_refused_rather_than_paid_out` 钉住。

### 二、公布价与实收价 —— 缺陷已修，且是在同一次 compare-and-swap 里

- **价钱与牌面同时定死**：`DungeonRunDomainService.FixClearRewardCards` 把 `paidFlipCost`
  放进 `new ClearRewardCardSet(free, paid, Empty, paidFlipCost)`，与两张牌面同在一个 `with`、
  同在一次 `_runs.TryUpdate(characterId, carded, current)` 里落地。不存在「有牌面没价钱」的中间态。
  `ClearRewardCardSet` 第四参数**无默认值**，所以任何构造点都被编译器逼着表态——这一条比注释管用。
- **幂等路径不再问规则服务**：`CardRewardDomainService.SettleAsync` 的
  `if (run.ClearRewardCards is { } already)` 分支（:40-44）直接
  `return new ClearRewardSettlementResult(already, already.PaidFlipCost)`，
  而 `cardRules.PaidFlipCost(...)` 在 :50，**在这个 return 之后**。次序确已颠倒。
- **翻牌路径必然走幂等分支**：`SelectCardCommandHandler` 在 :171 已经确认
  `run.ClearRewardCards is not { } fixedCards` 不成立才继续，`ChargeAsync`（:261）才发起
  `SettleAsync`。牌未定则先被 `NoCardsFixed` 拒掉，收费根本到不了。
- 答案里的价从 `fixedCards.PaidFlipCost` 读回而非局部变量 `cost`（:59-61），
  并发结算的输方报的是赢方的价配赢方的牌。这一处是执行者自己加的，方向正确。

### 三、那条判据是否真钉住了性质，而不是某个固定数 —— 钉住的是性质

`PaidCardFlipCostTests` 的 `A_clear_that_levelled_the_character_still_charges_what_it_announced`
（:145-175）通篇**没有任何价钱字面值**（对该文件搜 `10180`／`10300`／`5150` 零命中）：

- `announced = Price(provider, LevelBeforeTheAward)`、`afterTheAward = Price(provider, Level)`
  都是当场从真档案读出来的；
- `Assert.NotEqual(announced, afterTheAward)` 先钉住两者确实不同——档案哪天把两级定成同价，
  这条会当场红而不是静默退化成废话；
- `world.Announced` 来自 fixture 走**真** `SettleAsync` 的返回值，不是测试自己写下来再和自己比；
- 账号按 `announced + afterTheAward` 充值，两个价都付得起，所以「重算」实现下失败的是
  **收了多少**这条断言，而不是「余额不足被拒」这个副作用。失败会指着缺陷本身。

**对报告中变异输出的独立佐证**：`PvfClearRewardRateSourceTests` 钉住
`table.GoldCardCost(level) == 100 + ((level - 1) * 120)` 且 `GoldCardCost(86) == 10300`。
按此，85 级 = 10180、86 级 = 10300。执行记录里那句
`Expected: 10180 / Actual: 10300` 因此**恰好**是「公布价（85 级）对实收价（86 级）」，
且两个数都能由现有判据独立算出——这与「凭空写下的输出」在形状上可区分。
（`5150` 同理：它是 `CardRewardDomainServiceTests.RecordingRules.Price = 5_150` 这个 fixture 常量，
不是档案读数，其文档也如实写明了。）

三层覆盖属实：seam 级 `A_clear_asked_again_after_a_level_up_keeps_the_price_it_settled_at`
另断言 `rules.Priced == [Level]`（规则服务只被问过一次），run 级
`Cards_are_fixed_once_and_a_second_fix_leaves_the_first_pair_standing` 带
`Assert.NotEqual(Price, OtherPrice)` 防止断言变废话。

### 四、常量上提是否干净 —— 干净

- `GameProtocol.cs:45` `public const int ClearRewardCardGold = 1;`，remarks 第一段与
  同文件 `SecretShopNpcId`（:14）句式一致（「Two slices name it: … because a slice may not reach
  into another」），并用 `<see cref>` 指回先例。
- 全仓 `ClearRewardCardGold` 只有 `GameProtocol.cs:45` 一处定义，其余 14 处均为引用；
  `PerFlippedCard` 零命中。**两个名字都搜过**，未把一次阴性当成不存在。
- `DungeonClearSlice.cs` 的 using 块在整个分支 diff 里**没有任何 hunk**（该文件第一个 hunk 起于 :66），
  即它与基线逐字节相同——round 1 加的 `using Dfo.Server.Protocol.Game.CardReward;` 确已删净。
- `Game/CardReward/` 下搜行首 `using Dfo.Server.Protocol.Game.` 零命中。
- 因此 `AGENTS.md:233`「切片内不跨协议引用，要共用就上提到 endpoint 层」在本轮之后**成立**：
  本 Task 的改动不引入任何跨切片引用。`DungeonClear/` 残留的 `MonsterDeath`、
  `StateSynchronization`、`TownProjection` 三处是基线原有、本 Task 未触碰的消息类型引用
  （上一个重构已记为已知偏差）。

### 五、免费卡金币字段 —— 「到不了」成立

标记字节 `FreeFlipPositions`（:343-344）、物品 `ItemIdAt`（:289 / :438-439）、
金币 `FreeCardGoldAt`（:379-380）三者共用同一个 `PositionOf(cards, FreeCardPosition)`
（:425-435，按 `slot.Index` 匹配而非按列表下标）。金币非零 ⇒ `PositionOf` 非 null ⇒
物品字段取该物品的 `ItemId`、标记为 2。**是结构上到不了，不是碰巧到不了。**

`Gold_behind_the_free_position_never_arrives_without_an_item` 是 `[Theory]` 加 4 条 `InlineData`
（:457-481），**双向**都断言：金币非零则要求物品非零且标记 2，金币为零则要求物品为零且标记 1。
0 号位（唯一影响该不变量的位置）在有／无两种状态下各被覆盖，且各配一种不同的邻位摆法。

执行者自己记下的那处残留（若某物品的档案 id 本身为 0，物品字段写 0 而金币仍为 1）
是如实披露而非掩盖，且属内容侧而非本帧判断，不构成本 Task 缺陷。

### 六、`Transactional` —— 成立

`ProtocolModule.cs:197-198` 以 `CommandExecutionKind.Transactional` 注册
`SelectCardCommand`，与同处 `PickUpGroundDropCommand` 一致，符合 `AGENTS.md`
「能触达持久写的命令一律 Transactional」。`SelectCardHandler` 只做四类动作
（解身份、只读查 `runtimeSessions`、发一条命令、发送与记日志），无嵌套 dispatch。

### 七、漏网断言 —— 扫过，未发现无判据支撑的对外断言

逐条追了本 Task 新写的、指向本文件之外的可验证断言，含全称否定：

| 断言 | 出处 | 钉住它的判据 |
|---|---|---|
| 「归档说翻牌不出金币，也没有把金币命中折算成金额的段落」（全称否定） | `GameProtocol.cs` remarks | `PvfClearRewardRateSourceTests.Seven_of_this_files_sixteen_sections_are_shared_and_nine_are_declared_nowhere_else`：`Assert.Equal(16, own.Count)` 加**逐个具名**的两组段落清单，该文件的段落全集被钉死 |
| 「两个 drop-kind 槽是满值与零」 | 同上 | 同文件 `Assert.Equal([10000, 0], table.DropKindRates)` |
| 「加款会夹在零并答一个总数」 | `IAccountMoneyService` / `InventoryTables` / `InventoryStateTables` remarks | `An_account_short_of_the_price_keeps_every_coin_where_adding_would_empty_it` 的第三句断言 |
| 「结算取的是发经验**前**的等级」 | `CardRewardDomainService` 幂等分支注释、`PaidCardFlipCostTests.LevelBeforeTheAward` 文档 | 既有 `SetPlayResultHandlerTests.TheCardsAreFixedAtTheLevelTheRunWasClearedAt`：升级的一局里断言 `settlement.CharacterLevel == 3` |
| 「无定价的 clear 在帧上写该字段能承载的最大值」 | `SelectCardStatus.NotPriced` remarks | `DungeonClearCodecTests:580` `Assert.Equal(int.MaxValue, UnpricedPaidFlipCost)` 及 :586-590 |
| 「付费翻牌不问规则服务」 | `ChargeAsync` remarks | `CardRewardTests.A_paid_flip_asks_the_rules_nothing_because_its_price_was_already_fixed`（`RuleQuestions == 0` **且** `GoldCharged == FixturePrice`，后半句挡住「压根没收费」也满足前半句） |

另核：`RefusingRules` 三个成员确已全部恢复为抛出（`CardRewardTests.cs:1157-1178`），
既有用例 `Turning_a_card_over_never_asks_the_rules_anything` 及其注释一字未动，
上一轮记下的那处张力确已消解。`The_free_positions_second_field_is_gold_and_not_the_items_count`
按 Amendment 1 改期望值的同时补了 `Assert.NotEqual(9, ...)`，原意未削弱反而更强。

### 八、能否收工 —— 可以

密封契约 5 条验收、Amendment 1、以及三条控制器裁定逐条核过，均成立：

| 要求 | 核验结论 |
|---|---|
| 验收 1（扣款不足失败且不改余额，不得沿用加款） | 成立，见第一节 |
| 验收 2（固定金币值单独具名并写明是本服务器决定） | 成立，`GameProtocol.cs:45` 唯一定义，由 `The_card_gold_this_server_pays_is_one_and_is_this_servers_own_decision` 钉值 |
| 验收 3（按归档成本表取价，先收费后发奖） | 成立，次序为查未认领 → 收费 → 认领 → 发金币 → 进包；认领失败退款由 `A_paid_flip_that_lost_its_position_is_given_its_money_back` 钉住 |
| 验收 4（不足者被拒，金币与背包均未变） | 成立，`An_account_a_coin_short_of_the_archives_price_is_refused_and_keeps_it` 回读 SQLite 行 |
| 验收 5（不低于 1690 通过 / 0 失败 / 2 跳过） | 成立，评审者自跑 1901 / 0 / 2 |
| Amendment 1（免费卡金币字段与实付一致） | 成立，且不是取 0；见第五节 |
| 裁定一（常量上提到端点层，照 `SecretShopNpcId`，原处无第二份，跨切片 using 删除） | 全部成立，见第四节 |
| 裁定二（价钱与牌面同时定死；判据须在「重算」下变红） | 成立，见第二、三节 |
| 裁定三（`CharacterLevel` 保留但在代码里点明翻牌侧惰性） | 成立：`SelectCardCommand.cs:101-116` 明写「It does not price this flip, and that is the point」，并写明它只在「牌未定」的时序下才到达规则服务、而那种时序在收费前已被 `NoCardsFixed` 拒掉 |

### 非阻塞观察（不改变 passed 裁决，留给控制器）

1. **裁定三的后半句尚未落地到 TASK-09。** 裁定要求该惰性入参「同时进 TASK-09 的读数对照，
   归入『本服务器决定 / 残留』一栏」，但 `TASK-09` 的 `Contract Amendment 1` 表里没有这一项
   （表中有「每张翻到的牌固定 1 金币」与「`FreeCardGold = 0`」，无 `SelectCardCommand.CharacterLevel`）。
   TASK-07 无权改 TASK-09 的密封契约，所以这不是本 Task 的缺陷；但它正是 TASK-09
   那条 Amendment 写下来要防的「靠人记得」形状。建议控制器补进 TASK-09 的具名清单。
2. **TASK-09 表里那一行已被本 Task 消解。** `FreeCardGold = 0`（TASK-05 占位）一行自注
   「TASK-07 的密封条款要求填它，届时须核本条是否已消解」——本轮已填为
   `GameProtocol.ClearRewardCardGold`，TASK-09 核对时应据此结案而非照抄。
3. **一句轻微过头的自述。** `Gold_behind_the_free_position_never_arrives_without_an_item`
   的 XML remarks 写「asserts it over every arrangement of a face」，实际是四个位置 16 种组合中
   选取的 4 种。不变量本身被双向覆盖、0 号位两种状态都在，性质没漏；只是这句自述略宽于事实。
   本 Task 文件里的表述（「跑遍四个位置的四种有无组合」）反而是准确的。
4. `ClearRewardSettlementResult` 同时持有 `PaidFlipCost` 与 `Cards.PaidFlipCost`。
   只有一处构造、后者永远是前者来源，构造不出分歧，执行者已如实记下并在文档里写明二者恒等。
   属形状冗余，不属缺陷。

**结论：`passed`。** 本轮未发现可在给定输入内证实的阻塞缺陷。
按规程，这只表示本 Task Unit 可以走执行者的接受逻辑，不构成最终质量批准。
