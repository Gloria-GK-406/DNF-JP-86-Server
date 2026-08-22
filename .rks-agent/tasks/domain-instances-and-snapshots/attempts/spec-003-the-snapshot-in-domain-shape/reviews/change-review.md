---
kind: rks-review
verdict: approved
---

# Change Review: The snapshot in domain shape

评审范围：基线 `078d762`，工作树 `D:/project/DNF/DNF-JP-86-Server/.worktrees/domain-instances-and-snapshots`
的全部未提交改动（17 个已修改文件 + 1 个未跟踪的 attempt 目录）。
判据来源：子契约 `TaskContract.md`（revision 1）、`spec.md`、`plan.md`、根契约 revision 2、
`Docs/Refactor/DomainInstanceDesign.md`。执行者的自查结论一律未采信，逐项独立复核。

**verdict: changes_required** —— 一条 A 级：`RequireProfileInRange` 悄悄丢了四条宽度检查，
使得「超宽的行在装载时被一次性拒绝」这件事对这四个字段不再成立，而本轮新写进设计权威文档的
那一段恰好断言它成立。已用真改真跑双向证明。

## 我实际执行过的独立实验

以下每一项都真跑过；未跑的一律不写在这里。每次改动后都用 `sha256sum -c` 对 17 个文件的
基线摘要逐个校验复原，最终工作树与评审开始时逐字节一致（`git status --porcelain` 只剩
17 个 ` M` 与那个未跟踪目录）。

### E-0 构建与全量（复现执行者的基线）

- `dotnet build Server/86JP.Server.slnx` = **0 警告 0 错误**。
- `dotnet test Server/86JP.Server.slnx -m:1`（`PVF_ARCHIVE_PATH` / `Pvf__ArchivePath` 已设）
  = 60 + 530 + 4 + 100 + 46 + 230 + 203 = **1173 通过 / 2 跳过 / 0 失败**。
  `Dfo.Server.Host.Tests` 未出现假红。与执行者所报一致。

### E-1 golden 数值比对（独立重做，未采信执行者结论）

用脚本把 `078d762:.../EntryLoadCharacterizationTests.cs` 与当前版本的 `Expected` 原始字符串
（`"""` raw literal）整块取出，按行拆成 `key = value`，做两次多重集比对：

- **值多重集：新旧各 215 条，差集两侧皆为空。** 没有任何一个值变了。
  执行者所说的「5 处假差」在按 raw literal 整块解析后根本不出现——那确实是他提取方式的产物。
- **(叶名, 值) 多重集：**仅 9 条差异，且一一配对：
  `ExpertJobType/109 → Type/109`、`ExpertJobExperience/1009 → Experience/1009`、
  `PvpRankPoint/1014 → RankPoint/1014`、`PvpStatA/1022 → StatA/1022`、
  `PvpWinStreak/126 → WinStreak/126`、`PvpLoseStreak/127 → LoseStreak/127`、
  `PvpResults/[] → Results/[]`、`GuildLevel/115 → Level/115`、`GuildPowerWar/1047 → PowerWar/1047`。
  九个改名都是「去掉概念前缀、挂到新概念容器下」，值全部原样。

用 (叶名, 值) 而不是全路径做比对，是为了能抓住「同一概念内两个同型字段互换」——那种情况会同时
产生一条 old-only 和一条 new-only，而实测只有上述 9 条。**结论：golden 的数值一个都没变。**

`git diff -U0` 显示该文件只有一个 hunk（`@@ -188,78 +188,78 @@`），全部落在 `Expected` 字面量内，
断言体与测试方法未动。

另两份整记录表征用例 `AccountEntryLoadCharacterizationTests.cs`、`InventoryLoadCharacterizationTests.cs`
的 `git diff` 为空（`git status --porcelain` 无命中）。

### E-2 78 个字段搬运的逐条核对（脚本化，非肉眼）

写脚本解析：旧 `CharacterSubtype0StorageRecord`(45) / `CharacterSubtype1StorageRecord`(33) 的形参表
与旧 `Subtype0(...)` / `Subtype1(...)` 的实参表按位配对；新 `CharacterConditionStorageRecord`(37) /
`CharacterDerivedStatsStorageRecord`(27) 与新 `Condition(...)` / `DerivedStats(...)` 同样配对；
再按「同名形参的来源表达式叶名」比对。

- Condition 37 条：**0 处不一致**。
- DerivedStats 27 条：**0 处不一致**。
- 剩余 14 条（脚本列出的「未被两个映射方法覆盖」项）在 `LoadProfileAsync` 内联构造，逐条人工核对：
  `Settings(settingsRow.UserStateBits)`；`Pvp(pvpRow.PvpRankPoint, pvpRow.PvpStatA,
  pvpRow.PvpWinStreak, pvpRow.PvpLoseStreak, [])`；`ExpertJob(jobRow.ExpertJobType,
  jobRow.ExpertJobExperience)`；`Guild(row.GuildLevel, row.GuildPowerWar)`；
  以及 `CharacterProfileStorageRecord` 上的 `row.SkillTreeIndex` / `row.NameTagItemId` /
  `row.NameTagExpireTime` / `row.ManageLevel`。全部与旧来源一致（旧 `guildLevel` 形参的调用点
  `078d762` 第 170 行确为 `row.GuildLevel`）。
- 45 + 33 = 78；37 + 27 + 14 = 78。**无遗漏、无重复、无错位。**

第二次搬运（投影 → 编解码器）：`git diff` 显示投影已退化为原样传递，无字段级重排空间；
编解码器三个方法的 `writer.Write(` 计数脚本比对为 **20/20、73/73、57/57**，逐行取值来源已人工
一一对照（见 E-3）。

### E-3 位宽收窄的逐条核对

把旧 `CharacterSubtype0EntryState` / `CharacterSubtype1EntryState` 的**声明类型**当作旧
`writer.Write(x)` 实际选中的重载，与新代码写那一行的显式 `checked((T)…)` 逐条比对，三个方法全覆盖：

- `EncodeSupportStats`（20 处写）：`FireResistance`/`WaterResistance`/`DarkResistance`/`LightResistance`
  旧为 `short` 字段无 cast，新补 `checked((short)…)`；`HpRegenSpeed`/`MpRegenSpeed`/`HitRecovery`/
  `JumpPower` 旧 `ushort`，新补 `checked((ushort)…)`；`Weight` 旧 `uint`，新补 `checked((uint)…)`。
  字节数仍为 82（4+4+2×8+34+4+2+2+4+2+2+2+2+4），与 `writer.Write(82u)` 的声明一致。
- `EncodeUserStateBody`（73 处写）：55 个取值点逐条对齐，含全部 8 处 `!= 0 ? (byte)1 : (byte)0`
  的真假转换方向；`GuildLevel → guild.Level`、`ExpertJobType/Experience → job.Type/Experience`、
  `PvpStatA/WinStreak/LoseStreak/RankPoint → pvp.*` 概念归属正确。
- `EncodeExtendedUserState`（57 处写）：含 `Dimensions` 的 `.OrderBy(value => value.SortOrder)`
  —— 这条排序原在投影里，已随字段一起进入编解码器（`StateSynchronizationPayloadCodec.Entry.cs:782`），
  **没有丢**；`DimensionFlags` 4 处 byte、`PvpResults` 3 处、`CharacterExperience`/
  `ExtraEquipmentSlotState`/`CurrentHpMpPercent`/`EquippedCreatureLevel`/`FlagByte`/
  `ManageLevel`/`NameTag*`/`ServerTimestamp`/`QuestShopCount`/`GuildPowerWar` 全部补齐。

**结论：没有一处收窄丢失，没有一处收窄到错误的宽度。** 唯一形态可疑的是
`StateSynchronizationPayloadCodec.Entry.cs:750` 的 `checked((byte)checked((uint)settings.UserStateBits))`
双重收窄——它精确复制了旧的两段式语义（long→uint 在投影、uint→byte 在编解码器），抛与不抛的结果
与旧完全一致，不是缺陷，但见 B-1。

### E-4 六次真改真跑（我自己挑的位置，与执行者那四处不重合）

| # | 改动 | 期望 | 实测 |
|---|---|---|---|
| M1 | 编解码器 `EncodeExtendedUserState` 里 `checked((uint)guild.PowerWar)` → `checked((uint)stats.ServerTimestamp)`（**读错来源**） | 红 | **全量绿**（见 B-5） |
| M2 | 编解码器 `EncodeExtendedUserState` 里 `checked((ushort)stats.AttackSpeed)` → `checked((uint)…)`（**收窄宽度错**） | 红 | 红：`SelectCharacterAcceptedTranscriptTests.AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline` |
| M3 | 装配器 `Condition(...)` 里 `ChatBanEndTime` 改读 `c.FatiguePenalty`（**读错来源**） | 红 | 红：`EntryLoadCharacterizationTests.ACharacterIsLoadedWithExactlyWhatItWasLoadedWithBefore` |
| M4 | 投影里 `storage.Guild` 换成一个**等值副本** `new CharacterGuildStandingStorageRecord(storage.Guild.Level, storage.Guild.PowerWar)`（**概念被替换**） | 红 | 红：`CharacterEntryStateTests.The_profile_projection_hands_every_concept_through_unchanged` |
| M5 | `RequireProfileInRange` 里 `Stamina` 上界 byte→ushort、`FatiguePenalty` 上界 uint→long（**边界失效**） | 红 | **全量绿**（见 B-4） |
| M6 | 投影 `Extras` 里 `NameTagItemId` 与 `NameTagExpireTime` **对调** | 红 | **全量绿**（见 B-3） |

M1、M5、M6 三次的全量绿都是完整跑了 `dotnet test Server/86JP.Server.slnx -m:1` 七个测试项目后确认的，
不是只跑了相关项目。

### E-5 A-1 的双向证明（临时探针，已删除）

1. 在 `Dfo.Server.Persistence.Tests` 下临时加一个探针用例，向 `characters` 表写入
   `name_tag_item_id = 5000000000`、`name_tag_expire_time = 5000000000`、`manage_level = 300`、
   `guild_power_war = 5000000000`（列均为 SQLite `INTEGER`，可存），断言 `LoadAsync` 抛
   `InvalidDataException`。当前代码下 **四条全部失败**——装载成功了，没有拒绝。
2. 把旧的四条子句临时补回 `RequireProfileInRange`（并把 `row.NameTagItemId` /
   `row.NameTagExpireTime` / `row.ManageLevel` 传进去），同一探针 **四条全部通过**。
3. 撤掉补丁回到当前代码，同一探针 **四条重新全部失败**。
4. 在 `Dfo.Server.Protocol.Tests` 下临时加第二个探针：把 `SelectCharacterPayloadTestData.CreateComplete()`
   的 `Extras.ManageLevel` 改成 300，遍历 `SelectedCharacterStateMessageFactory.Create(result)`
   逐个 `EncodeBody()`，断言抛 `OverflowException` —— **通过**。

即：这四个字段今天的失败形态，正是「装到一半的包里炸」，而不是「一条消息把角色挡在门外」。
两个探针文件均已删除，17 个文件的 sha256 全部 `OK`。

### E-6 边界与编码检查

