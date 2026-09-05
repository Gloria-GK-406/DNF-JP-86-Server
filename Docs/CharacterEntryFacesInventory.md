# 会话大聚合剩下三块的清点

调查日期 2026-09-04，基准 `main` @ `51cd99c2`。**只读调查，未改动任何代码。**

## 这份清点回答什么

上一轮 `character-state-finds-its-owner` 收尾时留下一句话：`CharacterEntry` 的
`Public` / `Progression` / `Features` 三块「内容未查清，完成这部分后再看剩余的怎么处理」。
本文就是那次未查清的清点。它要回答的是三个问题，不是资料汇编：

1. 这三块里有没有**整个是死的**东西（像疲劳那样）？
2. 有没有**有主人但读陈快照**的缺陷（像「升级后 SP 不动」那样）？
3. 会话运行态大聚合 `SelectCharacterResult` 能不能退役？还差什么？

**它不回答**：这些帧该不该继续发（那要对着 A21 抓包定，本次没有抓包）；也不给实现方案。

## 头等结论（三条）

### 一、找到了第二个「陈快照」缺陷，而且对外可见

上一轮的清点结论是「**没有第二个『升级后 SP 不动』那样的缺陷等着修**」。
**这个结论被证伪了。**

带死宠进自由决斗频道时，**大厅里其他人看到的是死宠，而且永远不会刷新**。

逐行核过的顺序（`Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterHandler.cs`）：

| 行 | 做什么 |
|---:|---|
| `:272` | `PublishSelectedCharacterSessionCommand` —— 把**未复活**的聚合放进运行态目录 |
| `:279` | `PvpLobbySnapshotMessage` 发给自己 |
| `:283` | `MarkSelectedCharacterPvpReadyCommand` |
| `:293` | **`PvpParticipantPublished` 广播给大厅所有同伴** |
| `:328` | **`ReviveSelectedCharacterPetCommand` —— 到这里才复活** |
| `:338` | `PetRevivalStateMessage` **只发给自己** |

三个环节都已核实：

- 宠物死活**真的进线**：`StateSynchronizationPayloadCodec.Entry.cs:1009`
  是 `writer.Write(pet?.Field04 > 0 ? (byte)1 : (byte)0)`
- 广播读的是**快照**不是领域服务：`SelectCharacterLifecycleServices.cs:230`
  取 `result.CharacterEntry.Features.Pets`
- `PvpParticipantPublished` **全仓只有一个发布点**（`SelectCharacterHandler.cs:293`），入场后永不重发

复活命令（`SelectCharacterPostEntryServices.cs:118-148`）把 `field04` 落库为 1、
把聚合 `with` 回去、`ReplaceRuntimeExact` 更新运行态目录——但那三件事发生在广播**之后**。

**这个顺序是当初明确定下来的，不是排错了。** `248abac2`（2026-08-12，
`feat(entry): integrate G7 lifecycle`）的 spec 原文写着交付次序是
「… publication/presence, **PvP handshake/publication**, X-11, conditional X-12,
**adjacent X-13/X-14**, then ordered X-15/X-16」——X-09/X-10 是 PvP 公示，
X-13/X-14 是「装备槽 24 的 creature 从死转活」。帧序是对着 A21 抓包逐帧定的。

**所以根因不是顺序，是取数来源**：PvP 身份帧读的是入场快照，而不是问宠物的主人。
`SelectCharacterPetEntryService` 自 `fa77c721`（2026-08-20，
`人物持久化收敛到与物品相同的存储协议`）起就是个真的领域服务，读写都对着
`ICharacterDomainStore.Creatures`——但 `CreatePvpParticipant` 从来没问过它。

**这个区分决定了修法**：把复活提前**不是选项**（会动已经对着抓包定死的帧序）。
要动的是 `CreatePvpParticipant` 的取数来源，或者让复活之后补一次公示。

**次生窗口**：`:272`–`:328` 之间若有别人进同一大厅，其 `PlanPvp`
（`SelectCharacterLifecycleServices.cs:156`）经 `runtimes.GetRequired(owner)`
读到的是这个玩家尚未复活的聚合。

**这不是通知服务引入的。** `:293` 那次广播正是 `notifications-know-their-audience`
迁移的两处扇出之一，但那次迁移被证明**帧逐值不变**。缺陷比它早，搬家时原样带过去了。

半个主人已经在正确的位置：`SelectCharacterPetEntryService`
（`SelectCharacterPostEntryServices.cs:87-148`）是真的领域服务，读写都对着
`ICharacterDomainStore.Creatures`。**问题是别人还在读快照。**

### 二、三块加起来，绝大多数字段没有写入者——那是**系统没建**，不是东西多余

**这一节曾经写反过，必须记下来。**

