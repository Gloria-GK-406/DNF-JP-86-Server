---
kind: rks-review
verdict: approved
---

# 独立评审：角色自己回答自己的数

两轮。第一轮 `changes_required`，两条要点：B-1（RQ-003/C-2 只做到三个数里的一个，且收窄未经批准）与 O-1（四条发经验帧的路径里三条零判据，我用变异实证了击杀路径可以被完整回退而全量不动声色）。第二轮复核整改。**本轮结论：approved**，附一条必须在交付汇报前改掉的记载错误（见「必须改掉的一处」）。

---

## 第二轮：我自己做了什么

**判据（我自己跑的）**

| 项 | 结果 |
|---|---|
| `dotnet build Server/86JP.Server.slnx --nologo` | 0 警告 0 错误 |
| `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | **2024 通过 / 2 跳过 / 0 失败**（Protocol 498、Networking 52、Pvf 60、Persistence 255、Host 6、Infrastructure 223+2skip、Application 930） |

增量落点我自己对过：Protocol.Tests 489 → **498（+9）**，Application.Tests **930 不变**。三个新文件各 3 条，增量全部落在 Protocol.Tests，没有一条落在别处 —— 与「只补判据、不动别的」一致。相对真基线 `68a12119` 的 2007，总增量 +17。

**`Server/src` 零改动（我自己验的，不是采信）**：`git diff -M 68a12119 -- Server/src/` 的输出与我第一轮保存的副本 **`diff -q` 逐字节相同**；三个未跟踪的 src 新文件 md5 与我还原后的一致（`ICharacterSkillPointDomainService.cs` `d825a84b…`、`CharacterSkillPages.cs` `67e777b0…`、`CharacterSkillPointDomainService.cs` `cfe876fe…`）。`CommandHandlerArchitectureTests.cs` 的 diff 仍为空。

**变异（我自己重做了全部四条，每条备份→改坏→跑→还原）**

| 变异（把该切片的取数完整回退到 `…Skills.ProtocolMirror`，并删掉升级后的第二次询问） | 判据反应 |
|---|---|
| `MonsterDeathHandler` | **失败 2 / 通过 1** |
| `FinishQuestHandler` | **失败 2 / 通过 1** |
| `TutorialLevelUpHandler` | **失败 2 / 通过 1** |
| `SetPlayResultHandler`（副本结算） | **失败 3 / 通过 15**，主用例以 `TimesAsked` 期望 2 实得 **0** 变红 —— **是值不对，不是空引用**，观察 3 已闭合 |

第一轮那次「击杀路径整段回退而全量全绿」的变异，现在会红。**O-1 关闭。**

顺带一个我没料到的发现：这个仓库把 CS9113（主构造器参数未读）当**错误**。我试着把某个切片的 `characters` 参数彻底废掉时构建直接失败 —— 所以「加了接口只挂着不接线」这类半成品在这个仓库里连编译都过不去。这是一道我第一轮没算进去的结构性保护。

还原后我重跑了全量：**2024 / 2 / 0**，`git status --short` 与整改后状态逐行相同。

---

## 第二轮：整改内容的独立核查

### 三条新判据的设计我认可，而且它比我要求的更强

`Server/tests/Dfo.Server.Protocol.Tests/Game/ExperienceFrameStandIns.cs` 把四个切片共用的替身收在一处。关键设计是 `SkillPointLedger.StaleEntryMirror = (200,201,202,203)`：**账本永远不会给出这个值**，而 `ExperienceFrameEntry.At(...)` 把它填进会话的 `Skills.ProtocolMirror`。于是一个退回读会话的切片报出的是「看起来合理但错」的数字，而不是 null —— 这正是我第一轮观察 3 指出的弱点（副本结算原先靠 `NullReferenceException` 抓住回退，那是「抓对了但理由是错的」）。四次变异全部以值失败，不是以异常失败。

`FinishQuestHandlerTests` 与 `TutorialLevelUpHandlerTests` 还各断言了一条我没要求的东西：`LedgerAsksWhenTheCompletionRan == 0` / `LedgerAsksWhenTheGrantRan == 0` —— 即命令执行的那一刻账本**还没被问过**。这钉死了「问在写之后」的时序，而时序正是「新等级是否被反映」的全部前提。

### `SetPlayResultHandlerTests` 的 fixture 改动：我逐字复验过，断言零改动

这是整改动到的唯一一处既有 fixture，控制者点名要我自己验。我把基线 `68a12119` 的 `Entry()` 与 `ExperienceFrameEntry.At(3, StartingExperience)` 逐成员比对：

| 成员 | 基线 | 现在 | 判定 |
|---|---|---|---|
| `Character.Progression` | `new CharacterProgressionState(3, StartingExperience)` | `new CharacterProgressionState(level, experience)`，实参 `(3, StartingExperience)` | 逐字等价 |
| `CharacterEntry.Public` | 九个字段 + `CharacterExperience: StartingExperience` | 同，`CharacterExperience: experience` | 逐字等价 |
| `World.Subject` | `new WorldContentSubject(3, Job: 0, GrowType: 0, …)` | 同，首参 `level` | 逐字等价 |
| `CharacterEntry.Skills` | `SkillPointBasis: null!`，`ProtocolMirror: (11,22,33,44)` | `SkillPointBasis: (0,0)`，`ProtocolMirror: (200,201,202,203)` | **唯一差异** |

`Skills` 是这个 handler 已经不再读的成员（mirror 现在来自角色），也没有任何断言碰它。整个文件的 diff 我读完了：**没有一条 `Assert` 被改动**，改的只有一处方法改名（`WithoutASkillMirror…` → `WithoutASkillLedger…`）、一个参数改名（`withMirror` → `withLedger`）、四个嵌套替身移出到共享文件（`RecordingSender` 的异常文案改了措辞，无人断言）。控制者的说法属实。

### `NoCarriedGold.cs` 的悬空 `<see cref>` 已修

`<see cref="NoCharacterRow"/>` → `<see cref="CharacterRow.None"/>`，指向真实存在的成员。**O-2 关闭。**

---

## 第二轮：对 B-1 收窄的裁定

控制者把 C-2 与 RQ-003 重述到「凡在**入场之后会变陈**的角色自身数值」，契约升 revision 2、spec 同步 pin 到 revision 2，修订理由与「这是一次控制者做出的收窄」写进契约正文，并承诺在交付汇报里向所有者明说。

### 裁定：**收窄成立**，但它的一条依据是错的

**我自己核了实质依据，它站得住 —— 而且比控制者说的还稳一点。**

「四条能改变等级的路径全都把新等级写回会话」这句，我逐处核过：`MonsterDeathSlice`（`Progression = new CharacterProgressionState(level, total)` 后 `ReplaceExact`）、`DungeonClearSlice`（同形）、`FinishQuestHandler`（`new CharacterProgressionState(outcome.NewLevel, outcome.NewExperience)` 后 `ReplaceExact`）、`TutorialLevelUpSlice`（`new CharacterProgressionState(granted.Level, granted.Experience)` 后 `ReplaceExact`）。四处属实。

我另外去找了**控制者没提到的第五条写库路径**：`DungeonEffectRecoveryService.ApplyExperienceAsync` 直接调 `characters.SetProgressAsync(characterId, split.NewLevel, …)`，且完全不碰任何会话。这本可以戳穿那句话 —— 但该服务自己的文档说它「**Applies what a dungeon left behind for a character, before that character is entered**」，全仓唯一注册点是入场前的恢复通路，那时根本没有已发布的会话副本会变陈。所以结论不变：**在会话存续期间，等级只由那四条路径改动，而它们都维护会话副本。**

于是 RQ-003 原本要消灭的那类缺陷 —— 一个没人维护的数 —— 只存在于技能点镜像一处。把等级的读取点也一并重指，是给热路径各加一次数据库读去取一个本来就正确的值；教学升级那一处更是写入命令的判断入参，改它确实有带出第二处可观察变化的风险，而 Spec 明令只允许一处。**这是一次有实质依据的收窄，不是给未完成的工作找理由。**

治理动作也做对了：契约与 spec 同步升版、理由写进契约本身而不是埋在证据里、明写「控制者做出的收窄」并承诺上报所有者。这正是我第一轮开出的处置 (b)，而且是按流程做的，不是让评审替所有者追认。

Spec 的 `Why and Outcome` 也一并重写了 —— 它不再宣称「这一轮是为了不再有『同一个数有两处可读』」，改成「等级和经验没有这个毛病……一并让角色实例能回答，是为了让『角色自己的数问角色』这条路存在，而不是因为它们也错了」。**这很关键**：第一轮我判 B-1 阻断，一半的理由就是产物承诺了代码没做到的事。现在产物不再承诺它。`AGENTS.md` 那段「现在做到了哪一步，如实说」未改动，与新口径仍然一致。

### 必须改掉的一处：`DungeonClearSlice` 那条「照字面改就是错的」依据，**不成立**

控制者要我自己去核 `DungeonClearSlice.cs:614-626`，别采信转述。我核了，**转述的结论是错的**。

那段代码是：

```
// 行 620-625
// The level handed in is the one the character cleared at, taken before the award below
// can move it: ...
var settled = await cardRewards.SettleAsync(
    new ClearRewardSettlement(session.CharacterId, result.Character.Progression.Level),
    cancellationToken).ConfigureAwait(false);