- `Tool/`、`.worktrees/`：`git status --porcelain` 无命中。
- `Server/src/Dfo.Server.Persistence/`：`git diff --stat` 为空；`CREATE TABLE` 计数当前 44，
  基线 078d762 亦为 44。**持久层形状未动。**
- 14 个 `.cs` 改动文件：全部 CRLF（`crlf 行数 == 总行数`）、无 BOM；唯一例外
  `RosterScreenQueryCodecTests.cs` 为 `efbbbf`，`git show 078d762:` 确认原本就带 BOM；
  全仓带 BOM 的 `.cs` 计数为 10，与执行者所述一致。
- 协议编解码用例期望字节：`SelectCharacterPayloadCodecTests.cs` 的 `git diff` 只改了构造与取值路径，
  没有任何期望字节被改；我把新旧测试数据逐参数对照过（`Condition(1,2,0,0,0,0,0,5,6)` 对应旧的
  `Subtype0(1,2,false,false,1,3,false,false,0,4,5,6,7)` + `Settings(4)` + `Pvp(7,0,0,0,…)` +
  `ExpertJob(1,3)` + `Guild(0,0)`，默认值逐项相同，含 `ReturnUserFlag = 1`），构造出的值与旧完全一致。
- `SelectCharacterAcceptedTranscriptTests.cs`（下发帧 SHA-256 基线，写死的哈希数组）
  **`git diff` 为空且全绿**——这是「下发帧一字节不变」最硬的那条证据。

## 发现

### A 级（必须修）

#### A-1 `RequireProfileInRange` 丢了四条宽度检查，「装载时拒绝」对这四个字段不再成立

**文件与行**：`Server/src/Dfo.Server.Application/Characters/CharacterEntryAssembly.cs:591-640`
（新 `RequireProfileInRange`），对照基线 `git show 078d762:` 同函数第 610-621 行。

基线的第二个 `if` 块里有这四条，新版没有：

    || s1.ManageLevel is < 0 or > byte.MaxValue
    || s1.NameTagItemId is < 0 or > uint.MaxValue
    || s1.NameTagExpireTime is < 0 or > uint.MaxValue
    || s1.GuildPowerWar is < 0 or > uint.MaxValue

`guild.Level` 仍在 byte 清单里（第 612 行），`guild.PowerWar` 不在任何清单里；
`row.NameTagItemId` / `row.NameTagExpireTime` / `row.ManageLevel` 在第 212 行直接进
`CharacterProfileStorageRecord`，途中无任何守卫（已 grep 确认全函数只在 168、212 两行出现）。

**可复现构造方式**：见 E-5。四列都是 `INTEGER NOT NULL`
（`Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs:203`），写入超宽值后：
基线 → `LoadAsync` 抛 `InvalidDataException("Character extended detail state is out of range.")`；
当前 → `LoadAsync` 成功返回，随后
`StateSynchronizationPayloadCodec.Entry.cs:768/775/787` 与
`SelectCharacterPreparationService.cs:217` 的 `checked` 在组帧途中抛 `OverflowException`。

**分级理由**（三条叠加，任一单独我都只会给 B）：

1. **违反子契约 C-1**：「位宽收窄的行为逐条保留：今天会因超宽而抛的，改后仍在同一处抛。」
   本轮把收窄整体搬到编解码器，「同一处」对被搬走的收窄自然不能字面成立——但这四条不是收窄，
   是**装载时的拒绝**，执行者自己也把它归为「拒绝」而非「转换」并声明保留。声明保留而实际删掉，
   是两回事。
2. **使本轮新写的设计权威文档出现事实错误**。`Docs/Refactor/DomainInstanceDesign.md`
   新增节「Settled by the snapshot-side implementation」第二段写道：
   *"A value too wide for its field is still refused at load, in `CharacterEntryAssembly`,
   and that stayed put on purpose: a character whose stored state cannot be sent is turned away
   with one message instead of failing halfway through a packet."*
   E-5 第 4 步实测：`ManageLevel = 300` 恰恰就是 *failing halfway through a packet*。
   根 SC-10「设计文档与落地一致」在这一句上不成立。
3. **执行者交给评审的自查陈述与代码不符**：交办说明称「`RequireProfileInRange` 仍在装载时拒绝
   超宽的行」，未披露四条被删。这类未披露的行为变更正是评审必须挡住的东西。

**必须怎么修**（二选一，我倾向前者）：

- 把四条子句补回 `RequireProfileInRange`（把 `row.NameTagItemId`、`row.NameTagExpireTime`、
  `row.ManageLevel` 作为参数传进去，`guild.PowerWar` 已在参数里），并补一条用例把「超宽被拒绝」
  钉住（同时解决 B-4 与 SC-C3 的取证缺口）。
- 或者，明确决定「这四个字段的拒绝下沉到编解码器」，把设计文档那一段改写成实际发生的事，
  并说明为什么这四个与其余六十来个字段待遇不同。**不接受维持现状而不动文档。**

### B 级

#### B-1 `CharacterProfileEntryProjection` 里还剩两处收窄，且执行者的计数把它们漏掉了

**文件与行**：`Server/src/Dfo.Server.Application/Characters/CharacterEntryStateServices.cs:32` 与 `:41`
（均为 `checked((uint)storage.Settings.UserStateBits)`）。

该文件 `checked((` 计数：基线 93 → 当前 **30**，不是执行者与设计文档所说的 28。
28 条确属技能/任务/进度/特性四个不在范围内的投影；剩下 2 条正落在本轮要清空的
**profile 投影自己**身上，喂的是 `CharacterPublicEntryState.VisibilityBits`（`uint`）与
`CharacterSettingsEntryState.UserStateBits`（`uint`）。这两个 `uint` 是线上宽度，
下游 `SelectCharacterHandler.cs:346` 与 `StateSynchronizationPayloadCodec.Entry.cs:750`
还要各再收窄一次到 `byte`——后者写成了 `checked((byte)checked((uint)settings.UserStateBits))`，
是同一个值被收窄三次。

另有两处**本轮新引入**的 Application 层收窄，同属 profile 路径：
`SelectCharacterPreparationService.cs:217`（`checked((byte)selectedEntry.Public.Extras.ManageLevel)`）
与 `:504`（`checked((byte)entry.Public.DerivedStats.EquippedCreatureLevel)`）。

**分级理由**：无行为缺陷（抛与不抛跟旧的一致），但 SC-C3 / AC-3 的判据「收窄逐条位于
`Dfo.Server.Protocol`」在 profile 路径上共有 4 处反例，且证据陈述（28 vs 30）不准确。
修不修可以讨论，**但计数与文档表述必须改对**。

#### B-2 三个记录类型已成孤儿，未按 CK-7 收掉

**文件与行**：`Server/src/Dfo.Server.Data/Characters/CharacterModels.cs:305`
（`CharacterDimensionEntryState`）、`:307`（`CharacterDimensionFlagsState`）、
`:309`（`CharacterPvpResultState`）。

全仓 grep（`Server/` 全部 `.cs`）：三者各自**只有声明那一行**，零引用。它们正是旧投影里那些
线上宽度的载体（`uint Key, byte Value1, byte Value2` 等），随收窄下沉一起失去了调用方。

**分级理由**：plan T-004 / CK-7 明写「删除因搬运而无调用方的东西，删前逐条核对」，这三条漏了。
留着它们等于把「领域里带线上宽度的形状」保留了三个，与本成果的立意相反。构建无警告是因为
它们是 public 类型，编译器不报。

#### B-3 新加的投影守卫覆盖不到 `NameTagItemId` / `NameTagExpireTime` 的对调

**文件与行**：`Server/tests/Dfo.Server.Application.Tests/Characters/CharacterEntryStateTests.cs`
新用例 `The_profile_projection_hands_every_concept_through_unchanged`，及其夹具 `Storage(...)` 里
`SkillTreeIndex: 0, NameTagItemId: 0, NameTagExpireTime: 0`。

**可复现构造方式**：E-4 的 M6——把投影 `Extras` 构造里的 `storage.NameTagItemId` 与
`storage.NameTagExpireTime` 两行对调，**全量 1173 条一条不红**。原因有三层且都得同时成立才漏：
夹具里两者都是 0；`SelectCharacterPayloadTestData` 里也都是 0；整记录表征用例钉的是
`CharacterProfileStorageRecord`，位置在投影**上游**，根本看不到投影。

**分级理由**：当前无缺陷，但 spec「What this Spec relies on」把「那三份用例逐字段断言，错位就会红」
当作本成果唯一的安全网——对投影这一段，那个前提不成立。执行者第四次演示发现投影无守卫并补了守卫，
补的守卫对这一对字段仍然无效。修法很便宜：在 `Storage(...)`（以及
`SelectCharacterPayloadTestData`）里给这两个字段不同的非零值。

#### B-4 `RequireProfileInRange` 的宽度边界一条都没有用例钉住，SC-C3 的第二项证据不存在

**可复现构造方式**：E-4 的 M5——把 `CharacterEntryAssembly.cs:600` 的 `Stamina` 上界由
`byte.MaxValue` 改成 `ushort.MaxValue`、`FatiguePenalty` 上界由 `uint.MaxValue` 改成 `long.MaxValue`，
**全量绿**。全仓 grep `Assert.Throws<OverflowException>` / `ThrowsAsync<OverflowException>` 零命中；
`SqliteCharacterEntryStateRepositoryTests.CharacterEntryState_RejectsInvalidNormalizedState` 的
六个 `InlineData` 覆盖的是归属、缺行、技能范围、外观大小、日常挑战组号、日程不全，
**没有一条走 `RequireProfileInRange` 的宽度分支**。

**分级理由**：SC-C3 的 Required evidence 明写「一条用例证明超宽仍被拒绝」，这条证据在仓库里不存在。
这不是本轮引入的（基线同样没有），但 SC-C3 是本轮要满足的成功条件，所以它现在算欠账。
与 A-1 合并修最省：补的那条用例同时是 A-1 的回归守卫。

#### B-5 下发帧夹具里大量字段取 0，「全量通过」不足以证明编解码器没读错来源

**可复现构造方式**：E-4 的 M1——`EncodeExtendedUserState` 里把 `checked((uint)guild.PowerWar)`
换成 `checked((uint)stats.ServerTimestamp)`，**全量绿**。因为
`SelectCharacterPayloadTestData.CreateComplete()` 里 `Guild(0, 0)` 与 `DerivedStats` 的
`ServerTimestamp` 默认值都是 0。同类不可分辨的还有：四抗、四种回复/跳跃速度、`Weight`、
`CurrentHpMpPercent`、`EquippedCreatureLevel`、`FlagByte`、`QuestShopCount`、
`ExtraEquipmentSlotState`、`SkillTreeIndex`、`NameTag*`、以及 `Condition` 里的大多数字段。

**分级理由**：基线同样如此，**不是本轮引入的回归**，我也确认本轮没有真的读错来源（E-3 的
静态逐条比对为零不一致，且写次数 20/73/57 与基线一致、帧 SHA-256 基线未改而全绿）。
写在这里是因为：执行者把「全量通过」当作 SC-C5 的证据之一，而这条证据对上述字段是空的；
真正支撑「下发帧一字节不变」的是帧 SHA 基线 + 逐条静态比对，评审记录应当把这一点说清楚。

