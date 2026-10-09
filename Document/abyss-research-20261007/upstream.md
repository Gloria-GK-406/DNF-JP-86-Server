# ServerS4A21 深渊与 A21 PVF 只读调查

## 身份与证据边界

- 调查日：2026-10-07；上游根目录 `E:/Project/DNF/ServerS4A21`。
- HEAD：`c20dba5fa52c2faf4b5087a3b477676be8434e8f`，分支 `master`；无 tracked diff，既有未跟踪 `_reference-backup-20260826/`。未读该备份树作为主线事实。
- 无 `.codegraph/`，因此按仓库约定使用 `rg`、逐行只读源代码。
- 实际 PVF：`E:/Project/DNF/DNFClient-A21-protocol2/Script.pvf`，SHA-256 `03154D18DE43F5A0BCB11B2935AE8BB40BABD22495700657D682E491A24A3543`。
- 复用只读 `PvfProbe`：`E:/Project/DNF/DNF-JP-86-Server/Tool/PvfProbe/bin/Release/net10.0/PvfProbe.dll`，SHA-256 `F9C16270454BD9AD7FD3B8F493D59B5CF1117519A8BF556BEF13BF76EC2FC0D1`；未编译、启动上游或游戏。
- 已读上游 AGENTS、CONTRIBUTING、README、业务规范及副本规范相关所有权/资源/掉落/生命周期规则。未改上游、客户端、PVF、数据库；未执行服务端 SelfTest。本文的代码结论是 **UpstreamImplementation**，PVF结论是 **PVFStatic**，不能升级为 A21 `WireCaptured`/`ClientVerified`；上游注释援引客户端地址或官方样本时，本文仅转述为“上游注释宣称”。
- 身份原始输出：[upstream-identity.txt](evidence/upstream-identity.txt)。核心完整小文件快照：[upstream-source-static.txt](evidence/upstream-source-static.txt)；调用链逐行片段：[upstream-source-callpaths.txt](evidence/upstream-source-callpaths.txt)。以下代码路径均相对于上游根目录。

## 1. 实现覆盖总览

| 玩家行为/系统 | 上游当前状态 | 证据和限制 |
| --- | --- | --- |
| 选图主动深渊 | 已实现 | SELECT_DUNGEON Flag1 非零，非教程且所在区域为 HellDungeon；再查权限、地图、门票 |
| 选择困难/非常困难 | 已实现部分规则 | Flag2=1/2直接指定A/B；其他值按PVF60:40权重选；不是按请求名字推断 |
| 华丽挑战 | 已实现，费用硬编码 | CMD VERY_DIFFICULT_HELL_PARTY切UI状态；19万金币且季节/普通房有效时强制A；费用无PVF来源 |
| 普通图随机遇到深渊、深渊值累积 | 未找到实际消费路径 | PVF hellparty basic values、randomhell.etc均存在；上游仅消费group、difficulty及season开关 |
| 封印门/柱、破坏后波次 | 服务端投影已有；动作主要交给客户端 | START_MAP附隐藏模板；HELLPARTY_START只改WaitingStart→Started，不验证body/柱/命中次数，也不在该请求时逐波发刷怪通知 |
| 深渊组最终装备奖励 | 已实现 | 按group剩余人数归零且末只无[hell monster]标记触发；独立掉落仍单独生成 |
| 史诗碎片 | 已实现，但抽取次数硬编码 | PVF映射物品关系；次数不是从PVF drop info读；不走装备的命中/rarity门 |
| 组队准入与扣票 | 已走通用组队计划/提交路径 | 每个参与者权限与票均检查，整组成本提交；共享instance selection/maze |
| 房间重访、深渊中退、重进、掉落重放 | 复用普通副本机制，专用正确性未验证 | 存在phase/count重置与已死记录保留的组合风险，见第7节 |
| 专用深渊结算/累积通知 | 没有找到业务发送 | HELLPARTY_VALUE、HELL_PARTY_CLEAR、BOOSTER、FRIENDSHIP仅枚举引用，不代表已实现 |

## 2. 主动入场、权限、成本

### 调用链

`GameProtocolHandler.RegisterDungeonHandlers → DungeonHandler → DungeonEntryHandler.SELECT_DUNGEON → EntryAdmission.TryPrepareRun → TryPrepareHellParty → WorldMap/QuestRepository/Dungeon.FindHellMapRoom → CostPlan.Validate → PartyEntryAdmissionPlans → TryCommitGroup → EntryPreparation.ApplyTo → DungeonRun.Selection → DUNGEON_INFO/START_MAP`。

