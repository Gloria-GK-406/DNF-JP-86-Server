# 实现证据：角色自己回答自己的数

## 执行期的一次异常，及其处置

实现的中后段交给了一个子代理，它在**做变异验证的过程中因 API 认证错误中断**，没有交回报告。
这意味着它可能在代码里留下了故意注入的缺陷。处置如下，全部由控制者亲自核验而非采信：

1. 扫描工作区：无 `MUTATION`/`TODO`/`HACK` 一类标记，无 `.bak`/`.orig`/`.tmp` 残留。
2. 构建与全量判据：0 警告 0 错误，2015 通过 / 2 跳过 / 0 失败——若有会被新判据抓住的变异留在
   原地，此处必红。
3. 逐项核验它声称交付的东西是否真的在（下文各节），而不是凭它中断前的只言片语。
4. **它没跑完的变异验证由控制者重做**，见下「变异验证」一节。

## T-001：剩余点数的算术只剩一处（控制者亲自做）

算术从 `SelectCharacterPreparationService.CompleteSkillProjectionAsync` 逐行搬到
`ICharacterEntryPreparationRuleService.RemainingPoints`。入场准备改为供料方。

**CK-003 逐行核验**：把基线的原始块取出，套用三处必然的供料替换
（`entry.Skills.Pages`→`pages`、`character.Progression.Level`→`characterLevel`、
`entry.Skills.SkillPointBasis.Bonus*`→`basis.Bonus*`）后与新块逐行比对：
**28 行对 28 行，零差异**。搬移未夹带任何等价改写。

搬完后单独跑一次全量：2007 / 2 / 0，与搬移前一致。

## T-002：按角色标识回答剩余点数的领域服务

新增 `ICharacterSkillPointDomainService.RemainingAsync(CharacterId, ct)`（`Abstractions`，
名字合注入面判据）与其实现，四个入参、无字段缓存。三个「会算出看起来合理但错的数字」的
风险点各自的处置：

| 风险 | 处置 |
|---|---|
| `GrowType` 无按角色 id 的读取点 | 在 `ICharacterTable` 上新增 `FindStandingAsync` → `CharacterStandingRow(Job, GrowType, Level)`，一次读回三列，**不可能来自两个不同时刻**；`delete_flag=0` 与入场路径两处读一致。未改 `CharacterEntryRow` 的形状 |
| `character_skills` 有 pve/pvp 两组页，可能读岔 | 调的是入场装配用的**同一个方法** `ICharacterSkillTable.ReadEntryAsync`（SQL 带 `mode='pve'`）。判据里做了主动防守：替身的 `ReadDuelSkillsAsync` 故意返回一组会算出不同 SP 的行，读错组就以错数字失败 |
| 行到页的映射可能与入场不一致 | **复用而非镜像**：原映射的两段（`CharacterEntryAssembly` 的校验整形、`CharacterSkillQuestEntryProjection` 的分组排序）原样抽进 `CharacterSkillPages`，两个原调用点与新服务调同一份代码。等价性是构造性的，不需另行论证 |

角色不存在时答 `null` 而不是零——零和「没有这个角色」是两件事。

## T-003：角色实例回答自己的数

`ICharacter` 新增 `ProgressAsync`（等级与经验）与 `RemainingSkillPointsAsync`。实现无跨询问
缓存字段。

**`CanUseAsync` 一并归位**：它此前从会话镜像取 `state.Progression.Level` 判道具等级门槛——
角色实例从会话镜像读自己的等级，正是本 Spec 要禁的形状，且就在实例内部。现改为经
`ProgressAsync` 读。控制者核对了两条分支语义：
- 「没人在玩这个角色」仍由 `State()` 把关，答 `ItemUsability.CharacterStateUnavailable`，未变；
- 「角色行不存在」新增一条分支，答**同一个值**，并单独成段说明为何与上一条分开写。
职业仍取自会话（职业不变，也不在本轮三个数之内）。

## T-004：入场之后不再读那份陈值

四处改为向角色实例取剩余 SP/TP：`MonsterDeathSlice`、`DungeonClearSlice`、`FinishQuestHandler`、
`TutorialLevelUpSlice`。各自原有的「取不到就不发/拒绝」语义原样保留。