#### B-6 投影里 PvP 结果的空值守卫消失了

旧投影有
`storage.Subtype1.PvpResults ?? throw new InvalidDataException("Subtype-1 PvP results were not loaded.")`，
新投影原样传递 `storage.Pvp`，只剩编解码器一侧的
`pvp.Results ?? throw Missing("CharacterEntry.Public.Pvp.Results")`
（`StateSynchronizationPayloadCodec.Entry.cs:783`）。与 A-1 同一族（装载时拒绝 → 组帧时拒绝），
但装配器恒定给 `[]`，生产路径构造不出来，编解码器一侧的守卫也有用例钉着
（`SelectCharacterPayloadCodecTests.cs:396-408`），故只记不拦。

#### B-7 入场态持有一堆名字叫 `*StorageRecord` 的成员

`CharacterPublicEntryState`（`CharacterModels.cs:322` 起）的六个概念成员类型是
`CharacterConditionStorageRecord` 等；同一批类型又被 `Dfo.Server.Abstractions` 的
`CharacterProfileStorageRecord` 引用。复用本身是这次设计的核心（正因为两端是同一组概念，
中间那段复制才能消失），但「入场态的成员叫 StorageRecord」读起来别扭，且
`CharacterProfileStorageRecord` 在 Abstractions、它的六个成员类型在 Data，定义点被拆到两个工程。
各类型仍只有一个定义点，不违反根 SC-9，只是命名与落位值得下一轮收一下。

#### B-8 设计文档两处排版

`Docs/Refactor/DomainInstanceDesign.md` 新段落：「…still lives in the domain, deliberately;」
之后被一个空行截断，分号悬在段尾，下一段以 `ItemContainerOperations.ConsumeAsync` 开头，
读起来是半句话。同一节里 `layer -- twenty-eight` 用了 `--`，而文件其余部分统一用破折号。

## SC 逐条核对

| ID | 判定 | 依据 |
|---|---|---|
| SC-C1 | **满足** | 全仓 `Subtype0`/`Subtype1` 命中仅 4 处：`SupplementalSynchronizationCodec.cs` 的私有方法 `EncodePvpSubtype0`（在 `Dfo.Server.Protocol` 内）与 `SelectCharacterPayloadCodecTests.cs` 的两个测试方法名。**零个记录类型**。领域侧干净。 |
| SC-C2 | **满足** | `CharacterProfileStorageRecord` 的成员逐个可指出概念；`Subtype0`/`Subtype1` 两个拍平方法已删，替换为 `Condition(CharacterConditionRow)`、`DerivedStats(CharacterStatsRow)` 及四处按表内联构造。E-2 证明 78 个字段全部对得上。 |
| SC-C3 | **不满足** | (a) A-1：四个字段的超宽拒绝被删；(b) B-1：profile 路径上仍有 4 处 Application 层收窄（`CharacterEntryStateServices.cs:32,41`、`SelectCharacterPreparationService.cs:217,504`）；(c) B-4：Required evidence 要求的「一条用例证明超宽仍被拒绝」在仓库里不存在（M5 实测边界失效全量绿）。 |
| SC-C4 | **满足** | `SelectCharacterPreparationService.cs:210-211,217,504` 与 `SelectCharacterHandler.cs:351-352,363,365-366` 全部改为经 `DerivedStats` / `Extras` / `Pvp` 按名字读，无一处经由段记录。 |
| SC-C5 | **满足** | 帧 SHA-256 基线用例 `SelectCharacterAcceptedTranscriptTests.cs` 未改而全绿；协议编解码用例期望字节零改动；两份 golden 的 `git diff` 为空；第三份 golden 数值多重集零差异（E-1）。**关于字面判据的说明**：SC-C5 写的是「三份整记录表征用例期望值 `git diff` 为空」，而 `EntryLoadCharacterizationTests` 的 `Expected` 确实被重写了。契约 C-2（「断言的表述方式可随形状改变，期望的数值不可」）与 plan T-001（「按新形状重写，期望的数值一个字节不改」）已明确授权这件事，SC-C5 的措辞是这两处的简写，按 C-2 判定为满足。 |
| SC-C6 | **满足** | 独立复现：build 0 警告 0 错误；全量 1173/2/0（净增 1 = 新守卫用例）。 |
| SC-C7 | **部分满足** | 执行者四次演示的红点我未逐一复跑，但其中最关键的一次（投影无守卫）留下了可验证的产物：M4 证明新守卫真的会红。缺口在于本成果**新声称**的两条保证不可证伪：「超宽仍在装载时被拒绝」（M5 绿）与「每个概念在传递中逐字段不变」（M6 绿）。 |

## AC 逐条核对

| ID | 判定 | 依据 |
|---|---|---|
| AC-1 | 满足 | 同 SC-C2。 |
| AC-2 | 满足 | `CharacterPublicEntryState` 七个成员：Condition / Settings / Pvp / ExpertJob / DerivedStats / Guild / Extras，各自可指出概念；`Extras` 自带注释承认它不是概念而是随行杂项，这与 RQ-2 不冲突。 |
| AC-3 | **部分满足** | 「`Subtype0`/`Subtype1` 命名的记录类型零命中」成立；「收窄语句全部在 `Dfo.Server.Protocol`」不成立，见 B-1。 |
| AC-4 | 满足 | 同 SC-C4。 |
| AC-5 | 满足 | 同 SC-C5。 |
| AC-6 | 满足 | 同 SC-C6。 |
| AC-7 | **部分满足** | 四类演示（错位一格 / 收窄丢失 / 真假反向 / 取错概念）我用 M3、M2、M4 独立复现了其中三类且都先红；「收窄丢失」我用 M2（宽度改错）代替，同样红。但见 SC-C7 的缺口。 |

## 超范围检查

契约 Out 列了五项，逐条看：

- **下发帧任何字节与字段次序**：未变，见 SC-C5。
- **物品侧与账号侧的快照**：`AccountEntryLoadCharacterizationTests` / `InventoryLoadCharacterizationTests`
  的 `git diff` 为空；`Dfo.Server.Application/Accounts/`、`Inventory/` 无改动。
- **持久层形状**：`Dfo.Server.Persistence` 的 `git diff --stat` 为空，表数与基线同为 44。
- **客户端错误码**：未见任何拒绝码改动；`SelectCharacterHandler` 的改动只在取值路径。
- **「把入场侧段记录进一步拆成更小的协议类型」**：执行者**删除**了入场侧段记录而不是移进协议工程。
  我判定 **不算超范围**：spec.md 的 Outcome 一句「以协议段命名的记录在领域里不再存在——不是搬走，
  是没有了」、RQ-3「那段 45 字段的按位复制…它消失」、AC-3「零命中」三处都明确授权了删除，
  而这条 Out 约束的是「移进去之后不许再拆细」，删除比「移进去」更靠内、不触及它。
  **需要记一笔的是**：契约 Scope/In 写的是「段记录及其装配（含位宽收窄）**移入** `Dfo.Server.Protocol`」，
  与 spec 的「删除」在字面上不一致，而契约 revision 未随之推进。这是文档漂移，不是范围越界；
  收口时应在契约或 multi-spec-map 里补一句订正，否则下一位读者会以为协议工程里有这两个类型。

## 判 changes_required 所必须修的

1. **A-1**：把 `ManageLevel` / `NameTagItemId` / `NameTagExpireTime` / `GuildPowerWar` 四条宽度检查
   补回 `CharacterEntryAssembly.RequireProfileInRange`；或者明确改判为「下沉到编解码器」，
   并把 `Docs/Refactor/DomainInstanceDesign.md`「Settled by the snapshot-side implementation」
   第二段改写成实际行为。二者必居其一。
2. **B-4 + A-1 的守卫**：补至少一条用例，构造一个超宽的存储行并断言它在装载时被拒绝
   （四个字段各一条 `InlineData` 最省），使 SC-C3 的 Required evidence 有实物。
3. **B-2**：删除 `CharacterModels.cs:305/307/309` 三个零引用的记录类型（或说明为什么留）。
4. **B-1 的表述**：把「28 处收窄，全在其他四个投影」改成实际数字，并说明
   `CharacterEntryStateServices.cs:32,41` 与 `SelectCharacterPreparationService.cs:217,504`
   这四处 profile 路径上的收窄为什么留在 Application。

建议一并处理（不阻断）：B-3 给 `NameTagItemId`/`NameTagExpireTime` 不同的非零夹具值；
B-8 修文档排版；超范围检查末段的契约/spec 措辞漂移补一句订正。

## 我对「保留 `RequireProfileInRange`」这个决定的判断

**保留的方向是对的，执行是不完整的。**

方向对，理由有二：其一，它做的事在语义上确实是**拒绝**而不是**转换**——它不产生任何值，
只在存储行不可能被发出去时把角色挡住，因此「领域知道几个宽度」在这里是「领域知道自己有些状态
发不出去」，不是「领域替协议做打包」。其二，把它下沉到编解码器会把发现时机从「装载」推到「组帧到
一半」，而组帧一半失败的补偿路径（`SelectCharacterHandler.cs:138` 那个 `catch (Exception)`
会走 `CompensateSelectedCharacterSessionCommand` 并关连接）比装载失败重得多。
E-5 第 4 步已经把这两种形态实测出来了，差别是真的。设计文档把这一点写成「留下的是拒绝，
不是转换；是否该搬走是个关于*何时*发现的问题，留作未决」——这个措辞我认为准确且诚实。

它与 SC-C3 **不冲突**：SC-C3 的判据是「收窄逐条位于 `Dfo.Server.Protocol`」，
`RequireProfileInRange` 里没有一次收窄，全是范围判断，一个 `checked` cast 都没有。
真正让 SC-C3 不成立的是 B-1 那四处**真的收窄**，以及 A-1、B-4 的取证缺口，不是这个保留决定。

至于 `Dfo.Server.Application` 里剩下的收窄：**28 处属于技能/任务/进度/特性四个投影的部分，
不使 SC-C3 不成立**——SC-C3 与 RQ-3 的主语始终是 profile 快照那条路，那四个投影不在本成果范围内，
设计文档也把它们明写成遗留项。但执行者的计数漏了 `CharacterProfileEntryProjection` 自己剩的 2 处，
再加上本轮新引入的 2 处，profile 路径上一共 4 处 Application 层收窄——**这四处才是使 SC-C3
不成立的部分**，且必须在表述上改对。

## 第二轮

评审范围：同一工作树、同一接受基线 `078d762`，本轮全部改动仍未提交（17 个已修改文件 +
两个未跟踪项）。判据来源同第一轮，另加第一轮 `changes_required` 的必修清单四条。
执行者交办说明里的六项整改与两次演示**一律未采信**，逐项独立重做。

**verdict: changes_required**

第一轮的 A-1 **确已闭合**，且我独立证明补回的四条与基线逐条等价（边界值、比较方向、落在哪个
`if`、抛哪条消息全部相同），新增用例是真守卫。第一轮必修清单的第 1、2、3 条我判定关闭。