精确锚点：

- `Network/Handlers/Dungeon/DungeonEntryHandler.cs:2651-2660`：`run.HellMode = !isA21TutorialEntry && req.HellPartyRequestFlag != 0 && DungeonData.IsHellDungeon(req.DungeonId)`。Flag1不要求等于1；教程忽略残余深渊标志。无随机触发分支。
- `Game/Dungeon/DungeonEntryAdmissionApplicationService.cs:67-127,214-322`：一般副本required items仍加到同一cost plan；深渊追加权限、地图和门票计划。
- `GameWorld/WorldMap.cs:411-414`：WDM `[hell dungeon]`、`[hell quest]`、`[hell freepass item]`、`[item condition]`分别成为深渊区域开关、前置任务列表、替代通行证、普通门票ID列表。
- `Game/Dungeon/DungeonEntryCostService.cs:486-525`：所有正数hell quest都必须已完成；空列表视为无此限制。真实查询是QuestRepository，不是仅检查客户端的选图状态。
- `Network/Handlers/Dungeon/DungeonEntryHandler.cs:1438-1514`：打开选图时按队伍slot列出没完成任务的成员，供阻断UI投影；SELECT时还会重查。

### 门票规则

`DungeonEntryAdmissionApplicationService.cs:325-384`：按WDM列出freepass替代项，随后列普通票替代项；每种替代项自身只有该item及数量，`consumeOnEntry=true`。普通票数量取DGN最低入场等级，不是角色等级/怪物等级/基础等级。

`GameWorld/WorldMap.cs:289-295` 的公式为硬编码：

```text
minLevel <= 44 → 0（准备成本时因<=0拒绝，故不是免费主动入场）
minLevel > 44 → ((40*minLevel - 1800) / 100) + 10
```

`/`为C#整数除法；正数区间等价于`floor(0.4*minLevel-18)+10`。例：45→10；70→20；75→22；80→24；85→26。**PVF只给票ID，本文没有找到该数量公式的当前PVF来源。**优先freepass是当前cost plan顺序；不能从中推断官方优先级。

`EntryAdmission.cs:252,401-405`：Flag2=1或2直接选A/B；否则`HellPartyData.ResolveManualHellPartyMode()`。`HellPartyData.cs:56-72`取`[difficulty]`每行第4个数`Probability`作为A/B人工入场选择权重；当前PVF A60、B40，所以名义60%非常困难/40%困难。总权重<=0回退A。

### 华丽挑战

- `DungeonEntryHandler.cs:3633-3641,3672-3677`：请求body长度>13且`body[13]==0`表示开启；只存玩家session选图开关，无ACK或扣费。
- `DungeonEntryAdmissionApplicationService.cs:13,255-285`：费用固定190000；开启时寻找A且preferSeason=true的房，金币足够才强制A并加金币成本；金币不够或房无效则回到普通A/B抽样而非整体拒绝。
- `HellPartyData.cs:151-181`只读`etc/seasonserver/season.etc`的`[season hellparty] on`；没有在此校验`[duration]`，也没读取角色season资格。当前PVF `[season hellparty] on`，季节时间2016-01-01至2030-01-01是原始配置，不能据此补出不存在的准入规则。

### 组队扣费边界

`DungeonEntryHandler.cs:3528-3627`逐个冻结参与者校验session/party cohort、owned inventory、准入，不能只队长扣票。`:2889-2918`调用整组`TryCommitGroup`；成功后才记每人成本并继续。`:2922-2948`还会检查generation/入场限制，代码对licensed entry有特定回滚；本文未找到深渊票费用的专用退还。成本提交成功之后投影失败/旧generation是否应该退还，是待进一步审计和实机明确的语义，不把现有路径写成完全原子入场。

## 3. PVF 真实结构与地图房

原始数据：[hellparty](evidence/upstream-pvf-hellparty.txt)、[hell装备掉落](evidence/upstream-pvf-hell-drop.txt)、[randomhell](evidence/upstream-pvf-randomhell.txt)、[season](evidence/upstream-pvf-season.txt)、[深渊文件清单](evidence/upstream-pvf-hell-files.tsv)。

### hellparty.etc