初版的结论是「绝大多数字段是死的，最大的一桶是删」。所有者 2026-09-04 否掉了这个方向：

> 「本身这些内容都是游戏中存在的。所以比起删除更要做的是一个块一个块的攻坚，完成系统构建。
> 也就是全部按领域一个一个重建领域服务和相关的持久层。和事实不一致的就改，一致的就保留。」

**为什么初版错了**：「服务器上没有写入者」是事实，但它推不出「这东西不该存在」。
邮箱、成就、每日挑战、专家职业在游戏里都真实存在——那些表和 wire 编码不是凭空生成的假象，
是**从客户端逆出来的真形状**。删掉等于把已付的逆向成本扔掉，后面重新逆一遍；
省下的时间要还回去。

**还有一层更重的代价**：这些表和帧是今天**唯一记着「这个系统长什么样」的地方**。
删了之后重建，连形状都要重新猜。

所以正确的轴不是「删 / 留 / 建」，是**按块攻坚**。每一块的缺口分三类：

| 缺口 | 判别 | 该做什么 |
|---|---|---|
| **持久层缺失** | 连数据库列和存储记录字段都没有，值是投影里的字面量 | 建表、建列、建行网关 |
| **写入者缺失** | 有真表真列、投影忠实，缺的只是往回写的路径 | 建领域服务与写入通路 |
| **与事实不一致** | 冗余副本、双份读取、恒不重算、哑弹 | **改**——这是唯一「减」的一类 |

按这个标准，三块的定性见下面的总表。**表里说「死」的一律指「今天没有写入者」，
不是「该删」。**

### 三、清点完也退不掉大聚合，而且差得远

`SelectCharacterResult` 今天九个字段，被 15 个文件引用（12 个在 Protocol 层）：

| 字段 | 读点 | 状态 |
|---|---:|---|
| `Character` | 27 | 上一轮处理完 |
| **`CharacterEntry`** | **58** | **本次清点对象** |
| `World` | 25 | **未清点** |
| `CharacterInventory` | 10 | **未清点** |
| `Account` | 10 | **未清点** |
| `AccountInventory` | 5 | **未清点** |
| `Session` | 2 | — |
| `RosterEntry` | 1 | — |
| `Roster` | 0 生产 / 1 判据 | 带着走但没人读 |

即便这三块全部归位，**大聚合仍退不掉**——另有 `World` 25 处、`CharacterInventory` 10 处、
`Account` 10 处未清点。**不要把这份清点读成「清完就能拆」。**

顺带一条更正：上一轮记的读点数 **15 / 11 / 8 已过时**，今天是
**Public 26 / Progression 11 / Features 15**（Public 那 26 行里有 1 行是异常消息里的
字符串字面量 `"CharacterEntry.Public.Identity"`，不是读取，故实为 25 读）。

## 总表

### `CharacterEntry.Public`（11 字段）

| 字段 | 定性 | 依据 |
|---|---|---|
| `Settings` | **零读点** | 值以 `Public.VisibilityBits` 与 `CharacterSettingsEntryState.VisibilityBits` 两份副本活着 |
| `Pvp` | **零读点**，且 `Results` 硬编码 `[]`（`CharacterEntryAssembly.cs:164`） | 表里根本没有战绩列 |
| `Guild` | **零读点** | 有列有默认值，编码路径为零 |
| `Extras.DimensionFlags` | **零读点**，还带一段为死值抛异常的校验 | 已被 A21 常量块取代 |
| `Extras.SpecialRewardQuestIds` | **零读点，且唯一带 I/O 成本的死代码** | `CharacterEntryStateServices.cs:280-287` 对每个已清任务 `await questDefinitions.FindAsync` |
| `Condition`（37 字段） | 32 个零读点；上线的 5 个恒为建表默认值 | INSERT 只写 `(character_id, pc_room_state)` |
| `DerivedStats`（27 字段） | **不是死，是冻**：创建时的 1 级基础值，一辈子不重算 | 唯一写入是创建时 `InsertIfAbsentAsync`；`character_stats` 的两条 UPDATE 只动 `carried_gold` |
| `Appearance` | 冻在创建时 | `appearance_blob` 无 UPDATE |
| `VisibilityBits` | 恒为默认值 3，不可改 | `CharacterSettingsDomainService` 整个是只读的 |
| `CloneTitleItemId` | 读点最多，值恒 0 | 无穿戴称号路径 |
| `Identity` | 恒 (0,0,0)；可空 + `throw Missing` 守卫永不触发 | 生产路径无条件构造 |
| **`Extras.CharacterExperience`** | **活的，但是冗余副本** | 见下 |

### `CharacterEntry.Progression`（17 字段）