仍判 `changes_required` 的原因只有一条，落在必修清单第 4 条上：整改给出的「四处收窄为什么留在
Application」这个理由，**四处里有三处不属实**，而这个理由已被写进设计权威文档
`Docs/Refactor/DomainInstanceDesign.md:391-398`。我用两次「真改真跑 + 全量」把它证伪了。
见 A-2。修法很小。

### 我实际执行过的独立实验

每一次临时改动之后都用 `sha256sum -c` 对 17 个文件逐个校验复原；结束时工作树与评审开始时
逐字节一致，`git status --porcelain` 仍是 17 个 ` M` 加两个未跟踪项。临时探针文件已删除。
删除探针后又重跑了一次构建与全量作为收尾证据：0 警告 0 错误、1180/2/0。

#### R-E0 构建与全量（独立复现）

- `dotnet build Server/86JP.Server.slnx` = **0 警告 0 错误**。
- `dotnet test Server/86JP.Server.slnx -m:1`（两个 PVF 环境变量已设）
  = 60 + 530 + 4 + 100 + 46 + 237 + 203 = **1180 通过 / 2 跳过 / 0 失败**。
  与执行者所报一致；净增 7 全部落在 `Dfo.Server.Persistence.Tests`（230 → 237），
  即新增 `[Theory]` 的七行。`Dfo.Server.Host.Tests` 本轮三次满载运行均未出现假红。

#### R-E1 A-1 与基线的逐条等价（静态）

`git show 078d762:...CharacterEntryAssembly.cs` 第 609-622 行（第二个 `if`，抛
`"Character extended detail state is out of range."`）对照当前
`Server/src/Dfo.Server.Application/Characters/CharacterEntryAssembly.cs:641-655`：

| 基线子句 | 当前子句 | 落在哪个 `if` | 边界 | 比较方向 |
|---|---|---|---|---|
| `s1.ManageLevel is < 0 or > byte.MaxValue` | `manageLevel is < 0 or > byte.MaxValue` | 第二个 | 同 | 同 |
| `s1.NameTagItemId is < 0 or > uint.MaxValue` | `nameTagItemId is < 0 or > uint.MaxValue` | 第二个 | 同 | 同 |
| `s1.NameTagExpireTime is < 0 or > uint.MaxValue` | `nameTagExpireTime is < 0 or > uint.MaxValue` | 第二个 | 同 | 同 |
| `s1.GuildPowerWar is < 0 or > uint.MaxValue` | `guild.PowerWar is < 0 or > uint.MaxValue` | 第二个 | 同 | 同 |

四条连书写次序都与基线一致（`ManageLevel` → `Weight` → `NameTagItemId` → `NameTagExpireTime`
→ `GuildPowerWar` → `ServerTimestamp`）。`guild.Level` 仍留在第一个 `if` 的 byte 清单里，
与基线的 `s0.GuildLevel` 同位。**没有一条落进错误的 `if`，异常消息因此不变。**

调用点 `CharacterEntryAssembly.cs:183-185` 与新签名（`int skillTreeIndex, long nameTagItemId,
long nameTagExpireTime, int manageLevel`）按位对齐。这里有两个真实的静默风险：
`skillTreeIndex` 与 `manageLevel` 同为 `int`，对调可编译且**行为会变**（`< -1 or > 1`
对 `< 0 or > byte.MaxValue`）；`nameTagItemId` 与 `nameTagExpireTime` 同为 `long`，
对调可编译但**两条判据字面完全相同**，因此在这个方法内部对调是无痕的。两者我都用探针查了，见 R-E2。

#### R-E2 我自己的边界探针（12 例，临时文件 `ZzReviewProbeTests.cs`，已删除）

不采信执行者的用例，另建夹具（character_id 91）：

1. **恰在最大值仍装载**：`manage_level=255`、`name_tag_item_id=4294967295`、
   `name_tag_expire_time=4294967295`、`guild_power_war=4294967295`、`guild_level=255`、
   `skill_tree_index=1` → `LoadAsync` 成功，且六个取值原样返回。**通过。**
2. **各加一被拒，且消息是基线那一条**：四个字段各 +1，逐个断言
   `InvalidDataException` 且 `Message == "Character extended detail state is out of range."`。
   **四条全部通过** —— 这是「落在第二个 `if`」的实测证据，不是读代码得出的。
3. **各取 -1 也被拒**：四个字段各设 `-1`，同一条消息。**四条全部通过** ——
   `< 0` 这一半的比较方向在。
4. **相邻判据没有被错误吸收**：`guild_level=256` → `"Character public profile state is out of
   range."`（第一个 `if`，与基线同）；`skill_tree_index=2` → 同为第一个 `if` 的消息。
   **两条通过** —— 证明 `skillTreeIndex` 与 `manageLevel` 两个 `int` 形参**没有对调**
   （若对调，`skill_tree_index=2` 会落进 byte 判据而放行，`manage_level=256` 会撞上
   `< -1 or > 1` 而抛出错误的那条消息）。
5. **NameTag 那一对端到端不对调**：`name_tag_item_id=1234567`、
   `name_tag_expire_time=7654321`（互不相同、且都不为 0）→ 装载后
   `Profile.NameTagItemId == 1234567`、`Profile.NameTagExpireTime == 7654321`；
   再过一次 `CharacterProfileEntryProjection.Build`，`Public.Extras` 上两值仍各就各位。**通过。**

#### R-E3 A-1 的反向证明（真改真跑）

把补回的四条子句从 `RequireProfileInRange` 里逐字删掉，重建后：

- 执行者的新 `[Theory]`：**7 条里恰好红 4 条**（`manage-level`、`name-tag-item`、
  `name-tag-expiry`、`guild-power-war`），前三行（`stamina`、`mood`、`pvp-rank`）仍绿。
  与执行者所述一致，我独立复现。
- 我自己的探针：R-E2 第 2、3 两组共 8 条全红，第 1、4、5 组不动。

复原后 `sha256sum -c` 17 项全 `OK`。**A-1 闭合，且新用例是真守卫。**

#### R-E4 `EquippedCreatureLevel` 那处收窄可以无痛去掉（证伪整改理由之一）

把 `SelectCharacterPreparationService.cs:504` 的
`checked((byte)entry.Public.DerivedStats.EquippedCreatureLevel)` 改回
`entry.Public.DerivedStats.EquippedCreatureLevel`：
**构建 0 警告 0 错误；全量 1180/2/0 全绿**（连我那 12 条探针一起）。
理由是 `WorldContentSubject.EquippedCreatureLevel` 声明为 **`int`**
（`Server/src/Dfo.Server.Abstractions/World/IWorldEntryServices.cs:41`），不是 `byte`。

#### R-E5 `VisibilityBits` 那两处也可以无痛去掉（证伪整改理由之二）

把 `CharacterModels.cs:326` 与 `:419` 的 `uint VisibilityBits` 改成 `long`，
并把 `CharacterEntryStateServices.cs:32` 与 `:41` 的 `checked((uint)storage.Settings.UserStateBits)`
改成 `storage.Settings.UserStateBits`：
**构建 0 警告 0 错误；全量 1180/2/0 全绿**，且**只需要动这两个文件**——
唯一的读者 `SelectCharacterHandler.cs:346` 本来就写着 `checked((byte)…)`，源是 `long` 一样编译。

#### R-E6 投影守卫可证伪（复现执行者的演示二）

把 `CharacterEntryStateServices.cs:23-24` 的 `storage.NameTagItemId` 与
`storage.NameTagExpireTime` 对调 → **恰好红一条**：
`CharacterEntryStateTests.The_profile_projection_hands_every_concept_through_unchanged`。
夹具由 0/0 改成 77/88 确实起了作用。

#### R-E7 编解码器一侧的同一对仍不可区分（B-3 残留）

把编解码器里两处 `writer.Write(checked((uint)extras.NameTagItemId));
writer.Write(checked((uint)extras.NameTagExpireTime));`（`StateSynchronizationPayloadCodec.Entry.cs:745`
与 `:774`）全部对调 → **全量 1180/2/0 一条不红**。
原因：`SelectCharacterPayloadTestData` 里这两个字段仍是
`NameTagItemId: 0, NameTagExpireTime: 0`（`SelectCharacterPayloadCodecTests.cs:811-812`），
下发帧 SHA-256 基线因此对这一对是盲的。见 B-10。

#### R-E8 golden 数值多重集（独立重做）

按 `"""` raw literal 整块解析基线与当前的 `EntryLoadCharacterizationTests.cs`：
新旧各 **215** 条 `key = value`；**值多重集双向差集为空**；
`(叶名, 值)` 多重集仅 9 条差异且一一配对，全是「去掉概念前缀挂到新概念下」的改名
（`ExpertJobType→Type`、`ExpertJobExperience→Experience`、`PvpRankPoint→RankPoint`、
`PvpStatA→StatA`、`PvpWinStreak→WinStreak`、`PvpLoseStreak→LoseStreak`、`PvpResults→Results`、
`GuildLevel→Level`、`GuildPowerWar→PowerWar`），值一个没变。
与第一轮结论完全一致 —— **整改没有碰过 golden。**

#### R-E9 编解码器逐方法写入计数（独立重做，比第一轮更全）

脚本对该文件**全部 40 个含 `writer.Write(` 的方法**做花括号配对后计数，与基线逐方法比对：
**40 个方法全部相等，合计 438 / 438，零漂移**（含 `EncodeSupportStats` 20、
`EncodeCurrentUserState` 73、`EncodeExtendedUserState` 57）。
配合 `SelectCharacterAcceptedTranscriptTests.cs`（写死每帧 SHA-256）`git diff` 为空且全绿，
「下发帧一字节不变」这条我认为已被两条独立证据钉死。

#### R-E10 边界、编码与其余表征用例

- `AccountEntryLoadCharacterizationTests.cs`、`InventoryLoadCharacterizationTests.cs`、
  `SelectCharacterAcceptedTranscriptTests.cs` 三个文件**不在 `git status` 里**，`git diff` 为空。
- `SelectCharacterPayloadCodecTests.cs` 的 `git diff` 只改构造与取值路径，
  期望字节（含 `Assert.Equal(13502u, BinaryPrimitives.ReadUInt32LittleEndian(...))`）一处未动。
- `Server/src/Dfo.Server.Persistence/` 的 `git diff --stat` 为空；`Tool/`、`.worktrees/`
  的 `git status --porcelain` 无命中。
- 全部 17 个已修改文件 + 7 个新增/未跟踪工件：**CRLF 行数 == 总行数**、无 BOM；
  唯一例外 `RosterScreenQueryCodecTests.cs` 带 BOM，基线本就如此，按约束保留。

### 整改六项的逐条核对