```text
[hellparty basic values]
max base value=100; min base value=1; base value=1; base rising value=5;
sub base rates=1; level difference rates=[10000,5000,2000];
difficulty rates=[2000,4500,6800,6800,6800]; party rates=[0,1800,4000,8000];
ancient dungeon add rate=8500; guide line value=63

[difficulty]
4
A 8 5 3 60 0
B 6 4 97 40 0
C 4 3 0 0 0
D 0 3 0 0 1

[hellparty monster group]
[group index] id
[group] difficulty-letter (actorCode entityKind)*
```

上游`HellPartyData.cs:223-261`把每行五值映射为`RewardRollCount/Unknown1/RatioProbability/Probability/Unknown4`。第1值是当前奖励roll数；第4值被A/B选择消费；第3值仅保存/记录日志，未发现作为遭遇/掉落概率消费。**A3/B97不是上游实际A/B选择比，也不是史诗率。**第2/5值未知；基础值整块未被该类解析。

`entityKind==1`作为APC，其他值作为普通怪：`HellPartyData.cs:214-218`；转换到START_MAP类型是APC=5、普通怪=0，`Dungeon.cs:1240-1279`。APC优先自身AICharacter等级，缺失回退副本basis；普通怪取副本basis；`[hell monster]`标记从各actor脚本读，决定最终组奖励门。

### 随机深渊配置

`etc/randomhell.etc`确实含重复的`[hell info]`，每项`[level] min max`、`[special passive object] 30528 550 254 0`、`[apc list]`三元组（例`125 25 1`）。55-60段有多次重复，81-85之后还有最终86-86段，其APC列表为`166 40 1 169 40 1 172 40 1 175 40 1 176 30 2 177 30 2 167 30 2 168 30 2 170 30 2 171 30 2 173 30 2 174 30 2`。上游未引用该文件；三元组第2/3值的官方意义和重复段选择规则不能凭名称补出。`hellparty basic values`的计量分母、累计/衰减时机、队伍升降/换人/返城处理也未在上游实装。

### WDM区域、DGN分类

- `worldmap/worldmap.lst`中的 `[hell dungeon]=1`命中12项：[upstream-pvf-hell-worldmaps.txt](evidence/upstream-pvf-hell-worldmaps.txt)：Antberr12、LuftHafen15、TimeGate17、PowerStation19、Abnoba21、Meltdown22、DriftCave23、FallsToTheSky24、Icewall25、MirrorArad31、CastleoftheDead32、TauLevel35。
- `dungeon/dungeon.lst`扫描1047个有效文件，首个`[hell dungeon]=1`命中51项：[upstream-pvf-hell-dungeons.txt](evidence/upstream-pvf-hell-dungeons.txt)。这是token扫描口径，不是“可主动深渊完整列表”：还必须满足WDM可见性/权限、当前maze的有效seal-door、地图的hellparty组。
- `[special dungeon]=1`命中12项，含2005-2014、2016和Kings Library195：[upstream-pvf-special-dungeons.txt](evidence/upstream-pvf-special-dungeons.txt)。**特殊副本是另一种PVF分类，不能把整个Special目录当深渊。**
- 路径带`hell`也不自动成为深渊；例如Dungeon23 Darkhell与27 Bloodhell是剧情副本名，事件hell路径亦存在。

当前三地区的样本（PVFStatic）：

| WDM | hell quest | 普通票 | freepass | 样本DGN/房 |
| --- | --- | --- | --- | --- |
| TimeGate17 | 4391 | 3330 | PVF列20种，每种x1 | 常规timegate与任务版并存；不能只按目录启用 |
| PowerStation19 | 4539 | 3330 | PVF列20种，每种x1 | Trombe103，ordinary与season不同房 |
| Castle32 | 2002 | 3330 | PVF列20种，每种x1 | Watchtower190见下 |

原始WDM：[TimeGate](evidence/upstream-pvf-worldmap-timegate.txt)、[PowerStation](evidence/upstream-pvf-worldmap-powerstation.txt)、[Castle](evidence/upstream-pvf-worldmap-castle.txt)、[Antberr](evidence/upstream-pvf-worldmap-antberr.txt)。普通票3330原始STK明确名为“深渊派对邀请函”，说明不同地下城等级需不同数量；其`[need material] 3331 1`不是本次上游准入代码使用的替代规则：[ticket3330](evidence/upstream-pvf-ticket3330.txt)。

### DGN → maze → seal door → MAP