**16 个死，1 个是重复副本。** 整条子记录**会话内从不被替换**（6 处
`CharacterEntry with` 无一触及它），它的每个真源在服务器上也从不被写。

| 字段 | 定性 |
|---|---|
| `FatigueAccelerationValue` / `State` | **无列**——存储记录里没有这两个字段，`CharacterEntryStateServices.cs:107-108` 两行就是字面量 `0,` `0,` |
| `PremiumUsageSlots` | 生产消费者**为零**；对应 `0x036F` 帧被刻意不发；活着靠 4 处判据 |
| `DailyChallengeClaimFlags` | **冗余**，投影从 `DailyChallenges` 现算，同一信息还以 `RewardClaimed` 存在 |
| `DailyChallengeCharacterLevel` | **等级的入场副本**，会话内会发散，但 `0x0286` 入场后不重发，对客户端不可见 |
| `ScheduleCompatibility.SlotIndex` | 投影按排序后 index 重赋值，编码器根本不写它 |
| `GrowthWeaponStages`、`Cube*`、`ChampionBreak*`、`GoldLimitUpgradeLevel`、`DailyReviveCoinClaimed`、`DailyChallenges`、`DailyChallengeTailIds`、`ScheduleCompatibility`、`Dungeons` | **有列无主**，留快照 |

`GoldLimitUpgradeLevel` 是九个「有列无主」里**离活最近的**：已有
`IGoldLimitRuleService`、四档阈值逻辑与专门判据，差的只是一个写入者。

### `CharacterEntry.Features`（5 字段）

| 字段 | 定性 |
|---|---|
| **`Mailbox`** | **彻底死。全仓没有任何邮件协议**——没有 handler、没有 command、没有 opcode |
| `PvpMissions` | 死。只读表，创角不种行；本仓 PVP 有大厅无结算 |
| `Collections.Achievements` | 死。表只有一条 SELECT |
| `Collections.TitleBooks` | 结构上不死、实际不可达：它其实是 `items` 表容器 19-23 的投影，而唯一的物品移动切片只认 `Main` / `Equipment` |
| `Collections.CollectionBoxes` | 进度死、协议壳活（壳来自内容档与当前时间） |
| `ExpertJob` | **双份**：与 `Public.ExpertJob` 是同一张表同一行，**同一次入场读了两遍**（`CharacterEntryAssembly.cs:150` 与 `:470`），而 `Features.ExpertJob.Experience` 从不上线 |
| **`Pets`** | **唯一活的，也是唯一有缺陷的**（见头等结论一） |

## 两处该合并的重复

### 经验：一个数在聚合里有两份，靠四段手写代码保持一致

`Character.Progression.Experience` 与 `Public.Extras.CharacterExperience` 是同一个数。
四处 `with` **在同一个对象初始化器里同时写这两份**，例如
`DungeonClear/DungeonClearSlice.cs:717-733`：

```csharp
result = result with
{
    Character = result.Character with { Progression = new CharacterProgressionState(level, total) },
    CharacterEntry = result.CharacterEntry with
    {
        Public = result.CharacterEntry.Public with
        {
            Extras = result.CharacterEntry.Public.Extras with { CharacterExperience = total },
        },
    },
};
```

另三处在 `MonsterDeathSlice.cs:459`、`FinishQuestHandler.cs:122`、`TutorialLevelUpSlice.cs:220`。

**这四段代码存在的唯一目的，就是维持一个冗余副本的一致性。** 删掉副本、让编码器读
`Character.Progression.Experience`，四段同步逻辑一起消失，未来第五条经验路径也不再有
「忘了同步」的机会。

注意：**今天它没有出缺陷**。`CharacterExperienceDomainService.cs:39-40` 每次都先从库读、
不吃入参快照——那正是原型 bug 的修法。四处 `with` 只负责让已发出去的帧不倒退。
所以这是**降低将来出错的机会**，不是修一个现有缺陷。

### 专家职业：同一行读两遍，其中一份的经验从不上线

`Public.ExpertJob`（`Type`/`Experience`，进 subtype-0 尾块偏移 +23/+24）与
`Features.ExpertJob`（七元组，进 NOTI `00CD`）读同一张表的同一行。
`Features.ExpertJob.Experience` 在 `EncodeExpert` 里**从头到尾没被写过一次**。

## 一颗哑弹

`CharacterExpertJobEntryState.Protocol`（`CharacterModels.cs:494`）在全部生产代码里
**从未被赋值**（`CharacterEntryStateServices.cs:190-197` 只传 7 个参数），
而 `EncodeExpert`（`StateSynchronizationPayloadCodec.Entry.cs:573`）在
`ProfessionType == 1` 时抛 `Missing("ExpertJob.Protocol")`。