| # | 执行者所述 | 我的判定 | 依据 |
|---|---|---|---|
| 1 | A-1 四条拒绝已补回，加三个参数 | **属实，已闭合** | R-E1 / R-E2 / R-E3 |
| 2 | 新增七行 `[Theory]` 钉住超宽拒绝 | **属实，是真守卫** | R-E3：拿掉四条恰好红那四行 |
| 3 | 收掉三个零引用记录类型 | **属实** | 全仓 `.cs` grep `CharacterDimensionEntryState` / `CharacterDimensionFlagsState` / `CharacterPvpResultState` **零命中**；`Dimensions`/`DimensionFlags` 已改为元组，孤儿因此产生也因此清掉 |
| 4 | 四处收窄未下沉，理由是被别的投影逼窄 | **三处不属实** | R-E4 / R-E5，见 A-2 |
| 5 | NameTag 夹具 0/0 → 77/88 | **半属实** | 投影侧已可区分（R-E6）；编解码器侧仍不可区分（R-E7），第一轮 B-3 明写「以及 `SelectCharacterPayloadTestData`」 |
| 6 | 子契约推进到 revision 2，Scope/In 改为「删除」 | **属实** | 子契约 frontmatter `revision: 2`；Scope/In「段记录**删除**」；Out 已无「移进协议工程即止」；Assumptions 里写明了修订理由 |

### A 级（必须修）

#### A-2 「四处收窄为什么留在 Application」这个理由，四处里三处不属实，且已写进设计权威文档

**文件与行**：`Docs/Refactor/DomainInstanceDesign.md:391-398`。原文：

> Four narrowings survive on the profile's own path, and each survives for the same reason: its
> destination type belongs to a *different* projection and is declared narrow there. […]
> Widening those types is work on those projections, not on this one, and doing it here would
> have reached into records this outcome promised not to touch.

逐处核实：

| 收窄点 | 目标成员 | 目标声明 | 目标由谁构造 | 「属于另一个投影且声明为窄」 |
|---|---|---|---|---|
| `CharacterEntryStateServices.cs:32` | `CharacterPublicEntryState.VisibilityBits` | `uint`（`CharacterModels.cs:326`） | **本投影自己**（`CharacterEntryStateServices.cs:14`，全仓唯一构造点） | **否** |
| `CharacterEntryStateServices.cs:41` | `CharacterSettingsEntryState.VisibilityBits` | `uint`（`CharacterModels.cs:419`） | **本投影自己**（`:38`，全仓唯一构造点）；且该成员**全仓零读者** | **否** |
| `SelectCharacterPreparationService.cs:504` | `WorldContentSubject.EquippedCreatureLevel` | **`int`**（`IWorldEntryServices.cs:41`） | 同一个文件自己（`:496`） | **否**（目标根本不窄；且这处 `checked((byte)…)` 是**本轮新加的**，基线该行无 cast） |
| `SelectCharacterPreparationService.cs:217` | `CharacterRosterEntryState.ManageLevel` | `byte`（`StateSynchronizationModels.cs:71`） | 同一个文件（`:216`），但该记录是名录侧线上模型，读者是 `SupplementalSynchronizationCodec.cs:40` | **是**（四处里唯一成立的一处） |

**可复现构造方式**：R-E4 与 R-E5。两次都是构建 0 警告 0 错误 + 全量 1180/2/0 全绿，
且两次动到的文件全部是**本成果自己已经在改的文件**（`CharacterModels.cs`、
`CharacterEntryStateServices.cs`、`SelectCharacterPreparationService.cs`），
没有一处「reached into records this outcome promised not to touch」。

补充：那两处 `checked((uint)…)` 与 `:504` 的 `checked((byte)…)` 在生产装载路径上其实是**死转换**——
`RequireProfileInRange` 已经先拒了 `settings.UserStateBits is < 0 or > uint.MaxValue` 与
`stats.EquippedCreatureLevel is < 0 or > byte.MaxValue`，值到不了这三行还能越界。
（严格说不是绝对死：测试可以绕开装载直接构造 `CharacterPublicEntryState`。但「留着它是为了
保住一次抛出」这个可能的辩护，在生产路径上不成立。）

另有一处漏数：`CharacterEntryStateServices.cs:297`
（`specialRewardQuestIds.Add(checked((uint)clearFlag.SlotIndex))`，在
`CharacterEntryStateDomainService.LoadAsync` 里）也在 profile 路径上，喂的是
`CharacterEntryExtrasState.SpecialRewardQuestIds`（`IReadOnlyList<uint>`，线上宽度）。
所以「Four narrowings survive on the profile's own path」这个数本身也是少的。
（`CharacterEntryAssembly.cs:210-217` 那七处 —— `Job`/`Level` → `byte`、`CloneTitleItemId` → `uint`、
热键 → `ushort`、`PvpGrade`/`PvpRatingGrade`/`UserState` → `byte` —— 与基线完全相同、
不属于被搬走的那 78 条，我不计入；但文档说「profile 自己这条路上只剩四处收窄」时，
读者会以为它们不存在。）

关于计数那一半：我按「含 `checked((` 的行数」复核，基线 `CharacterEntryStateServices.cs` = **93**、
当前 = **30**，其中 28 属技能/任务/进度/特性四个投影 —— **执行者与文档的这组数字属实**。
（按出现次数而非行数计，基线是 96，因为基线有 2 行是双重收窄；两种口径都自洽，文档用的是行数口径。）

**分级理由**（与第一轮给 A-1 的第 2 条同一把尺）：

1. 这段话是第一轮 `changes_required` 必修清单**第 4 条的答案**。清单要求的是
   「说明这四处为什么留在 Application」；给出的说明经两次真改真跑证伪，该条不能算关闭。
2. 它给设计权威文档引入了一处**新的事实错误**。根契约写明该文档是本任务的设计权威、
   「若某条规则站不住应修订该文档并记录理由」，根 SC-10 是「设计文档与落地一致」——
   而这段话对三处的落地描述是反的。第一轮正是以同一条理由把 A-1 判为 A 级。
3. 它的方向性后果很实在：下一位读者被告知「拓宽这些类型是那些投影的工作」，
   于是不会去动一个改三行、只碰本成果自己文件、全量全绿的东西。
4. 其中 `SelectCharacterPreparationService.cs:504` 还是**本轮新加进 Application 层的一处收窄**
   （基线该行没有 cast），方向与 SC-C3 / RQ-3 相反，而记录下来的理由把它说成了不得已。

**必须怎么修**（二选一，我倾向前者）：

- 按事实重写 `DomainInstanceDesign.md:391-398`：只有 `ManageLevel` 那处是被目标类型逼窄的
  （`CharacterRosterEntryState.ManageLevel` 是 `byte`，读者在 `SupplementalSynchronizationCodec`）；
  `VisibilityBits` 两处与 `EquippedCreatureLevel` 一处的目标类型都属于本成果自己已经在改的记录，
  留下它们是一个**选择**（例如「收尾轮不再扩大 diff」），把那个选择写出来即可；
  并把计数改对（至少补上 `CharacterEntryStateServices.cs:297`）。
- 或者直接按 R-E4 / R-E5 去掉那三处（三行改动、构建 0 警告、全量绿已验证），
  文档随之只留 `ManageLevel` 一处。

**不接受维持现状而不动文档。**

### B 级（本轮新记，均不阻断）

#### B-9 新增 `[Theory]` 只断言异常类型，不断言消息

`SqliteCharacterEntryStateRepositoryTests.cs:393-432`：七行都是
`Assert.ThrowsAsync<InvalidDataException>`，不看 `Message`。
后果是「这四条落在哪个 `if`」没有被钉住：若某条被挪进第一个 `if`，消息会从
`"Character extended detail state is out of range."` 变成
`"Character public profile state is out of range."`，用例照样绿。
我的探针（R-E2）断言了消息，成本只是多一个 `InlineData` 列。建议补上。

#### B-10 第一轮 B-3 只修了一半，编解码器一侧的 NameTag 对调仍全量绿

`SelectCharacterPayloadTestData` 的 `NameTagItemId: 0, NameTagExpireTime: 0`
（`SelectCharacterPayloadCodecTests.cs:811-812`）未改，R-E7 实测对调编解码器两处写入**一条不红**。
第一轮 B-3 的修法明写「在 `Storage(...)`（以及 `SelectCharacterPayloadTestData`）里给这两个字段
不同的非零值」。值得注意的是：这一对字段的**排序与收窄本轮才搬进编解码器**，
也就是说这个取证缺口现在正落在本成果新写的代码上（SC-C7 / AC-7 的「错位一格」对这一对不可演示）。
改一行夹具即可。

#### B-11 `spec.md` 的 `contract_revision: 1` 已过期

子契约已推进到 `revision: 2`（正是为了与 spec 的「删除」对齐），
而 `spec.md` frontmatter 仍写 `contract_revision: 1`。收口时对齐一下。

#### B-12 RQ-2 里「0 与非 0 已经变成真假」未实现，且相对基线是倒退

spec RQ-2 原文：「它持有同一组概念，只是用领域的类型（0 与非 0 已经变成真假，因为那是含义不是宽度）」。
实际相反：基线 `CharacterSubtype0EntryState`（`078d762:CharacterModels.cs:179-205`）
有八个 `bool`（`EventCharacter`、`PrivateStore`、`HardcoreMode`、`HardcoreDead`、
`PremiumPcRoom`、`Disguised`、`ReturnUser`、`LinkSlotEnabled`），
现在 `CharacterConditionStorageRecord`（`CharacterModels.cs:188-225`）把它们全部退回 `int`，
真假转换移进了编解码器（`c.EventCharacter != 0 ? (byte)1 : (byte)0`，八处）。
行为没问题（`RequireProfileInRange` 仍把这八个限在 0/1，帧字节不变），
AC-2 的 evidence 列「每个成员可指出其概念」也仍然成立，所以我不因此改判；
但 RQ-2 的这半句既没实现也没被撤销，与执行者对待契约 Scope/In 的做法（发现不对就推进 revision
并写明理由）不一致。第一轮未提及此点，我作为新发现记在这里。

#### B-13 第一轮 B-8 的排版只修了一半

`Docs/Refactor/DomainInstanceDesign.md`：` -- ` 已全部改掉（全文零命中）；
但「…still lives in the domain, deliberately;」后紧跟空行、下一段以
`ItemContainerOperations.ConsumeAsync` 开头（`:413-418`），分号仍悬在段尾。

### 第一轮其余 B 级的处置复核

| ID | 处置 | 我的判断 |
|---|---|---|
| B-1 | 计数改对了（93 → 30、其中 28 属其他四个投影，我按行计数复核**属实**），但理由不属实 | **拆开**：计数部分关闭；理由部分升为 **A-2** |
| B-2 | 三个零引用类型已删 | **关闭** |
| B-3 | 只修了投影侧夹具 | **仍开**，续记为 B-10（仍为 B） |
| B-4 | 已补用例 | **关闭**（另见 B-9） |
| B-5 | 如实记录，未改 | **维持 B**。基线同样如此，非本轮引入；R-E9 的 438/438 与帧 SHA 基线仍是「一字节不变」的真凭据 |
| B-6 | 未改：PvP 结果的空值守卫只剩编解码器一侧（`StateSynchronizationPayloadCodec.Entry.cs:788`） | **维持 B**。装配器恒给 `[]`，生产路径构造不出来，编解码器守卫有用例钉着 |
| B-7 | 未改：入场态成员仍叫 `*StorageRecord`，定义点跨 Abstractions / Data 两个工程 | **维持 B**。`CharacterPublicEntryState` 的 `<remarks>` 已把复用理由写清楚；命名与落位留给下一轮 |
| B-8 | 只修了 `--` | **仍开**，续记为 B-13 |