`Dungeon.cs:996-1069`：默认A/B都使用ordinary seal-door；只有preferSeason且season on才先取season；配置无效时有ordinary/season互相fallback，再按`hell_`文件名和坐标猜候选。`:1071-1120`要求坐标对应当前maze的普通MAP、深渊MAP含hellparty block、波次非空。文件名fallback是上游兼容策略，不能视为PVF权威关联。

Watchtower190实际DGN：`min85/basis88`，maze0 normal map `(3,3)=38142`，ordinary `60078@(3,3)`；season `91009@(3,1)`；`hit count=[50,50,50,50,50]`、`seal door appear rate=10`；后续quest maze有`[escape hell] 1`。[DGN完整](evidence/upstream-pvf-dgn-watchtower.txt)。这些hit/appear/escape字段没有在深渊处理链被消费。

Watchtower普通seal60078的柱块：

```text
30528 700 307 0 1 [hellparty]
125 25 1 126 25 1 127 25 1 128 25 1
176 30 1 177 30 1 167 30 1 168 30 1 170 30 1 171 30 1 173 30 1 174 30 1
166 40 2 169 40 2 172 40 2 175 40 2
[/hellparty]
```

数据是`groupId rate order`，不是具体怪物代码。`Dungeon.cs:1195-1237`按order分组，先滤当前A/B有有效actor组，再按正rate加权抽一个组；`:1420-1444`使用ServerRandom。A情况下order1可抽125-128（各25，均A普通hell actor），order2抽166/169/172/175（各40，均APC A）；B情况下order1只保留176/177/167/168/170/171/173/174（各30，均B）。因此地图中order并不等于“固定两波且每波所有列都生成”。见[seal60078](evidence/upstream-pvf-map-seal60078.txt)、[season91009](evidence/upstream-pvf-map-season91009.txt)。

另一个文件名为`hell_38142(3,0)normal.map`的候选块使用A组在order1、B组在order2；它并非DGN明确指定的60078，不可凭文件名把`(3,0)`当190真正深渊坐标：[命名候选](evidence/upstream-pvf-map-watchtower.txt)。

`Tool/PvfLib/Models/MapFile.cs:1001-1028,1115-1127`解析第一个inline hellparty块并附到前缀最后一个特殊对象。若一个特殊对象段存在多个hellparty块，当前正则首匹配逻辑不证明可全部支持。对象30528通过passiveobject.lst指向`Monster/CosmoFiend/SealedGateOfHellDungeon4.obj`，其原始int data包含`...30 1 80 60 40 20 500 20 20 4 1053`及封印柱基础/变光/破坏动画。该数组不是已经确认的服务端生命/触发协议布局，保留Unknown：[pillar30528](evidence/upstream-pvf-pillar30528.txt)。

## 4. 进入房间、柱、怪物生成与协议

`DungeonMapHandler.cs:550-605`按frozen HellRoomInfo替换为深渊MAP，instance第一次创建房时拿普通房怪物摘要，并附所有选中的深渊波次模板。`BuildHellPartyStartMapMaze:1150-1193`是“普通房怪物 + hidden hell actors”，不是只发深渊组。

`AppendHellPartyTemplateRows:1217-1248`：每个actor `TemplateOrder=PVF order`、`Flag0=1`、`Flag1=0xFF`、`PacketIndex=null`、`ExtraState=0`。隐藏怪也被服务端登记为blocking actor，计入group remaining；START_MAP隐藏的含义不能等同于未在服务端注册。

`DungeonMapHandler.cs:677-713`初始化pillar code/spawn coords/waves及WaitingStart；HellMode的所有标准房START_MAP都有mode=A/B、fogFlag=1，普通run mode=2、fogFlag=0。只有匹配深渊房才附hidden actors并发送HELL_PARTY_MONSTER_INFO。

`HandleHellPartyStart:1314-1333`只检查当前房为深渊房且phase=WaitingStart，转Started；body完全未解析，重复或非深渊房静默忽略。没有柱耐久、命中计数校验，没有按波次独立发送spawn/奖励。当前“柱破坏→隐藏演员可见/动作”依赖客户端MAP/object/hidden模板行为；上游源代码不能证明客户端何时显示或实际发何种body。

协议分类以`Network/Core/PacketTypesA21.cs`为准；**同号CMD response与NOTI必须区分**：