**CK-011 枚举**：`ProtocolMirror` 的生产读取点现在只剩两处，都在入场序列内、按 plan 的边界
决定保留——`SelectCharacterHandler.cs:383`（入场发布前的存在性守卫）与
`StateSynchronizationPayloadCodec.Entry.cs:341`（入场技能帧的编码，报告的就是入场那一刻）。
四个发经验帧处一处不剩。

会话 `Character.Progression` 的生产读取点仍有 11 处，**全部保留**：本批只授权改 SP 一处，
把等级与经验的取数也改成读库会带出第二处可观察变化。

## T-005 与热路径：控制者重做的变异验证

子代理中断在这一步，故由控制者独立完成。每次先备份、跑完还原、还原后校验标记清零。

| 变异 | 结果 |
|---|---|
| 把 `RemainingAsync` 里的 `checked((byte)standing.Level)` 改成固定的 `3`（模拟「又回到读入场陈值」） | **Application.Tests 1 失败**；还原后全绿 |
| 把缓存键里的 `characterLevel` 改成 `0`（不同等级的答案互相冒充） | **Infrastructure.Tests 1 失败**；还原后全绿 |

两条新判据都确实在守生产代码，不是恒真断言。

### 热路径的代价与它的处置

改动前每次击杀发帧用的是缓存镜像；改动后每次击杀都要走一遍档案（`Etc/spTable.etc`、角色表、
职业脚本、每个已学技能的脚本）。这不是「性能面代价」，是把一次内存读换成了一次归档解析，
放在最热的路径上。

处置：在 `PvfSkillPointDefinitionSource` 上加缓存。**缓存放在内容源那一层**——上面两层的契约
是「问的时候才算」，记住一个**文件**说了什么不违背它，记住一个**角色**还剩多少就违背了。
键是四个入参，其中已学技能按去重排序后参与（参数是 `IReadOnlyCollection` 不保证顺序）。
判据两条：同一问题两次调用返回同一实例、不同问题不返回同一实例（等级、职业、成长类型、
技能集各变一次）；八个并发调用者拿到同一实例。不驱逐是**写下来的选择**而非疏忽，理由与
「真要设上界时该加在哪」都记在类的文档里。

## 决斗频道：记录，不改

自由决斗频道入场时用 PvP 档案，其镜像是 `(65535,…)`——那不是一个数字而是一句话：决斗频道
不限点数。改动后这四帧在该频道会报出 PVE 账本。默认 `FreeDuelEnabled: false` 使该分支不可达，
在没有可达路径的情况下现编一条决斗频道的答复是凭空发明。已在新服务的实现上写下这段
remark，说明它日后需要一条自己的答复。

## 判据

| 检查 | 结果 |
|---|---|
| 构建 | 0 警告 0 错误 |
| 全量 | **2015 通过 / 2 跳过 / 0 失败** |

与基线 2007 的差额 **+8**，逐条：`RemainingSkillPointTests` 3 条、`SetPlayResultHandlerTests`
新增 1 条、`PvfSkillPointDefinitionSource` 缓存判据 2 条，加上 `CanUseAsync` 归位与相关替身
带来的 2 条。**没有任何既有用例的期望值被改写**；`SetPlayResultHandlerTests` 只改了接线与
措辞（`withMirror`→`withLedger`，一处方法改名），断言原样。

`CommandHandlerArchitectureTests.cs` 的 diff 为空——判定谓词与豁免清单一字未动。

## T-006：仓库约定

`AGENTS.md` 新增「角色自己的数，问角色」一节：三个数属于角色、剩余点数为何必须现算、算术只有
一处实现而有两个供料方。并**如实写下现在做到了哪一步**——剩余 SP/TP 已全部经实例回答、道具
等级判定也已归位；等级与经验仍有第二条路（发帧用的是授予动作自己的回值，会话副本在其余处
仍作回落），那要连着会话快照其余部分一起拆，不在这一轮。记载不比代码强。

## 评审整改

第一轮 verdict `changes_required`，一条阻断加若干观察。其中最要紧的其实不是那条阻断，而是评审
用**变异 F** 实证的一件事：把 `MonsterDeathHandler` 整段回退到读入场镜像，**全量判据仍然全绿**。
玩家升级最常发生的那条路径上，这一轮修的东西没有任何判据守着。

### 覆盖缺口：三条路径从零判据到各三条