**第一轮其余 B 级里没有一条需要升级为阻塞**；A-2 是从 B-1 的「理由」那一半升上来的，
因为它同时是必修清单第 4 条的答案。

### SC 逐条核对

| ID | 判定 | 依据 |
|---|---|---|
| SC-C1 | **满足** | 全仓 `.cs` 搜 `Subtype0` / `Subtype1`：7 处命中，全是 `Dfo.Server.Protocol` 内的私有方法 `EncodePvpSubtype0`（4 处）与两个测试方法名（3 处）。**零个记录类型。** 三个零引用类型也已删净 |
| SC-C2 | **满足** | `CharacterProfileStorageRecord`（`ICharacterEntryStateServices.cs:5-29`）成员逐个可指出概念；装配器无按段拍平的方法。第一轮的 78 字段脚本比对我未重做 —— 整改未触及那一段，且 R-E8 / R-E9 / 帧 SHA 三条独立证据都指向它没变 |
| SC-C3 | **部分满足** | (a) 超宽拒绝已补回并有用例钉住 —— 第一轮的 (a)(c) 两个缺口**已关闭**（R-E1/E2/E3）；(b) profile 路径上仍有 Application 层收窄（`CharacterEntryStateServices.cs:32,41,297`、`SelectCharacterPreparationService.cs:217,504`），且**留下的理由不属实**（A-2）。判据「收窄逐条位于 `Dfo.Server.Protocol`」在字面上仍不成立 |
| SC-C4 | **满足** | 全仓无 `.Subtype0` / `.Subtype1` 取值；`SelectCharacterPreparationService.cs:210-211,217,504` 与 `SelectCharacterHandler.cs:351-352,363,365-366` 全部经 `DerivedStats` / `Extras` / `Pvp` 按名字读 |
| SC-C5 | **满足** | 帧 SHA-256 基线用例未改而全绿；协议编解码用例期望字节零改动；两份 golden `git diff` 为空；第三份 golden 数值多重集零差异（R-E8）；编解码器逐方法写入计数 438/438（R-E9）。关于「`EntryLoadCharacterizationTests` 的 `Expected` 被重写」，我沿用第一轮按 C-2 与 plan T-001 的判定 |
| SC-C6 | **满足** | 构建 0 警告 0 错误；全量 1180/2/0（R-E0，且删掉探针后复跑确认） |
| SC-C7 | **较第一轮实质改善，仍有一处缺口** | 第一轮点名的两条不可证伪保证都补上了：「超宽仍在装载时被拒绝」现在可证伪（R-E3，恰好红四行）；「每个概念在传递中逐字段不变」现在可证伪（R-E6）。剩下的缺口是 R-E7：编解码器一侧 `NameTagItemId` / `NameTagExpireTime` 对调仍全量绿（B-10） |

### AC 逐条核对

| ID | 判定 | 依据 |
|---|---|---|
| AC-1 | 满足 | 同 SC-C2 |
| AC-2 | 满足 | `CharacterPublicEntryState` 七个成员各可指出概念；`Extras` 自带 `<remarks>` 承认它是随行杂项。注：RQ-2 的「0/非 0 变真假」未实现，见 B-12，但不影响 AC-2 的 evidence 列 |
| AC-3 | **部分满足** | 「`Subtype0`/`Subtype1` 命名的记录类型零命中」成立；「收窄语句全部在 `Dfo.Server.Protocol`」不成立（A-2 表中的四处 + `CharacterEntryStateServices.cs:297`） |
| AC-4 | 满足 | 同 SC-C4 |
| AC-5 | 满足 | 同 SC-C5 |
| AC-6 | 满足 | 构建 0 警告 0 错误 |
| AC-7 | **基本满足** | 四类演示我独立复现或构造了三类：「收窄/拒绝丢失」= R-E3、「装配取错概念」= R-E6、「错位一格」= R-E2 第 4 组（`skillTreeIndex` / `manageLevel` 形参对调会被消息判据抓住）。第四类「真假转换反向」我未单独真改：夹具里那八个布尔位全为 0，反向会把 0 写成 1、直接改帧字节，`SelectCharacterAcceptedTranscriptTests` 必红 —— 这是**构造上确定**，不是我实测的，特此分开写 |

### 超范围检查

- 下发帧字节与字段次序：未变（R-E9 + 帧 SHA 基线）。
- 物品侧与账号侧快照：两份表征用例 `git diff` 为空，相关目录无改动。
- 持久层形状：`Dfo.Server.Persistence` 的 `git diff --stat` 为空。
- 客户端错误码：无改动。
- `Tool/`、`.worktrees/`：无命中。
- 契约与 spec 的措辞漂移：子契约已推进 revision 2 并写明理由，第一轮指出的这一点**已收口**；
  残留只有 `spec.md` 的 `contract_revision`（B-11）。

### 判 changes_required 所必须修的

**只有一条。**

1. **A-2**：`Docs/Refactor/DomainInstanceDesign.md:391-398` 那段关于「四处收窄为什么留下」的说明，
   四处里三处与代码相反，且计数少算了 `CharacterEntryStateServices.cs:297`。
   二选一：(a) 按事实重写这段 —— 只有 `SelectCharacterPreparationService.cs:217` 一处是被
   `CharacterRosterEntryState.ManageLevel`（`byte`）逼窄的；另外三处的目标类型
   （`CharacterModels.cs:326`、`:419`、`IWorldEntryServices.cs:41`）分别是本投影自己的输出
   与一个 `int`，留下它们是选择不是被迫 —— 并把计数改对；
   或 (b) 按 R-E4 / R-E5 直接去掉那三处（三行、构建 0 警告、全量 1180/2/0 已验证），
   文档随之只留一处。

建议一并处理（不阻断）：B-9 给新 `[Theory]` 加上消息断言；B-10 给
`SelectCharacterPayloadTestData` 的 `NameTagItemId` / `NameTagExpireTime` 不同的非零值；
B-11 对齐 `spec.md` 的 `contract_revision`；B-12 决定 RQ-2 那半句是实现还是撤销并写明理由；
B-13 修 `:413-418` 那个悬在段尾的分号。

### 一句话给执行者

A-1 修得很干净 —— 补回的四条与基线逐条等价到「抛哪一条消息」这一层，新用例是真守卫，
三个孤儿类型删干净了，契约也收口了。挡下这一轮的不是代码，是**你为剩下那几处收窄写进设计权威
文档的理由**：我按你的邀请去证伪它，三处都被证伪了，其中一处（`EquippedCreatureLevel`）
的目标类型压根不是 `byte`，而且那个 cast 是本轮你自己新加的。把那段话改成实际发生的事，
或者把那三处删掉，这一轮就过了。

## 第三轮

评审范围：同一工作树、同一接受基线 `078d762`，改动仍全部未提交（17 个已修改文件 + 两个未跟踪项）。
判据来源同前两轮，另加第二轮 `changes_required` 的唯一必修项 A-2。执行者交办说明里的五项整改
与那一项「未做并说明理由」**一律未采信**，逐项独立重做。

**verdict: passed**

A-2 已按事实闭合，且我独立复核了三处下沉/删除**没有把收窄弄丢**（两处本就是死转换，一处目标类型
根本不窄），保留的那一处目标确为 `byte` 且被一个 `byte` 重载读走。针对本轮**类型加宽**
（`uint`→`long`、`IReadOnlyList<uint>`→`IReadOnlyList<int>`）我专门做了负值探针：
**没有任何负值能一路走到线上而基线拦得住**——两条路径的拦截点、拦截子句、异常消息都与基线逐字相同。
下发帧逐字节不变有三条独立证据。本轮新记五条 B，无一条构成阻塞。

### 我实际执行过的独立实验

每次临时改动后都用 `sha256sum -c` 对 18 个文件（17 个已修改 + `RESUME.md`）逐个校验复原；
全部临时探针文件已删除；结束时 `git status --porcelain` 仍是 17 个 ` M` 加两个未跟踪项，
与评审开始时逐字节一致。收尾又跑了一次构建与全量：**0 警告 0 错误、1180 / 2 / 0**。

#### T-E0 构建与全量（独立复现）

`dotnet build Server/86JP.Server.slnx` = **0 警告 0 错误**。
`dotnet test Server/86JP.Server.slnx -m:1`（两个 PVF 环境变量已设）
= 60 + 530 + 4 + 100 + 46 + 237 + 203 = **1180 通过 / 2 跳过 / 0 失败**，与执行者所报一致。
`Dfo.Server.Host.Tests` 本轮五次满载运行均未出现假红。
（附一句给下一位：`Dfo.Pvf.Tests` 的 60 条排在输出最前，用 `tail` 截日志会把它整块吃掉。）

#### T-E1 A-2 的逐处核实（查收窄的去向，不只是条数）

`CharacterEntryStateServices.cs` 的 `checked((` 计数：基线 **93** 行 / 96 次 → 当前 **27** 行 / 27 次。
按类体边界归属：`CharacterProfileEntryProjection`（`:9-42`）**0 处**；
`CharacterEntryStateDomainService.LoadAsync`（`:277-315`）**0 处**；
其余 27 处为 `:58,59`（技能/任务）、`:111-142`（进度，12 处）、`:156-256`（特性，13 处），
**全部落在四个不在范围内的投影**。第二轮点名漏数的 `:297` 已随 `SpecialRewardQuestIds` 一起消失。
账也对得上：第二轮见到的 30，减去 `VisibilityBits` 两处、减去 `:297` 一处 = 27。

三处去掉的收窄，逐处查「是不是把收窄弄丢了」：

| 去掉的收窄 | 收窄现在在哪 | 值的上游守卫 | 判定 |
|---|---|---|---|
| `checked((uint)storage.Settings.UserStateBits)` ×2 | `SelectCharacterHandler.cs:346` 的 `checked((byte)…)`（**基线原文，本轮未动**）与 `StateSynchronizationPayloadCodec.Entry.cs:750` | `CharacterEntryAssembly.cs:613` `settings.UserStateBits is < 0 or > uint.MaxValue`，与基线 `s0.UserStateBits is < 0 or > uint.MaxValue` **逐字相同** | 原来那一步是**死转换**，删掉无损 |
| `checked((uint)clearFlag.SlotIndex)` | `StateSynchronizationPayloadCodec.Entry.cs:799` 的 `writer.Write(checked((uint)questId))`（**本轮新加**；基线该行直接写 `uint`） | `CharacterEntryAssembly.cs:306` `flag.SlotIndex is < 0 or >= 30_000` 抛 `"Quest clear flag state is out of range."`，与基线第 305/307 行**逐字相同** | 收窄真的挪到了写字节处；原来那一步同样是死转换 |
| `checked((byte)entry.Public.DerivedStats.EquippedCreatureLevel)` | 无需收窄 | 目标 `WorldContentSubject.EquippedCreatureLevel` 声明为 **`int`**（`IWorldEntryServices.cs:41`），唯二读者是 `WorldContentEntryRuleService.cs:131,140` 的两次比较，**不上线** | 该 cast 确为本轮新加且多余；`git diff` 显示删除后该行与基线形状完全一致（基线 `entry.Public.Subtype1.EquippedCreatureLevel` 是 `byte`→`int` 隐式加宽，同样无 cast） |