| 方向/类别 | opcode / 枚举 | 上游字段/动作 | 证据等级 |
| --- | --- | --- | --- |
| C2S CMD | 0x000F ENTER_SELECT_DUNGEON | 打开选图、生成队员任务限制投影 | UpstreamImplementation |
| C2S CMD | 0x0010 SELECT_DUNGEON | 最少9B：u32 dungeon@0,u8 difficulty@4,u8 hellFlag1@5,u8 hellFlag2@6,u16 sentinel@7；保留额外tail特征 | `Parsers/Dungeon/SelectDungeonRequest.cs:36-64` |
| S2C CMD response | SELECT_DUNGEON同号 | 通用入场成功/结构化拒绝；深渊没有另造成功ACK | UpstreamImplementation；错误码需原生链单独对照 |
| S2C NOTI | 0x001B ENTER_SELECT_DUNGEON | 通用选图信息及成员slot阻断 | UpstreamImplementation；并非C2S0x000F |
| S2C NOTI | 0x001C DUNGEON_INFO | u32 dungeon,u8 difficulty,maze,bossXY,hellXY；hellXY位于+8/+9，未启用为0/0；动态minimap group+固定尾 | `Builders/Dungeon/DungeonNotificationBuilder.cs:51-107` |
| S2C NOTI | 0x001D START_MAP | +7 mode，+14 u32 mapId，hidden actor模板，extra/drop及fog尾字段 | builder:160-229；下见actor记录 |
| S2C NOTI | 0x02A7 HELL_PARTY_MONSTER_INFO | i32 count，重复i32 key、i32 level；上游key填actorCode并按code去重 | builder:143-157；map handler:1006-1020,1125-1147；真实index语义见下 |
| C2S CMD | 0x00EB REQUEST_DISJOINT_ITEM | 有entered分解店时走分解；否则路由深渊开始，无深渊专用parser | `GameProtocolHandler.cs:1049,1057-1064`；不要把业务旧别名HELLPARTY_START当另一opcode |
| C2S CMD | 0x0413 VERY_DIFFICULT_HELL_PARTY | body[13]==0开启华丽挑战，否则关闭；无独立ACK | entry:3633-3641,3672-3677 |
| S2C NOTI | 0x00CF HELLPARTY_VALUE | 仅枚举；未找到业务发送 | 搜索当前.cs引用 |
| S2C NOTI | 0x030E HELL_PARTY_CLEAR | 仅枚举；未找到业务发送 | 同上 |
| S2C NOTI | 0x0431 HELL_PARTY_BOOSTER_GAGE | 仅枚举；未找到业务发送 | 同上 |
| S2C NOTI / C2S CMD | 0x049C FRIENDSHIP_HELL_PARTY_INFO / 0x04C3 FRIENDSHIP_HELL_PARTY_SELECT | 仅枚举；未找到实现 | 同上 |

START_MAP actor记录为21B：`u16 templateOrder + i32 packetIndex + u16 runtimeSeq + i32 actorCode + u8 level + u8 type + u8 flag0 + u8 flag1 + i32 extraState + u8 extension0`，逐Write宽度之和`2+4+2+4+1+1+1+1+4+1=21`。APC与普通怪packetIndex各自从0递增，runtimeSeq连续；actorCount是u8。这里只能证明上游布局，上游注释援引A21 consumer并非本次独立逆向。深渊怪info在START_MAP之后才发送，不能将其解释为单独即时刷怪包。

### 与同批独立客户端研究的冲突核对

同批[client.md](client.md)直接读取本PE。其结论优先于上游命名与注释；上表是实现快照，不得直接当作待实现的正确协议规格。

| 上游选择 | 同批客户端证据 | 结论 |
| --- | --- | --- |
| 02A7 key填monster/APC code | ClientStatic为`u32 count + (u32 index,u32 level)*`，原生日志`hell index(%d) level(%d)`；index→组/actor映射未闭合 | 包体形状一致，但上游key的业务取值未证；不是partyId，也不能径直等同monsterCode |
| 0413 `body[13]==0`当开启、静默无ACK | 发送点raw-copy18B，首14B本地未赋值、+14为u32状态0/1；18B仅本地writer尺寸，最终wire仍Unknown；response外层成功后读12B（三u32） | 上游13偏移与原生+14字段错位，忽略response也与原生consumer存在缺口；需抓包闭合，不能照搬 |
| 00EB离开分解店则开始深渊 | ClientStatic当前writer/handler为分解物品及NPC/摊位链；封印柱procAppend已读区域未见该专用发送 | “深渊双用”只存在于上游路由，未获A21客户端证明；不能登记为已确认深渊start opcode |