今天不炸，只因为 `expert_job_type` 没有写入通路、恒为 0。
**只要库里导入一行 `expert_job_type=1`，该角色入场直接失败。**

## 一个必须守住的区分

恒 0 的 `Progress1`/`Progress2`/`CloneTitleItemId`/`Identity`、冻在 1 级的 `DerivedStats`、
冻在创建时的 `Appearance`、永远领不掉的邮箱金币——**玩家全都看得见**。

但那是**从未实现写入**，不是**写了没同步**。

同理，副本许可 `Dungeons` 不是陈数据缺陷：通关根本不写
`character_dungeon_permissions`（那张表全仓只有 SELECT），**「真源被改」这一半不存在**。

把功能缺失说成陈数据，会盖掉真正的问题。本仓记过这个方向的教训。

## 攻坚清单：按块列出，每块缺什么

所有者定的方向是**一个块一个块攻坚，重建领域服务与持久层**。下表把这份清点的事实
折成块，**缺口类型用上面头等结论二的三分法**。这不是计划，是待裁定的候选表。

| 块 | 今天已有 | 缺口 |
|---|---|---|
| **疲劳** | 占位领域服务、进本调用点、拒绝码 `0x16`、变异验证证明调用点会红 | 持久层缺失（无列）＋写入者。上限规则、6 点期号边界 |
| **属性 `DerivedStats`** | 27 列真表，创建时写 1 级基础值 | 写入者：随等级/装备/技能重算的通路 |
| **专家职业** | 两张表（主行＋配方）、完整 wire | 写入者；**并改**双份读取与 `Protocol` 哑弹 |
| **邮箱** | 4 张表、分页加载、约 140 行 wire | 写入者＋**全部协议**（收信/读信/领附件/领金币） |
| **成就** | 只读表、帧 | 写入者＋推进点（任务完成、怪物击杀） |
| **每日挑战** | 3 张表、帧 | 写入者＋领取协议＋期号边界（与疲劳可能共享） |
| **副本许可** | 只读表、帧 | 写入者（通关时写 `clear_state`）＋入场后重发 `0x0005` 的通路 |
| **PVP 战绩与任务** | 大厅、就绪名单、房间快照、身份广播；只读表 | 对战结算。`Results` 今天硬编码 `[]` |
| **宠物** | **已有领域服务**，写 `field04` | 其余 8 列的写入者；**并改** PvP 身份帧改问主人（见头等结论一） |
| **称号书** | `items` 容器 19-23 | 写入者：`MoveItemSpace` 今天只认 `Main`/`Equipment` |
| **收集箱** | 内容档驱动的箱子集合、协议壳 | 写入者：收集通路 |
| **成长武器 / 魔方 / 冠军突破 / 金币上限** | 有列有帧；金币上限还有 `IGoldLimitRuleService` 与四档阈值 | 各自的写入者 |
| **外观** | 创建时的 blob | 写入者：换装后重算写回（今天世界内形象与角色列表形象两个来源） |
| **可见位** | 只读领域服务 | 写入者；**并改**三份副本 |
| **公会** | 两列 | 整个系统 |

**排序上的一条观察**（不是决定）：**疲劳是最 ready 的一块**——设计已由所有者裁定
（存 used 不存 remaining、上限＝基础 156 ＋有效期内权益现算、惰性期号不用定时器）、
seam 已在、拒绝码已在、调用点已有变异验证钉着。而且它要立的「6 点期号边界」这个形状，
每日挑战、每日复活币都要用，**每日补货那条 UTC+8 0 点边界不得因此改动**。

**宠物那条缺陷体量最小**，可以单独做，也可以搭在任一块里顺手修。

## 未查清的

1. **仓外写入者**。「无写入通路」的结论限定在 `Server/src` 的 C# 代码。若存在导入脚本或
   直接灌库，这些表里可能有非空数据——不改变「运行时永不变化」的结论，
   但会影响「删字段」的取舍，尤其上面那颗哑弹。
2. **A21 参考帧内容**没有核对。因此本文只谈**字段归属**，不谈帧的去留。
   删字段不等于删帧——删掉聚合字段后编码器应写常量。
3. **判据依赖面**没有全量清点，`Server/tests/Integration` 完全没看。
   所以「删掉零读点字段要改多少判据」给不出准确数字。
4. **修法未定。** 「把复活提前」已被排除（帧序对着抓包定死，见头等结论一），
   但「让 PvP 身份帧问宠物领域服务」与「复活后补一次公示」两条路都没有验证过
   ——前者要确认那时宠物是否已经复活（今天不是），后者要确认 A21 允许重发。
5. **`ReplaceExact` 的并发语义**未读实现。若它不是 compare-and-swap，
   击杀与通关同时结算可能互相覆盖——那是聚合级问题，超出本次范围。