保留的那一处：`SelectCharacterPreparationService.cs:217`
`checked((byte)selectedEntry.Public.Extras.ManageLevel)`。
目标 `CharacterRosterEntryState.ManageLevel` 确为 **`byte`**
（`Server/src/Dfo.Server.Data/Game/StateSynchronizationModels.cs:71`），
读者 `SupplementalSynchronizationCodec.cs:40` 的 `writer.Write(value.ManageLevel)` 选的正是 byte 重载。
**理由成立。** 该文件 `checked((` 计数基线 13 → 当前 14，`git diff` 证明净增的就是这一处，
`:504` 与 `:210/:211` 都回到或保持基线原样。

#### T-E2 负值探针（临时文件 `ZzR3ProbeTests.cs`，10 例全过，已删除）

不采信执行者用例，另建夹具（character_id 91，account 19）：

1. `user_state_bits=-1` → `InvalidDataException("Character public profile state is out of range.")`。**通过。**
2. `user_state_bits=4294967296` → 同一条消息。**通过。**（上下界两半都在）
3. `manage_level=-1`、`name_tag_item_id=-1`、`name_tag_expire_time=-1`、`guild_power_war=-1`
   → 四条各自 `"Character extended detail state is out of range."`。**四条全部通过。**
4. 负的任务号 `character_invisible_flags.slot_index=-1` →
   `"Quest clear flag state is out of range."`；`slot_index=30000`、`flag_value=-1` 同。**三条全部通过。**
5. 满宽仍装载且值不变：`user_state_bits=4294967295`、`manage_level=255`、两个 name_tag 满 uint、
   `guild_power_war` 满 uint、`slot_index=29999 / flag_value=255` → 装载成功；
   再过一次 `CharacterProfileEntryProjection.Build`，
   `Public.VisibilityBits == 4294967295L`、`Settings.VisibilityBits == 4294967295L`。**通过。**

**结论（回答「负值可表达性」这一问）**：基线与当前的拦截点、拦截子句、异常消息**逐字相同**——
负的 `user_state_bits` 与负的任务号都在 `CharacterEntryAssembly` 装载时被拦，两版一致。
加宽只是让「万一拦不住会在哪里炸」从投影挪到了组帧（`checked((byte)long)` / `checked((uint)int)`
仍会抛），而那条路在生产上到不了。**没有任何负值能一路走到线上而基线拦得住。**

补充两条排除项：(a) 全仓只有 `CharacterEntryStateServices.cs:14` 一处生产侧构造
`CharacterPublicEntryState`，**不存在把线上 `uint` 解码回 `int` / `long` 的路径**，
所以加宽不会让高位值回环成负数；(b) `VisibilityBits` 全仓无比较型读者
（唯一读者是 `SelectCharacterHandler.cs:346` 的 `checked((byte)…)`），
`SpecialRewardQuestIds` 只被 `Count` 与 `foreach` 用到，**加宽没有改变任何比较语义**。

#### T-E3 下发帧逐字节不变（独立重做）

- `EntryLoadCharacterizationTests.cs`：按 `"""` raw literal 整块解析基线与当前，
  各 **215** 条 `key = value`；**值多重集双向差集为空**；`(叶名, 值)` 多重集仅 9 条差异且一一配对，
  全是去前缀改名（`ExpertJobType→Type` 等），值一个没变。与前两轮结论一致。
- `AccountEntryLoadCharacterizationTests.cs`、`InventoryLoadCharacterizationTests.cs`、
  `SelectCharacterAcceptedTranscriptTests.cs`（写死每帧 SHA-256）三个文件**不在 `git status` 里**，
  `git diff` 为空。
- `SelectCharacterPayloadCodecTests.cs` 的 `git diff`（+28 / −18）逐行看过：**零处期望字节改动**，
  全是构造与取值路径；夹具数值与基线一一对应
  （`SpecialRewardQuestIds: [0x0A0B0C0D, 0x11121314]`、`Dimensions` 的 (Key,V1,V2)=(1,2,3)、
  `DimensionFlags` (4,5,6,7)、`ManageLevel: 8` 全部原值；
  `NameTagItemId` / `NameTagExpireTime` 基线取的是记录默认值 0/0，当前显式写 0/0，值未变）。

#### T-E4 六次真改真跑（位置我自己挑，含前两轮未实测的类别）

| # | 改动 | 类别 | 期望 | 实测 |
|---|---|---|---|---|
| T1 | 投影 `CharacterEntryStateServices.cs:32` `storage.Settings.UserStateBits` → `storage.CloneTitleItemId` | 概念读错来源 | 红 | **全量 1180 绿**（见 B-14） |
| T2 | 编解码器 `:799` `checked((uint)questId)` → `checked((ushort)questId)` | 收窄宽度错 | 红 | 红：**11 条**，含 `AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline` |
| T3 | 编解码器 `:781` `writer.Write(checked((byte)stats.EquippedCreatureLevel))` → `writer.Write(stats.EquippedCreatureLevel)` | 收窄丢失 | 红 | 红：1 条 |
| T4 | `RequireProfileInRange` 把 `manageLevel is < 0 or > byte.MaxValue` 从第二个 `if` 挪进第一个 | 拒绝搬错半边 | 红 | 红：**恰好是** `CharacterEntryState_RefusesAValueTooWideForItsFieldWhenItIsLoaded(manage_level=256, Extended)` 那一行 |
| T5 | `RequireProfileInRange:613` 删掉 `settings.UserStateBits is < 0 or > uint.MaxValue` | 拒绝丢失 | 红 | **全量 1180 绿**（见 B-15） |
| T6 | 编解码器 `c.EventCharacter != 0 ? (byte)1 : (byte)0` 真假对调 | 真假转换反向 | 红 | 红：`AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline` |

T1、T5 的全量绿都完整跑了七个测试项目。T4 证明第二轮 B-9 的整改是**真守卫**：
消息断言确实把「落在哪个 `if`」钉住了。T6 把第二轮只能写「构造上确定」的那一类**实测**了。

#### T-E5 B-10 的理由可以被推翻（我给出替代办法并跑通）

先独立复现缺口：把编解码器 `:745` 与 `:774` 两处
`writer.Write(checked((uint)extras.NameTagItemId)); writer.Write(checked((uint)extras.NameTagExpireTime));`
的次序对调 → `Dfo.Server.Protocol.Tests` **203 条一条不红**。缺口属实。

再验证「必须改期望字节」这半句不成立：我写了一条新用例（临时文件 `ZzR3NameTagProbeTests.cs`，
约 50 行），**不动任何共享夹具、不动任何期望字节**——用
`source with { … Extras = … with { NameTagItemId = 0x11223344, NameTagExpireTime = 0x55667788 } }`
就地覆写，遍历 `SelectedCharacterStateMessageFactory.Create(result)` 的每个 `EncodeBody()`，
断言这两个 4 字节值出现在 3 个包体里、且每处 `expiry == itemId + 4`。实测：

- 未改编解码器：**204 条全绿**（原 203 条一条未动，新用例通过）。
- 把编解码器两处对调：**只红新用例这一条**，其余 203 条仍绿。

即：这个取证缺口可以在不碰 C-3 的前提下补上。见 B-16。

#### T-E6 边界、编码与治理工件

- `Server/src/Dfo.Server.Persistence/` 的 `git diff --stat` 为空；`Tool/`、`.worktrees/`
  的 `git status --porcelain` 无命中。
- 14 个已修改 `.cs`：**CRLF 行数 == 总行数**、无 BOM；唯一例外
  `RosterScreenQueryCodecTests.cs` 为 `efbbbf`，基线本就如此，按约束保留。
  `change-review.md` / `spec.md` / 两份 `TaskContract.md` / `DomainInstanceDesign.md`
  亦全部 CRLF、无 BOM。
- 全仓 `Subtype0` / `Subtype1` 搜索：7 处命中，全是 `Dfo.Server.Protocol` 内的私有方法
  `EncodePvpSubtype0`（4 处）与三个测试方法名。**零个记录类型。**
- 第二轮确认删净的三个孤儿类型（`CharacterDimensionEntryState` / `CharacterDimensionFlagsState` /
  `CharacterPvpResultState`）复查仍为**零命中**；本轮的加宽没有产生新孤儿类型。
- 根契约 `revision: 2`、子契约 `revision: 2`、`spec.md` `contract_revision: 2` 三处一致（B-11 关闭）。

### 整改五项 + 一项「未做」的逐条核对

| # | 执行者所述 | 我的判定 | 依据 |
|---|---|---|---|
| 1 | 三处按事实下沉/删除，第四处保留 | **属实，A-2 闭合** | T-E1：三处的收窄或已挪到写字节处、或本就是死转换、或目标根本不窄；保留那处目标确为 `byte` |
| 1b | 「`CharacterProfileEntryProjection` 零收窄，`CharacterEntryStateServices` 93→27，剩 27 全属另四个投影」 | **属实** | T-E1，按类体边界逐处归属 |
| 2 | 设计文档按实测重写，并记下「先给了站不住的理由、被评审用实验推翻」 | **属实** | `DomainInstanceDesign.md:387-400` 与代码逐句对得上；把被推翻这件事写进权威文档是加分项 |
| 3 | B-9 改为断言抛的是哪一条消息 | **属实，是真守卫** | T4：把一条判据挪进另一个 `if`，恰好红对应那一行 |
| 4 | B-11 / B-12：`contract_revision` 1→2；RQ-2 按实际做法订正 | **属实** | 三处 revision 一致；`spec.md` RQ-2 已如实写明「八个字段由 `bool` 退回 `int`，比基线弱了一点自证性，换来的是两族合一」 |
| 5 | B-10 未做，理由是「改输入值必然改期望字节，违反 C-3」 | **半属实** | 缺口属实（我独立复现）；「必须改期望字节」这半句**不成立**，T-E5 给出并跑通了替代办法。降记为 B-16，不阻塞 |

### A 级（必须修）

**无。**

### B 级（本轮新记，均不阻断）

#### B-14 `VisibilityBits` 两处的来源无人钉住，且这个值现在在入场态里存了三份

**文件与行**：`Server/src/Dfo.Server.Application/Characters/CharacterEntryStateServices.cs:32` 与 `:41`。

**可复现构造方式**：T1 —— 把 `:32` 的 `storage.Settings.UserStateBits` 换成
`storage.CloneTitleItemId`（夹具里两者分别是 3 与 44，值不同），**全量 1180 条一条不红**。
原因是本轮新加的投影守卫 `The_profile_projection_hands_every_concept_through_unchanged`
只断言了 15 项，**不含 `VisibilityBits`、`Appearance`、`CloneTitleItemId`、`Identity`、
`CharacterOptionBlob`**；而钉下发帧的那批用例直接构造 `CharacterPublicEntryState`，根本不过投影。

顺带记两条形状上的事：`CharacterSettingsEntryState.VisibilityBits` **全仓零读者**
（`VisibilityBits` 一共只有 3 处命中：两处声明 + `SelectCharacterHandler.cs:346`）；
同一个 `user_state_bits` 现在在入场态上有三份拷贝
（`Public.Settings.UserStateBits`、`Public.VisibilityBits`、`Settings.VisibilityBits`），
其中一份没人读。两者基线同样如此、**不是本轮引入**，但本轮既然把这两行重写了一遍，
零读者的那一份按 CK-7 的口径本可以顺手收掉。
**无行为缺陷**（T-E2 第 5 项证明值原样通过），只是取证与形状上的欠账。