## 5. 装备掉落、史诗率和装备池

### 死亡触发链

`DungeonKillApplicationService.cs:921-939 → BuildAbyssPartyDropRequest:1674-1710 → DropService.GenerateAbyssPartyAndRegister:218-264 → IndependentDropSystem + (组末只&&!IsHellMonsterScript ? HellMonsterDropConfig + EpicPieceDropService : 无最终奖励) → RegisterDrops → MONSTER_DIE/drop投影 → 普通拾取Inventory奖励路径`。

group计数由`DungeonMapHandler.BuildHellPartyGroupRemaining:1251-1267`按所有隐藏actor的groupID统计，死亡时减少；归零标记last。**奖励不是任意深渊怪固定roll，不是只在整个深渊最后一只统一roll，也不是通关翻牌发。**若多个非hell组被选中，每个组各有最后一只奖励；若同一个group重复出现在不同order，计数按groupID合并。若组末只是脚本`[hell monster]=1`，装备和碎片都不触发，杀敌顺序语义需要验证。

`DropService.cs:220-236`首先受RewardPolicy.AllowsMonsterDrops限制；每只深渊actor仍按Independent source policy生成独立掉落。最终hell装备/碎片分支没有再次按normal装备source标志判定，不能把通用source policy覆盖猜成完全一致。深渊actor该分支`goldGained=0`；普通房怪在HellMode时普通掉落基准改用dungeon basis而非actor.Level。

### 装备命中门

`HellMonsterDropConfig.cs:9-10,43-61`：`BaseDropRate=100`、`DropChanceDenominator=1001`；按basis level找到范围行、按dungeonDifficulty0..4夹取rate，`chance=100*rate/100`；每轮`r=lcg.Next(1001)`，**r>chance才miss，即包含0和chance**。

当前PVF `[dungeon difficulty drop prob]`：

| basis范围 | N | E | M | K | S |
| --- | --- | --- | --- | --- | --- |
| 1..44 | 0 | 0 | 0 | 0 | 0 |
| 45..200 | 335 | 875 | 1000 | 1400 | 1000 |

rate<=0直接无奖励；正rate时名义单轮装备命中概率为`min(chance+1,1001)/1001`，所以N=336/1001约33.5664%、E=876/1001约87.5125%、M/K/S=100%。K的1400已经超过r最大1000。代码注释“70服务端以基础倍率乘深渊概率表；当前模拟器没有独立基础倍率来源，先固定100”是作者说明，**本次没有读取70对应实现，不把它当已证差异**。

### 稀有度门

`HellMonsterDropConfig.cs:195-214,247-267`：读每行7累积阈值，却只遍历rarity0..5六列。`r=lcg.Next(1000000)+1`，先命中的累积阈值定rarity。

| A/B | 当前PVF累积阈值（rarity0..6） | 名义rarity概率（0..5） |
| --- | --- | --- |
| A | 0,599000,993000,993000,1000000,1000001,1000001 | 0%,59.9%,39.4%,0%,0.7%,0% |
| B | 0,800000,1000000,1000000,1000001,1000002,1000001 | 0%,80%,20%,0%,0%,0% |

这些是上游消费当前PVF后的概率；rarity4=Epic是当前代码常量。**0.7%只在A模式且通过前述装备门后的单轮稀有度抽取，不是整局概率。**B名义史诗为0；不从B的`1000001`推正概率，因为r最大1000000。

PVF A RewardRollCount=8、B=6。以单个具备最终奖励资格的A组、史诗池非空、无药、各轮近似独立为前提，N单轮史诗名义概率`(336/1001)*0.007`；E=`(876/1001)*0.007`；M/K/S=`0.007`。8轮至少1史诗名义值为`1-(1-p)^8`，分别约1.864%、4.797%、5.465%。**这不是官方掉率、实测频率或整局总概率；未计组数、池fallback、LCG序列相关。**

`DnfLcg.cs:16-34`每次Next做3次`state=state*1103515245+12345`，拼11/10/10bit结果，Next(max)为取模。上面的分数基于等概率余数的名义计算；实际seed分布与取模偏差未测。掉落用`ParticipantDropLcg ?? RoomLcg`，组队每人独立；`DungeonMapHandler.cs:626-632`seed由room seed、instanceId、roomId、characterId派生。

### 史诗药