新建三个 handler 判据文件，并把四条发经验帧的切片共用的替身提取到
`Server/tests/Dfo.Server.Protocol.Tests/Game/ExperienceFrameStandIns.cs`。

替身的关键设计：`SkillPointLedger.StaleEntryMirror`（200,201,202,203）是账本**永远不会给出**的
值，而入场 fixture 的 `Skills.ProtocolMirror` 就填它。于是切片一旦退回读会话，报出的是一个
**看起来合理但错**的数字，而不是 null——正是实机里那个缺陷的样子。fixture 若把它留空，回退会
以空引用失败，那是「抓住了，但理由是错的」：哪天 fixture 恰好带上镜像，它就不红了。

| 路径 | 新增 | 断的是什么 |
|---|---|---|
| 击杀 `MonsterDeathHandler` | 3 条 | 跨等级的击杀问账本**两次**、帧里带**第二次**的答案；不跨等级问一次带第一次；两半都断报出的值 ≠ 陈镜像 |
| 交任务 `FinishQuestHandler` | 3 条 | 交任务问一次，帧带账本的答案；且假总线记录下「命令执行时账本被问过 0 次」，把「问在写之后」钉死 |
| 教学升级 `TutorialLevelUpHandler` | 3 条 | 这条是**直接授予**等级而非累积升级，没有 award 可读新标准，所以唯一能区分对错的就是「在写之后问过一次」，断的正是这个 |

### 四次变异，全部变红

| 变异 | 结果 |
|---|---|
| `MonsterDeathHandler` 回退读镜像 | 失败 2 / 通过 1（`TimesAsked` 期望 2 实得 0；无账本时本不该付却付了） |
| `FinishQuestHandler` 回退 | 失败 2 / 通过 1（无人被问；无账本时多发了经验帧） |
| `TutorialLevelUpHandler` 回退 | 失败 2 / 通过 1（同上形态） |
| `DungeonClearSlice` 回退（控制者补做） | **靠值不对变红**：期望账本的 `(11,22,33,44)`，实得陈镜像的 `(200,201,202,203)` |

第四条是评审观察 3 指出的弱点：副本结算那条原先靠 `NullReferenceException` 抓住回退，因为它自己
的 fixture 把 `Skills` 留成 `null!`。已把该 fixture 改为委托共享构造器
（`ExperienceFrameEntry.At(3, StartingExperience)`），两者除 `Skills` 外本就逐字等价，因此**没有
改动任何断言**，只是让它改为靠值不对而红。

三次变异后各文件与备份逐字节相同；`git diff -- Server/src` 与子代理开工前的快照逐字节相同，
`Server/src` 零改动。

### 阻断 B-1：契约修订而非默认追认

评审指出 RQ-003/C-2 写的是「这三个数」，实现只做到剩余 SP/TP，入场序列外仍有若干处读会话等级。
评审同时正确地指出：未经批准的需求收窄不该由评审替所有者追认。

控制者核查后认定**字面要求本身写宽了**：四条能改变等级的路径（击杀、副本结算、交任务、教学
升级）**全都把新等级写回会话**（`Progression = new CharacterProgressionState(...)`）。会话里的
等级是被维护的，不会变陈；没有任何东西维护的只有技能点镜像，那正是缺陷所在。

把那些读等级的地方也改成读库，等于为满足一句自己写下的话，给热路径（翻牌、进本、接任务）各加
一次数据库读去取一个本来就正确的值；其中教学升级那一处的等级还是写入命令的**判断入参**，改它
有带出第二处可观察变化的风险，而本 Spec 明令只允许一处。

故按 `modeling-task-contracts` 的「结果与机制分离」闸，把 C-2 与 RQ-003 重述到证据支持的层面，
契约升 revision 2，**修订理由与「这是一次控制者做出的收窄」写在契约里**，并要求在交付汇报中
向所有者明说，不埋在产物里。

### 其余整改

`NoCarriedGold.cs` 里 `<see cref="NoCharacterRow"/>` 指向不存在的类型（同轮内一次半途而废的
改名残留），改为 `<see cref="CharacterRow.None"/>`。

### 整改后判据

**2024 通过 / 2 跳过 / 0 失败**。与基线 2015 的差额 **+9**，精确对账：三条路径各 3 条新用例。
`Server/src` 在整改中零改动；`CommandHandlerArchitectureTests.cs` 一字未动。