#### B-15 加宽之后，`user_state_bits` 的装载期拒绝成了唯一防线，而它一条用例都没有

**可复现构造方式**：T5 —— 删掉 `CharacterEntryAssembly.cs:613` 那一子句，**全量 1180 绿**。
本轮新增的七行 `[Theory]` 覆盖的是 `stamina` / `mood` / `pvp-rank` / `manage-level` /
`name-tag-item` / `name-tag-expiry` / `guild-power-war`，**没有 `user_state_bits` 这一行**。

之所以值得记：基线在投影上还有一层 `checked((uint)…)` 兜着（虽然是死转换），本轮把它去掉后，
装载期这一子句成了唯一一处会因这个字段超宽或为负而抛的地方，而它恰恰是新用例没盖到的。
补一行 `InlineData` 即可，与既有七行同构。

#### B-16 B-10 不做的理由不成立：有不改期望字节也能钉住次序的办法

见 T-E5。**编解码器侧 NameTag 次序不可区分这件事属实**，但「要让它可区分就必须改输入值、
就必然改期望字节」这个推理漏了一种做法：新写一条用例、用 `with` 就地覆写这两个字段，
不碰 `SelectCharacterPayloadTestData`、不碰任何写死的期望字节或帧 SHA。
我已经跑通（不改编解码器 204 全绿；对调编解码器只红这一条）。
执行者把「不做」写成了「做不到」，这一步与本轮 A-2 被推翻的教训是同一类。
**不阻塞**：缺口本身第一轮就存在、非本轮引入，且「下发帧一字节不变」另有两条硬证据
（帧 SHA 基线未改而全绿、golden 值多重集零差异）。

#### B-17 「四处只剩一处」这句话对读者仍会少算两处

`Docs/Refactor/DomainInstanceDesign.md:392-397` 说得没错（**被争议的那四处**里只剩一处），
但读者容易读成「profile 这条路上的 Application 层收窄只剩一处」。
实际上同一个方法里还有 `SelectCharacterPreparationService.cs:210` 与 `:211`
（`checked((uint)Math.Max(0, entry.Public.DerivedStats.Progress1 / Progress2))`），
读的是 profile 的派生数值、落进名录成员记录的 `uint`。
这两处与基线**逐字相同、非本轮引入**，前两轮也都没把它们算进那四处，所以不改判；
但真要写「还剩几处」，名录投影上是三处而不是一处。

#### B-13（第二轮遗留）文档排版仍未修，且 `--` 又回来一处

`Docs/Refactor/DomainInstanceDesign.md:419`「…still lives in the domain, deliberately;」
后紧跟空行、下一段以 `ItemContainerOperations.ConsumeAsync` 开头，分号仍悬在段尾。
另外第二轮记「` -- ` 全文零命中」，本轮新写的 `:393` 又用了一处 `--`，
而全文其余 67 处破折号都是 `—`。

### 前两轮其余 B 级的处置复核

| ID | 处置 | 我的判断 |
|---|---|---|
| B-1 | 计数与理由本轮都改对了（93→27，零处在 profile 投影） | **关闭** |
| B-2 | 三个孤儿类型已删 | **关闭**（复查零命中） |
| B-3 / B-10 | 投影侧夹具已改 77/88；编解码器侧仍 0/0 | **仍开，维持 B**，续记为 B-16（理由不成立，但缺口不阻塞） |
| B-4 | 七行 `[Theory]` 已补 | **关闭**（T4 证明是真守卫）；另见 B-15 的新缺口 |
| B-5 | 如实记录，未改 | **维持 B**。基线同样如此；T2 / T3 / T6 三次红证明帧 SHA 基线对宽度、丢失、真假三类都不瞎 |
| B-6 | PvP 结果空值守卫仍只在编解码器一侧 | **维持 B**。装配器恒给 `[]`，生产路径构造不出来，编解码器守卫有用例钉着 |
| B-7 | 入场态成员仍叫 `*StorageRecord`，定义点跨两个工程 | **维持 B**。复用是本设计的核心，`<remarks>` 已写明；命名与落位留给下一轮 |
| B-8 | 只修了 `--` | **仍开**，见 B-13（且 `--` 又新增一处） |
| B-9 | 已加消息断言 | **关闭**（T4 实测） |
| B-11 | 三处 revision 已对齐 | **关闭** |
| B-12 | RQ-2 已按实际做法订正并写明代价 | **关闭** |

**前两轮其余 B 级里没有一条需要升级为阻塞。** 本轮新记的四条也都不阻塞：
B-14 / B-15 / B-16 是取证缺口（无行为缺陷，且其中两条基线同样存在），B-17 / B-13 是文档表述与排版。

### SC 逐条核对

| ID | 判定 | 依据 |
|---|---|---|
| SC-C1 | **满足** | 全仓 `Subtype0` / `Subtype1` 共 7 处命中，全是 `Dfo.Server.Protocol` 内私有方法 `EncodePvpSubtype0`（4）与测试方法名（3）。**零个记录类型**；三个孤儿类型复查仍为零命中 |
| SC-C2 | **满足** | `CharacterProfileStorageRecord` 成员逐个可指出概念；装配器无按段拍平的方法。第一轮的 78 字段脚本比对我未重做——本轮整改未触及那一段，且 T-E3 的三条证据（golden 值多重集零差异、两份表征用例 diff 为空、帧 SHA 基线未改而全绿）共同指向它没变 |
| SC-C3 | **满足** | (a) 超宽拒绝已补回并有用例钉住，T4 证明是真守卫；(b) profile 快照这条路上的 Application 层收窄**已清零**——`CharacterProfileEntryProjection` 与 `LoadAsync` 各 0 处；(c) 残留的 `SelectCharacterPreparationService.cs:210 / 211 / 217` 属**名录投影**，其中 `:217` 被 `CharacterRosterEntryState.ManageLevel`（`byte`）逼窄、另两处与基线逐字相同，都不在本成果范围内且现已被文档如实记下。第二轮判「部分满足」的两半（理由不属实、profile 投影自己还有收窄）**都已闭合** |
| SC-C4 | **满足** | 全仓无 `.Subtype0` / `.Subtype1` 取值；`SelectCharacterPreparationService` 与 `SelectCharacterHandler` 全部经 `DerivedStats` / `Extras` / `Pvp` 按名字读 |
| SC-C5 | **满足** | 帧 SHA-256 基线用例未改而全绿；协议编解码用例期望字节零改动；两份 golden `git diff` 为空；第三份 golden 值多重集零差异（T-E3）。关于 `EntryLoadCharacterizationTests` 的 `Expected` 被重写，沿用第一轮按 C-2 与 plan T-001 的判定 |
| SC-C6 | **满足** | 构建 0 警告 0 错误；全量 1180 / 2 / 0（T-E0，删除探针后复跑确认） |
| SC-C7 | **满足** | 本成果新建立的保证现在四类都可证伪且我亲手跑红：收窄丢失 = T3、收窄宽度错 = T2、拒绝搬错半边 = T4、真假转换反向 = T6（这一类第二轮只能写「构造上确定」，本轮实测）；概念取错 = 第二轮 R-E6 的 NameTag 对调（夹具 77/88 起了作用）。残留两处不可证伪点：编解码器侧 NameTag 次序（B-16，已给出可行补法）与 `VisibilityBits` 的来源（B-14），均为 B |

### AC 逐条核对

| ID | 判定 | 依据 |
|---|---|---|
| AC-1 | 满足 | 同 SC-C2 |
| AC-2 | 满足 | `CharacterPublicEntryState` 七个成员各可指出概念；`Extras` 的 `<remarks>` 承认它是随行杂项。RQ-2 的「0 / 非 0 变真假」已在 spec 里如实改判并写明代价（B-12 关闭），不再是暗账 |
| AC-3 | **满足（按范围口径）** | 「`Subtype0` / `Subtype1` 命名的记录类型零命中」成立；「收窄语句全部在 `Dfo.Server.Protocol`」在**本成果的主语**（profile 快照那条路）上现已成立——那段 45 字段按位复制里的每一次收窄都进了 `writer.Write(...)`。字面上仍有反例（另四个投影 27 处、名录投影 3 处），但那是范围外的遗留，设计文档已把它们明写为遗留项，前两轮亦按同一口径判定 |
| AC-4 | 满足 | 同 SC-C4 |
| AC-5 | 满足 | 同 SC-C5 |
| AC-6 | 满足 | 构建 0 警告 0 错误 |
| AC-7 | **满足** | 四类演示我全部独立跑红：字段错位 / 拒绝搬错半边 = T4、收窄丢失 = T3、真假转换反向 = T6、装配取错概念 = 第二轮 R-E6（本轮我另跑 T1，发现同类的一处**不可证伪**，记为 B-14）。另加 T2 的收窄宽度错 |

### 超范围检查

- 下发帧字节与字段次序：未变（T-E3 三条证据 + T2 / T3 / T6 三次红反证帧 SHA 基线是活的）。
- 物品侧与账号侧快照：两份表征用例 `git diff` 为空，相关目录无改动。
- 持久层形状：`Dfo.Server.Persistence` 的 `git diff --stat` 为空。
- 客户端错误码：无改动；`SelectCharacterHandler` 的改动只在取值路径与一处新增的
  `checked((uint)…)`，而该文件属 `Dfo.Server.Protocol`，收窄落在这里是对的。
- `Tool/`、`.worktrees/`：无命中。
- 契约与 spec 的措辞漂移：根契约、子契约、`spec.md` 三处 revision 已一致，第一、二轮指出的这一点**已收口**。

### 建议一并处理（均不阻断）

1. **B-15**：给新 `[Theory]` 补一行 `user_state_bits` 的 `InlineData`（与既有七行同构，一行的事）。
2. **B-16**：按 T-E5 的做法补一条 NameTag 次序用例——不碰共享夹具、不碰任何期望字节。
3. **B-14**：给投影守卫补上 `VisibilityBits`（以及 `Appearance` / `CloneTitleItemId` / `Identity`）
   的断言；顺带考虑收掉零读者的 `CharacterSettingsEntryState.VisibilityBits`。
4. **B-17 / B-13**：文档里「还剩几处」的说法补一句「名录投影上其实是三处」；修 `:419` 悬在段尾的
   分号，把 `:393` 的 `--` 改回 `—`。

### 一句话给执行者

A-2 修得干净且诚实——该下沉的下沉了、该删的删了，保留的那一处我查到了 `byte` 声明与那个 `byte`
重载，理由站得住；把「先给了站不住的理由、被实验推翻」这件事写进设计权威文档，比改对代码更值钱。
我另外替你把最担心的方向查了：`uint`→`long`、`uint`→`int` 的加宽**没有**放任何负值上线，
两条路径的拦截子句与基线逐字相同。唯一想提醒的是第 5 条——你写的是「必须改期望字节，所以做不到」，
我用 50 行写了个不改任何期望字节的版本并跑通了；下次把「不做」和「做不到」分开写，
就不会再有第二个 A-2。