`HellMonsterDropConfig.cs:63-68,281-299`：药生效且初次不是史诗，额外抽一次rarity；只有第二次为史诗才覆盖初次，否则保留原稀有度。这不会增加装备命中轮数；A条件史诗名义概率成为`0.007+(1-0.007)*0.007=0.013951`，B仍0。药是否生效通过`EpicBuffPotionDefinition.IsActiveForCharacter`取持久effect，不在此调查中修改或实机验证。

### 装备池与等级范围

`EquipmentDropPoolProvider.cs:47-126`从`EquipmentDefinitionCatalog.GetAll()`即equipment.lst定义取`Rarity/Grade/CreationRate`，只排除creationRate<=0、grade<=0、rarity不在0..5；桶键`grade*10+rarity`。权重=creation rate，不是平权；没有按职业/hell区域/装备类型过滤深渊pool，avatar也进GetPool的大池（虽然翻牌另有avatar/normal子池）。是否实际存在满足史诗条件的avatar由PVF决定，本文没编造池数量。

`HellMonsterDropConfig.cs:314-368`实际调用`EquipmentDropLevelRule.TryGetAtlasGradeRange`：`gradeMin=max(1,minRequiredLevel)`、`gradeMax=min(200,basisLevel+7)`；交换/填补异常输入。Watchtower例85..95，不是PVF参考表basis88对应的80..91。每种rarity池为空且rarity>0时允许回退rarity0；无池则整轮无物品，不能报告只按rarity表就必有史诗。

`[item drop ref table]`已解析成200行down/up，但`TryGetGradeRangeFromItemDropRefTable`在当前源码没有调用：这是**已加载但未消费**，不是实际等级池规则。`[item drop rarity control] 2 13 17 0 -5`亦未解析消费。

### hell rarity group

本次对1047个DGN及118479个equipment.lst定义按精确tag **`[hell rarity group]`**扫描全部缺失；hell字符串池扫描也无该tag；全`.dgn`标签统计没有此项。上游全代码无此字段，也无group式装备池过滤。[DGN扫描](evidence/upstream-pvf-hell-rarity-group-values.txt)、[装备扫描](evidence/upstream-pvf-equ-hell-rarity-group-values.txt)、[标签表](evidence/upstream-pvf-dgn-tags.txt)、[字符串池](evidence/upstream-pvf-hell-pool.txt)。这只证明这些扫描范围内未见该**确切名称**，不能解释70同名/其他配置字段，也不能证明整个客户端没有对应机制。首轮probe误传未加方括号tag，已用精确方括号重跑并覆盖结果；本文只引用重跑证据。

## 6. 史诗碎片

`EpicPieceDropService.cs:46-117,120-146`：次数=`hellModeA?1:0 + dungeonDifficulty(0..4)→5/6/8/9/10`。因此A每次合资格group末只为6/7/9/10/11次，B为5/6/8/9/10次；每次固定Count1。

每次直接从`HellMonsterDropConfig.TryChooseSpecificEquipment(rarity4, allowFallback:false, predicate:HasEpicPiece)`加权选装备，再经`EpicPieceCatalogService.TryGetEntryByOutputId`映射pieceId。没有命中概率门/rarity门，不受史诗药二抽影响；池空则该次不产碎片。DungeonId与MonsterId在request里存在，但当前TryRoll仅在失败日志使用，**不是深渊怪/地下城专属碎片表选择条件**。

`EpicPieceCatalogService.cs:33,75-113`真源`etc/epicpieceinfo.etc`，`[equipment piece drop info][piece list]`是equipmentId/pieceId对，当前原始数据710对（10300000..10300709）；另有`[equipment recipe info]`定义合成。这个PVF存在映射不意味着次数硬编码来自PVF。[原始epicpieceinfo](evidence/upstream-pvf-epicpieceinfo.txt)。当前每次抽取实际候选数与region/等级统计没有在本次运行上游目录；后续实现前宜用只读probe复算当前pool并与client相同规则对照。

## 7. 队伍、重访、结束与残余风险

### 共享世界和参与者掉落

上游副本规范要求Instance拥有共享selection/maze/room，Run只拥有单个参与者状态。当前物理Room通过`Instance.GetOrCreateRoom`共享；首个创建时随机roomSeed和runtimeSeq，重访复用它们；每个玩家都重建自己的RoomState和ParticipantDropLcg。深渊`phase/group remaining`目前是每个RoomState字段，不是一个独立的instance hell机制owner。