```

而授予在**同一个方法的下方、第 677 行左右**：

```
var paid = (await commandBus.SendAsync(
    new GrantClearExperienceCommand(...), cancellationToken).ConfigureAwait(false));
```

两者是同一个方法里顺序的 `await`，翻牌结算严格在授予**之前**。所以此刻数据库里的等级**就是**授予前的等级 —— 把这一行改成 `await character.ProgressAsync(...)` 取回的仍然是通关时的等级，卡面档次不会变。那条注释说的「taken before the award below can move it」是关于**方法内的先后**，不是关于**读哪个存储**。

因此「改成向角色实例读库，取到的会是授予**之后**的等级」这句是假的，「改了反而是错的」这个论断在这一处不成立。

**这不影响裁定**：收窄的成立靠的是上面那三条（没有无人维护的值、热路径白加读、教学那处是写入入参的风险），它们各自独立成立，而且核心那条是我自己验的。但契约的修订依据里现在有一句加粗的、事实上错误的论断，而所有者要据这份依据决定是否接受一次范围缩减 —— **一份比代码强的记载，正是本 Spec 的 C-6/RQ-007 所禁止的东西，只不过这次出现在契约里而不是 `AGENTS.md` 里。**

**要求**：交付汇报之前，把契约里「**其中有一处，改了反而是错的**」那一整段删掉或改写为它真实的样子（可以说「那一处的注释要求的是方法内的取数时机而非存储来源，因此它不构成反例；收窄的依据是前三条」）。零代码改动，一次编辑即可。我不为此挂 `changes_required`，因为它不影响代码、不影响判据，而且控制者已经明确承诺这段会进交付汇报 —— 但它必须在进汇报之前改对。

---

## Success Conditions 逐条判定（按 revision 2）

| ID | 判定 | 我的证据 |
|---|---|---|
| C-1 | **满足** | `ICharacter.cs:53,68` 两个新成员；`PlayingCharacter` 与 `CharacterSkillPointDomainService` 均无字段，实例每次由 `CharacterDomainService.Get` 新建；三个输入分别追到 `FindStandingAsync`(`… AND delete_flag=0`)、`ReadEntryAsync`(`… AND mode='pve'`，与入场同一方法)、`FindBonusPointsAsync`(与入场 `CharacterEntryAssembly.cs:263` 同一方法、同一 `?? (0,0)` 缺省)；变异「冻结等级」与「改读决斗页」各自让新用例变红 |
| C-2 | **满足** | `ProtocolMirror` 生产读取点只剩 2 处，均在入场序列内，且 `SkillStateMessage` 全仓唯一构造点是 `SelectedCharacterStateMessageFactory.cs:60`（入场）；`CanUseAsync` 已改为经 `ProgressAsync` 读行；仍读会话等级的 9 处，我逐处核过它们为何不陈（四条改动路径全部写回会话；第五条写库路径 `DungeonEffectRecoveryService` 运行在入场之前，无会话可陈） |
| C-3 | **满足** | `RemainingSkillPointTests` 与四个切片各自的「报出的是授予之后那次的答案」用例；我自己的六次变异（两轮合计十次）证明它们会红 |
| C-4 | **满足** | 全量 2024/2/0；两轮测试 diff 我逐文件读完，**没有一条既有断言被改写**；唯一动到的既有 fixture（`SetPlayResultHandlerTests.Entry`）我逐成员复验，差异只在该 handler 已不再读的 `Skills` 成员上 |
| C-5 | **满足** | `CommandHandlerArchitectureTests.cs` 两轮 diff 均为空；`IsDomainShape` 仍是「Abstractions 程序集 + `DomainService`/`DomainStore` 后缀」两半，`Exempt` 仍只有 `TimeProvider`；反例锚点 `ISelectedSessionRuntimeDirectory` 未动且仍被拒；`ICharacterSkillPointDomainService` 靠合规命名与位置通过 |
| C-6 | **满足** | `AGENTS.md` 新增一节，我逐句核过对应代码事实，无一句强于代码 |

## Observable Requirements 逐条判定（按 revision 2）

| ID | 判定 |
|---|---|
| RQ-001 | **满足** |
| RQ-002 | **满足** —— `RemainingPoints` 全仓唯一实现，两个供料方；CK-003 我自己做过：基线块套三处必然的供料替换后与新块规范化比对**逐行零差异**，新增仅三行 `ArgumentNullException.ThrowIfNull`；入场端 `CharacterEntryInventoryTests.cs:474` 断言 `(85,105,0,0)` 仍绿，是搬移未改数的端到端佐证 |
| RQ-003 | **满足**（按 revision 2 重述；重述本身我判定成立，附「必须改掉的一处」） |
| RQ-004 | **满足** —— 四条路径**各有**可证伪判据，我自己四次变异全部变红 |
| RQ-005 | **满足** —— 无新增帧、无字节布局改动（所有 codec/encoder 两轮均未被触碰）；`CanUseAsync` 的取数位移见下方观察 |
| RQ-006 | **满足** |
| RQ-007 | **满足** |

## 边界决定裁定：入场序列的帧仍可读入场镜像 —— **成立**（第一轮已裁定，本轮不变）

理由是我自己追出来的三条：(1) 两处保留读取点在结构上不可能在入场之后被发出，镜像与帧同源同刻；(2) 反过来收紧会**制造**缺陷 —— 自由决斗频道入场时 `MergePvpProfile` 把镜像换成 `(65535×4)`，那是「决斗频道不限点数」的意思，而新的领域服务只会答 PVE 账本；(3) 该分支今天不可达（`ChannelEntryOptions.Legacy` 的 `FreeDuelEnabled: false`），执行者已在实现上写下 remark 留待日后。

## 范围核查（Spec Non-Goals）—— 一条未碰

坐标/在场/世界内容/背包投影/账号侧状态、HP-MP 随等级成长（`character_stats` 未动）、新增协议帧或改字节布局、投影器常态化、`IEntryInventoryLeaseRegistry` —— 逐条核过 diff，全部未触及。

---

## 遗留的非阻断观察

（O-1、O-2 已在第二轮关闭。）

- **O-3 plan 未随 revision 2 同步。** `plan.md` 的 CK-011 仍写「镜像与会话进度字段的读取点枚举 / 除入场序列外无残留」，与重述后的 RQ-003 口径不一致，frontmatter 也没有 revision pin。契约与 spec 是治理 artifact，plan 落后不影响验收，但建议一并对齐，免得下一轮读者拿旧口径去量。
- **O-4 `CanUseAsync` 是被授权范围内的第二处取数位移。** 它落在 RQ-003 的字面范围内（角色实例内部不再从镜像读自己的等级），我不算它夹带；但它确实改变了道具等级门槛的真源。两处取值在生产中一致，替身刻意把它们分开，变异「改回读会话等级」证明判据能抓。
- **O-5 同一 `level` 列，两个新读取点越界策略不同。** `ProgressAsync` 用 `checked((byte)Math.Clamp(progress.Level, 1, byte.MaxValue))`（夹取，下界 1，等级 0 会被答成 1），`RemainingAsync` 用 `checked((byte)standing.Level)`（溢出抛异常）。建议统一。
- **O-6 `FindProgressAsync` 没有 `delete_flag=0`，与新加的 `FindStandingAsync` 不一致。** `FindStandingAsync` 的注释写着「as every other read that answers "what is this character" leaves them out」，而同文件的 `FindProgressAsync`（基线既有）恰恰不过滤 —— 这段注释比代码强。今天不可达。
- **O-7 热路径的真实代价只讲了一半。** 缓存放在 `PvfSkillPointDefinitionSource`（内容源层）是放对了层 —— 键覆盖全部四个入参、已学技能 `Distinct().Order()` 规范化、`ConcurrentDictionary`、答案两个集合成员都是只读接口且无调用方改写，因此实例共享安全；上面两层不缓存，「问的时候才算」的契约没被破，变异「缓存键去掉等级」证明缓存判据会红。但缓存挡的是归档解析，挡不住数据库：`RemainingAsync` 每次击杀都被**无条件**调用一次（不只是升级时），基线是 0 次三读。证据文档只谈了归档那一半。
- **O-8 缓存不驱逐的增长描述略乐观。** 类文档说它「grows as characters learn skills and stops when they stop」，而键里含等级，所以每次升级也会新增一条。
- **O-9 `SetPlayResultHandler` 的拒绝分支从不可达变成可达。** 换取数来源必然带来的缺席条件变化，新注释已诚实写下；但跑本途中角色行消失会让这一次清算既不付也不报，而 `SET_PLAY_RESULT` 一局只来一次。
- **O-10 新服务少了入场端的一处校验。** 入场 `CharacterEntryAssembly.cs:265-268` 会在 `bonuses.BonusSp < 0 || bonuses.BonusTp < 0` 时抛 `InvalidDataException`，新服务没有这道检查。

---

## 结论

整改把第一轮两条要点都做实了。O-1 那个缺口不但补上，补法比我要求的更强：`StaleEntryMirror` 让每一次回退都以**错值**而不是空引用变红，我自己重做的四次变异全部按预期失败，第一轮那次「击杀路径整段回退而全量全绿」的情形不复存在；`SetPlayResultHandlerTests` 唯一动到的既有 fixture 我逐成员复验过，断言零改动。`Server/src` 与注入面判据两轮逐字节未变，全量 2024 / 2 / 0，增量 +9 全部落在三个新判据文件里。

B-1 的处理方式是对的：不是让评审追认，而是把契约与 spec 一起升到 revision 2、把「这是控制者做出的收窄」写进契约正文、并承诺上报所有者；Spec 的 `Why` 也重写掉了那句代码没做到的承诺。收窄的实质依据我自己核过并且核得比控制者更远一步（那条没被提到的第五条写库路径运行在入场之前，不构成反例），**依据成立**。

唯一没能通过的是那条被加粗当作决定性反例的论断：`DungeonClearSlice` 的翻牌结算严格排在授予之前，在那里读库取到的仍是通关时的等级，所以「改了反而是错的」在这一处不成立。它不影响裁定，因为收窄不靠它成立；但它是所有者据以批准范围缩减的依据，而一份比代码强的记载正是这个 Spec 自己禁止的东西。**交付汇报前把那一段改掉**，这一批就完整了。verdict: approved。