所有参与者准入都检查hell quest及门票；共同selection携带HellRoomInfo和mode。对队员的`TryValidateExistingRunEntry`仍会做TryPrepareRun（请求固定mode，gorgeous标志也带入）；后续真正使用leader frozen snapshot，不能把prepare中临时房抽样等价为每人各用不同共享MAP。队员独立掉落使用participant随机流，而普通actor物理死亡事实通过InstanceRoom同步。

### 锁房、重访

- `DungeonMapHandler.cs:1301-1311`只有phase=Started且未清房时MOVE_MAP被阻止；WaitingStart可离开。
- `DungeonKillApplicationService.cs:591-598`房间清完且已Started则Complete；没有发HELL_PARTY_CLEAR。
- `DungeonMapHandler.cs:614-619`重访保留InstanceRoom已死seq；`:636-650`新建RoomState并承接Cleared；但`:677-687`无条件再置WaitingStart，并按全START_MAP actor列表重建group remaining。
- **静态风险（Hypothesis）**：未启动前已经发生kill/再重访时，已死演员仍算入group remaining但不会再次有效死亡，可能导致组末奖励不触发；清完后重访phase又回WaitingStart，是否允许开始/如何重放隐藏对象也未知。不能据此声称已复现漏洞。须查客户端WaitingStart时能否对hidden演员合法kill，并验证kill去重/房间投影是否排除了已死actor。
- 房间重访把ParticipantDropLcg也重置到原派生seed；普通死亡/已发掉落的幂等journal是否完整覆盖深渊奖励，以及group计数是否按已死恢复，需重点对照。

### 重进与中途退出

结算EPLP retry走`DungeonSettlementHandler.cs:3578-3740`：先验证全部参与者retry准入，再结束旧run，使用return anchor，队长标记pending retry，由后续选图重新开始；`TryValidatePartyRetryEntry:3862-3893`只预检可付票/权限。新SELECT仍执行正常组成本提交，所以它不是旧门票无限复用。未找到深渊专用retry免票或退票规则。

`DungeonRunLifecycle.cs:524-628`统一EndRun：detach、registry terminate、取消timer、持久经验、清机制effect、end pet；无deep abyss refund/专用结算。结束后地面掉落只是旧run状态；已捡取走普通Inventory持久化，未捡取是否允许被保留/重放需按照通用resume窗口验证。本文没有启动数据库或模拟中退，不能下“所有退场路径无丢物/无重奖”的结论。

深渊actor奖励在kill发生，通关EXP/免费/付费翻牌仍走普通settlement；未看到凭“深渊完成”另发一份史诗。`:3007`的HellPartyRoom排除只是settlement对某些当前房actor清理投影条件，不是取消整个副本结算。`[escape hell]`官方与回城/允许绕过房间的真实关联仍Unknown。

## 8. 实现前必须保留的未知项

1. **A21原生请求**：SELECT两flag、0x00EB柱开始body、0x0413开关body的消费条件与真实发送顺序；上游parser和注释需要独立ClientStatic/WireCaptured核对。
2. **随机深渊**：基础值如何按通关/等级差/难度/队伍/远古图计算；抽样分母；换队、重进、中退、掉线、正常结算是否重置；randomhell重复level块与APC三元组含义。上游不能提供完成实现参考。
3. **封印柱权威**：hit count、appear rate、30528 int data、PVF特殊对象客户端自动生成顺序；客户端脚本是否先普通房→破柱→隐怪按order出现；服务端如何验证不能提前kill hidden actors。
4. **掉率差异**：基础倍率硬编码100、装备池图鉴范围、unused item drop ref/rarity control、奖励末只[hell monster]门、史诗药二抽、碎片次数，哪些是官方规则、哪些仅模拟器权宜实现，须对照70代码和A21证据分别确认。
5. **掉落池实际口径**：当前positive creation rate史诗数、按等级/区域/职业筛选、avatar排除、是否有其他“hell rarity group”表；本文不以文件名/图鉴范围替代确认。
6. **组队/重访/重进**：深渊房phase/group remaining的共享owner与replay恢复、各队员独立奖、队员离线/中途离队、末杀者/APC命中顺序、入場成功前后费用返还策略。
7. **奖励和结算证据**：0x030E/0x00CF是否本客户端实际使用、碎片地面掉落/吸取/书册/满包效果、普通通关翻牌和中退是否相互影响。枚举存在不等于请求或通知已经实现。

本文提供的是后续规格选择和实现前置证据。未实现、未部署、未提交，保留人测与抓包接受边界。
