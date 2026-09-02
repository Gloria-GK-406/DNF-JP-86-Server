---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: 通关结算的翻牌奖励

## Implementation Approach

Spec 的核心成果是「玩家通关之后能翻牌并拿到东西，且拿到什么由归档决定」，
必要支撑是「本局内内容不变、牌位不可重翻、付费先收后发」，
外围保证是读数出处对照、各稀有度可达性证明与分层枚举。

最早的核心成果切片是 **T-003：一局结算时四个牌位的内容被定下来并且此后不变**——
在此之前没有「牌」这个东西可谈。它无法在 T-001 与 T-002 之前真实开始：牌面内容是从
归档算出来的，读法不定形就只能填假值，而假值一旦被上层判据钉住，后面每一层都要返工。
这正是本任务选 sdd-standard 的理由，两者因此是不可避免的前置。

三个互相牵制的决定在此定死，不留到实现时随手选：

1. **牌面状态住在 `DungeonRunState` 上。** `TrySettle` 只置标记、不结束 run，接口文档
   明写「结算与出门之间这一局仍然是他的」；`End` 的三个调用点是现成的清理出口；
   `MonsterDrops` 已经是同一条记录上的 `init` 属性，牌面是同一形状的第二份。
   选它，RQ-002 的「本局内不变」与生命周期清理都不必另建机制。
2. **规则服务一次产出整个牌面（四个牌位），不是一次一张。** 内容在结算时一次定死、
   四张同时揭晓；一次产出使「同一局同一牌位同一件东西」由「只调用一次」保证，
   而不是靠每个调用方自律传同样的种子。
3. **免费牌与付费牌共用一个规则服务**，牌型作为传入的事实。两者读同一张费率表、
   同一条稀有度阶梯、同一个装备池；只有成本表与兜底表是付费专有。拆成两个会让
   同一批读数出现两次，而重复的读数正是本任务最怕的东西。

协议这一半是去接一个已经留好的茬：`ResultScreenSlice` 今天在 `0x0045` 之后**故意不发**
版式，注释写明理由是「一次翻牌值多少属于本服务器还没有的领域」。本计划补上那个领域，
然后把版式发出来。

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs` | 新建：翻牌费率表的读取契约与其产出的记录类型 | 归档段落形状 | `IClearRewardRateSource` 与其 record |
| BD-002 | `Server/src/Dfo.Server.Infrastructure/Content/PvfClearRewardRateSource.cs` | 新建：从 PVF 读该表 | `PvfArchiveProvider` | BD-001 契约的实现 |
| BD-003 | `Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs` | 增一行内容源注册 | BD-002 | 已注册的内容源 |
| BD-004 | `Server/src/Dfo.Server.Abstractions/World/ClearRewardRules.cs` | 新建：静态纯规则——几率合成、按评级分布 | 归档值与事实 | 判定与数值 |
| BD-005 | `Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs` | 新建：规则服务契约与牌面记录类型 | 事实 + 显式种子 | 四个牌位各自的内容 |
| BD-006 | `Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs` | 新建：BD-005 的实现，本表与本池的唯一读取点 | BD-001、`IDropItemPoolSource`、`IDungeonDefinitionSource` | 牌面内容 |
| BD-007 | `Server/src/Dfo.Server.Application/ApplicationModule.cs` | 增规则服务与业务服务的注册行 | BD-006、BD-010 | 已注册的服务 |
| BD-008 | `Server/src/Dfo.Server.Abstractions/World/IDungeonRunDomainService.cs` | 扩展：`DungeonRunState` 增牌面状态；服务增「定下牌面」「认领牌位」两个动作 | 牌面内容 | 携带牌面的本局状态 |
| BD-009 | `Server/src/Dfo.Server.Application/World/DungeonRunDomainService.cs` | 实现 BD-008 新增的两个动作，沿用既有的比较交换写法 | BD-008 | 幂等的定下与认领 |
| BD-010 | `Server/src/Dfo.Server.Abstractions/World/ICardRewardDomainService.cs` 与 `Server/src/Dfo.Server.Application/World/CardRewardDomainService.cs` | 新建：编排用例的业务服务 | BD-005、BD-008 | 结算定牌、翻牌取物 |
| BD-011 | `Server/src/Dfo.Server.Data/Networking/NetworkMessages.cs` | 增两个目录成员：牌面版式、翻牌 | 协议号 | 目录成员 |
| BD-012 | `Server/src/Dfo.Server.Protocol/Game/GameProtocol.cs` | 增对应的两个线级类型号常量 | 协议号 | 常量 |
| BD-013 | `Server/src/Dfo.Server.Protocol/Game/CardReward/` | 新建切片：消息、payload codec、解包器、编码器、消息 handler、命令与命令 handler | BD-010 | 翻牌交互 |
| BD-014 | `Server/src/Dfo.Server.Protocol/Game/DungeonClear/ResultScreenSlice.cs` | 修改：卷轴应答之后追发牌面版式，并撤下那条「本仓不发」的注释 | BD-010 | 版式帧 |
| BD-015 | `Server/src/Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs` | 修改：结算时定下牌面；把 274 字节里的卡片尾部按实际内容填写 | BD-010 | 携带牌面的结算帧 |
| BD-016 | `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/`、`Server/src/Dfo.Server.Protocol/Game/`、`Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterHandler.cs` | 上提既有的背包增量告知，使其可被两个切片共用；不改其字节输出 | 既有的增量帧 | 可共用的增量告知 |
| BD-017 | `Server/src/Dfo.Server.Abstractions/Inventory/IAccountMoneyService.cs`、`Server/src/Dfo.Server.Application/Inventory/AccountMoneyService.cs`、`Server/src/Dfo.Server.Abstractions/Persistence/InventoryTables.cs`、`Server/src/Dfo.Server.Persistence/Inventory/InventoryStateTables.cs` | 扩展：增一条「扣款，不足则失败且不改变余额」的能力 | 账号与金额 | 成功扣款或不足拒绝 |
| BD-018 | `Server/src/Dfo.Server.Protocol/ProtocolModule.cs` | 增解包器、编码器、命令、消息 handler 四类注册行 | BD-013 | 已接线的协议 |
| BD-019 | `protocol/game/client-to-server/0x0046.md`、`protocol/game/client-to-server/0x0047.md`、`protocol/game/server-to-client/NOTI-0x000E.md`、`protocol/catalog.md` | 更新记录的实现映射并登记目录 | 实现结论 | 与代码一致的协议记录 |
| BD-020 | `Server/src/Dfo.Server.Abstractions/Content/DungeonContent.cs` 与 `Server/src/Dfo.Server.Infrastructure/Content/PvfDungeonDefinitionSource.cs` | 扩展：读取副本自行声明的牌面产出与付费牌开关 | 副本声明 | 覆盖声明 |
| BD-021 | `Server/tests/` | 新增判据；不改既有用例的断言与期望值 | 各任务产出 | 判据 |
| BD-022 | `Server/src/Dfo.Server.Abstractions/World/ClearRewardRules.cs`、`Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs`、`Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs` | 重定合成：`刷图基础 + 难度增益 + 骰子×组队增益 > 档位阈值`；房间数超范围取最大一档；`[drop prob]` 读作阈值而非乘数；撤掉 revision 3 遗留的探索度实现 | 四张率表、局内房间数与队伍人数 | 可核对的几率，且可达副本不会仅因房间数而整面无物 |
| BD-023 | `Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs`、`Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs`、`Server/src/Dfo.Server.Application/World/CardRewardDomainService.cs` | `[item drop ref table]` 与 `[gold card cost table]` 的键由角色等级改为副本 `[basis level]` | `DungeonDefinition.BasisLevel` | 同一副本对不同角色等级给出同一品级带与同一价钱 |
| BD-024 | `Server/src/Dfo.Server.Abstractions/World/ClearRewardRules.cs`、`Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs`、`Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs`、`Server/src/Dfo.Server.Abstractions/Content/ClearRewardContent.cs` | 保留两张兜底表的加权抽取，由每种牌各自的开关控制是否发放，默认全关 | `[gold card blank item]`、`[pcroom card blank item]`、三个开关 | 开则翻空给保底物品，关则翻空不给任何东西 |

## Ordered Tasks

### T-001: 翻牌费率表可被读出，且每一段都有出处

- Covers: RQ-008
- Boundaries: BD-001, BD-002, BD-003, BD-021
- Inputs: 归档 `etc/itemdropinfo_clearreward.etc`；既有的 `DropRateTables.ClearReward` 常量
- Outputs: 一个只读该表的内容源，产出基础率档案、类别概率、物品类别概率、难度加成、
  地图数加成、稀有度阶梯、队伍加成、付费成本表、付费兜底表

#### Steps

1. 按实测形状定义记录类型：基础率是「具名档案 → 若干等级段 → 单一率」，
   与怪物表的「一段五率」不同；付费成本表是「等级 → 价钱」；兜底表是
   「权重、物品、数量」三元组的加权表。
2. 稀有度阶梯复用既有的 `RarityLadder`，不重新建模——它已按「基数 = 末位门槛」
   正确实现，且其注释已记下参照服务端写死摇点的后果。
3. 每一段读法在 XML doc remarks 里写明它读的是哪个段落标签，以及未查明处的原样记录。
4. 明确不读 Spec 已表态不使用的三段。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/tests/Dfo.Server.Infrastructure.Tests --nologo` | 新增的内容源判据通过；读出的基础率、难度加成、地图数加成、成本表与实测值逐值相同 |
| CK-002 | `Non-command:` 逐段核对读取器与归档段落标签 | 每个被读的段落都有对应的标签常量与出处注释；不使用的三段在代码里被具名列出并说明为何不用 |

### T-002: 从归档算出一个牌位有什么

- Depends on: T-001
- Covers: RQ-008, RQ-009, RQ-010
- Boundaries: BD-004, BD-005, BD-006, BD-007, BD-021
- Inputs: T-001 的内容源；既有的 `IDropItemPoolSource`、`DropRandom`
- Outputs: 一个规则服务，接收事实与显式种子，一次产出四个牌位各自的内容

#### Steps

1. 静态纯规则只承担**牌位出现概率的合成**（单一基础率、难度、地图数、队伍、分母）；
   配置性的数值作为参数传入，不在其中默认。掷稀有度、取品级带、桶内加权抽一件三步复用
   既有零件，不得重写：既有的 RatesFor / Gate / RollRarity 三个签名都吃怪物表的逐槽
   形状因而用不上，但 RollRarity 的实质逻辑在公开的 RarityLadder 上，照用即可。
   见 TASK-02 的契约修订 1。
2. 规则服务按牌型、角色等级、难度、地图数、队伍人数合成每个牌位的出现概率，
   命中则按稀有度阶梯掷档次、按品级带取范围、从装备池按权重抽一件。
3. 物品类别只取装备那一支；时装那一支按 Spec 的非目标不取。
4. 分母与合成方式作为具名假设集中在一处，附理由说明归档没有这个数。
5. 种子由调用方传入并派生，同样的输入必须产生同样的四张牌。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-003 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo --filter ClearReward` | 几率合成判据以实测归档值为期望通过 |
| CK-004 | `dotnet test Server/86JP.Server.slnx --nologo --filter Rarity` | 归档声明可达的六档逐档可达，含最高两档；摇点上界等于归档基数减一 |
| CK-005 | `Non-command:` 以最低难度产生牌面 | 四个牌位均为空，因该难度的加成为零 |
| CK-006 | `Non-command:` 同一组事实与种子重复产生牌面 | 两次结果逐位相同 |

### T-003: 一局的牌面在结算时定下，此后不变

- Depends on: T-002
- Covers: RQ-002, RQ-004
- Boundaries: BD-008, BD-009, BD-010, BD-007, BD-021
- Inputs: T-002 的规则服务；既有的本局状态与其生命周期
- Outputs: 携带牌面的本局状态；一个编排「结算定牌」与「认领牌位」的业务服务

#### Steps

1. 在本局状态上增牌面：四个牌位各自的内容，以及各自是否已被翻开。
2. 定下牌面是幂等的：已定下的本局再次要求定牌不改变已有内容。
3. 认领牌位同样按既有的比较交换写法：认领成功返回该牌位内容，重复认领返回空。
4. 业务服务向规则服务提问时自行准备事实与种子；规则服务不查任何世界状态。
5. 不新增清理路径：本局结束的三个既有出口自然带走牌面。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-007 | `dotnet test Server/86JP.Server.slnx --nologo --filter DungeonRun` | 重复定牌不改内容；重复认领同一牌位第二次失败；既有的本局判据仍通过 |
| CK-008 | `Non-command:` 检查业务服务与规则服务的构造器 | 规则服务的依赖中没有仓储、注册表、目录、存储或领域服务 |

### T-004: 牌摆出来，并且翻得开

- Depends on: T-003
- Covers: RQ-001, RQ-003, RQ-004
- Boundaries: BD-011, BD-012, BD-013, BD-014, BD-018, BD-019, BD-021
- Inputs: T-003 的业务服务；协议记录中已闭合的版式布局与部分可得的翻牌布局
- Outputs: 版式帧、翻牌交换、以及四个牌位同时揭晓的应答

#### Steps

1. 目录中增三个成员之二（版式、翻牌），并在端点层增对应的线级类型号常量。
2. 新建切片，按已实证的形状产出版式帧；版式不携带任何物品。
3. 卷轴状态的应答之后追发版式，并撤下 `ResultScreenSlice` 里那条「本仓不发」的注释
   ——它记录的理由已经不再成立。
4. 翻牌请求携带牌型与牌位序号；序号超出可翻范围者丢弃，牌型超出者丢弃。
5. 翻牌应答的每条记录声明它描述哪个牌位与其上的物品；空牌位声明零件东西。
6. 更新两份协议记录的实现映射，并把它们登记进协议目录。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-009 | `dotnet test Server/86JP.Server.slnx --nologo --filter CardReward` | 版式帧与翻牌应答的逐字节判据通过 |
| CK-010 | `Non-command:` 翻开一个牌位后读应答 | 四个牌位各自被声明，含空牌位；重复翻开同一牌位不再交出东西 |
| CK-011 | `Non-command:` 核对协议记录与目录 | 两条记录的实现映射与代码一致，且已出现在协议目录表中 |

### T-005: 结算帧不再对牌面说零

- Depends on: T-003
- Covers: RQ-012
- Boundaries: BD-015, BD-021
- Inputs: T-003 的业务服务；既有的结算编码与其长度自检
- Outputs: 卡片尾部按实际牌面与归档价钱填写的结算帧

#### Steps

1. 结算流程在既有的幂等闸之后、构造结算帧之前定下牌面。
2. 把卡片尾部的免费牌位物品与金币、其余牌位、付费价钱按实际内容与归档成本表填写。
3. 保持体长不变；既有的长度自检必须仍然成立。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-012 | `dotnet test Server/86JP.Server.slnx --nologo --filter DungeonClear` | 结算帧判据通过；体长仍为既有值，长度自检未触发 |
| CK-013 | `Non-command:` 比对结算帧卡片尾部与本局牌面 | 两者所载的物品、金币与价钱一致 |

### T-006: 翻到的装备进背包，客户端看得见

- Depends on: T-004
- Covers: RQ-005
- Boundaries: BD-016, BD-019, BD-021
- Inputs: T-004 的翻牌交换；既有的背包增量告知与背包到货路径；地面拾取的先例
- Outputs: 可被两个切片共用的背包增量告知，以及翻牌到货的完整链路

#### Steps

1. 背包增量告知已经存在并已接线，其唯一调用方是入场补给。把它从所在切片上提到可共用的
   位置（端点层或领域服务，按仓库约定二选一并写明理由），因为切片内不得跨协议引用。
2. 上提只搬家，不改字节输出；入场路径的既有判据必须原样通过。
3. 翻牌命令把装备交给既有的背包到货路径；命令按仓库约定为事务式。
4. 到货之后在同一次交换里发出告知，顺序照地面拾取的先例：先应答后通知。
5. 在该协议记录的人工区补记新增的第二个调用方。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-014 | `dotnet test Server/86JP.Server.slnx --nologo --filter StateSynchronization` | 上提后入场路径的字节输出逐字节不变，既有判据原样通过 |
| CK-015 | `Non-command:` 端到端翻到一件装备 | 该装备出现在背包中，且告知帧随应答一同发出 |

### T-007: 金币两侧——发出去的与收进来的

- Depends on: T-006
- Covers: RQ-006, RQ-007
- Boundaries: BD-017, BD-013, BD-021
- Inputs: T-006 的翻牌链路；既有的账号钱包
- Outputs: 每张有东西的牌附带的固定一份金币；付费牌的收费与余额不足拒绝

#### Steps

1. 给账号钱包增一条「扣款，不足则失败且不改变余额」的能力。既有的加款把结果夹在零，
   不足时静默夹到零而不失败，用它收费会把付不起的玩家算成付过了。
2. 翻到有东西的牌时按固定一份计入账号。**这个固定值是本服务器的决定，不是归档读数**：
   归档说翻牌不出金币，也没有算金额的段落。它必须单独具名、附理由，
   使日后定下真规则时只改这一处。
3. 付费翻牌按归档成本表取该等级价钱，先收费后发奖；收费失败则拒绝，不发奖。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-016 | `dotnet test Server/86JP.Server.slnx --nologo --filter AccountMoney` | 扣款成功与不足失败两条判据通过；不足时余额未变 |
| CK-017 | `Non-command:` 端到端付费翻牌两例 | 金币充足者按归档该等级价钱扣减并得到内容；不足者被拒且余额与背包均未变 |
| CK-018 | `Non-command:` 核对固定金币值 | 它单独具名，其所在之处写明是本服务器的假设及其理由 |

### T-008: 副本自己说了算的那几个

- Depends on: T-002
- Covers: RQ-011
- Boundaries: BD-020, BD-006, BD-021
- Inputs: 副本定义中的牌面产出声明与付费牌开关
- Outputs: 声明了的按声明产生，未声明的落全局表

#### Steps

1. 副本定义的读取扩展出牌面产出声明与付费牌开关两项。
2. 规则服务在产生牌面前先看副本是否自行声明；声明了就用它的，含按评级的分布。
3. 未声明的副本行为不变，仍落全局表。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-019 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo --filter "PaidCard|ResultCard"` | 有声明与无声明两类副本各一条判据通过 |
| CK-020 | `Non-command:` 对一个自行声明的副本产生牌面 | 内容出自该副本的声明而非全局表 |

### T-009: 读数对得上归档，分层站得住，判据全绿

- Depends on: T-005, T-007, T-008
- Covers: RQ-013, RQ-014, RQ-015
- Boundaries: BD-021
- Inputs: 全部已完成任务的产出
- Outputs: 读数与出处的逐项对照、无出处值的清单、依赖枚举、全量判据结果

#### Steps

1. 扫一遍全部新增的规则数值，逐项指回归档中声明它的段落；指不回去的进入无出处清单
   并核对其是否已在代码里被具名标为假设。
2. 枚举全部新增类型的构造器依赖：归档读取点只应出现在规则服务上；
   规则服务不应持有任何世界状态的读写方。
3. 运行全量判据，档案可达。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-021 | `Non-command:` 读数与归档出处的逐项对照 | 每一项要么指回具体段落，要么在无出处清单中且已被具名标为假设 |
| CK-022 | `Non-command:` 新增类型的构造器依赖枚举 | 归档读取点全在规则服务上；规则服务的依赖中无世界状态读写方 |
| CK-023 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 通过数不低于 1690，失败数为零；跳过数仍为 2 |

### T-010: 出现概率按 revision 4 的式子重算

- Covers: RQ-008
- Boundaries: BD-022
- Inputs: 归档的四张率表；`ClearRewardOccasion` 现有字段
- Outputs: 新的合成实现；房间数上限；`[drop prob]` 作阈值
- Steps:
  1. 先写一条用真档案里房间数超过 5 的真副本产生牌面的判据，**确认它在现实现下变红**。
  2. 把 `ChanceOf` 改成加法合成、骰子乘组队、与档位阈值比较。
  3. 房间数超出声明范围取最大一档。
  4. 把 revision 3 遗留的探索度实现（`ExplorationRate`）一并撤掉——它已被 revision 4 取代。
  5. 所有本服务器自定项在其所在之处具名标明。
- Validation:

| ID | Check | Expected result |
|---|---|---|
| CK-024 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo --filter ClearReward` | 全绿；含房间数超范围的真副本用例 |
| CK-025 | `Non-command:` 用归档已知值手算一组期望几率，与实现逐值比对 | 逐值相符 |

### T-011: 品级带与付费价改用副本基准等级

- Covers: RQ-007, RQ-010
- Boundaries: BD-023
- Inputs: `DungeonDefinition.BasisLevel`
- Outputs: 两处取值改键；角色等级不再参与
- Steps:
  1. 先写一条「同一副本、两个不同角色等级得到同一品级带与同一价钱」的判据，确认其变红。
  2. 改两处取值的键。
  3. 复核 TASK-07 建立的「公布价与实收价一致」仍然成立——按副本取价后该问题自然消失，
     但**既有判据不得因此被删**。
- Validation:

| ID | Check | Expected result |
|---|---|---|
| CK-026 | `PVF_ARCHIVE_PATH=... dotnet test Server/86JP.Server.slnx --nologo --filter PaidCard` | 全绿 |
| CK-027 | `Non-command:` 副本 144 的品级带与价钱对照归档 `[basis level] 5` 那一行 | 价钱 580，品级带取 5 级那一行 |

### T-012: 翻空保底的机制与开关

- Covers: RQ-016
- Boundaries: BD-024
- Inputs: `[gold card blank item]`、`[pcroom card blank item]`、三个开关
- Outputs: 加权抽取机制；每种牌一个开关；默认全关
- Steps:
  1. 把两张兜底表的加权抽取实现出来。
  2. 每种牌一个开关，默认关。
  3. 判据覆盖开与关两态；关态下翻空不给任何东西。
  4. 在其所在之处写明：归档给的两件物品在本服务器都不可用，故默认全关。
- Validation:

| ID | Check | Expected result |
|---|---|---|
| CK-028 | `PVF_ARCHIVE_PATH=... dotnet test Server/86JP.Server.slnx --nologo --filter Blank` | 开与关两态各有用例且全绿 |
| CK-029 | `Non-command:` 默认配置下翻空不给任何东西 | 与 revision 3 的行为一致 |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Evidence |
|---|---|---|---|
| RQ-001 | T-004 | BD-011, BD-012, BD-013, BD-014, BD-018, BD-019 | CK-009, CK-011 |
| RQ-002 | T-003 | BD-008, BD-009, BD-010 | CK-007 |
| RQ-003 | T-004 | BD-013, BD-018 | CK-009, CK-010 |
| RQ-004 | T-003, T-004 | BD-008, BD-009, BD-013 | CK-007, CK-010 |
| RQ-005 | T-006 | BD-016 | CK-014, CK-015 |
| RQ-006 | T-007 | BD-013, BD-017 | CK-017, CK-018 |
| RQ-007 | T-007, T-011 | BD-017, BD-013, BD-023 | CK-016, CK-017, CK-026, CK-027 |
| RQ-008 | T-001, T-002, T-010 | BD-001, BD-002, BD-003, BD-004, BD-006, BD-022 | CK-001, CK-003, CK-005, CK-024, CK-025 |
| RQ-009 | T-002 | BD-004, BD-006 | CK-004 |
| RQ-010 | T-002, T-011 | BD-005, BD-006, BD-023 | CK-003, CK-006, CK-026, CK-027 |
| RQ-011 | T-008 | BD-020, BD-006 | CK-019, CK-020 |
| RQ-012 | T-005 | BD-015 | CK-012, CK-013 |
| RQ-013 | T-001, T-002, T-007, T-009 | BD-021 | CK-002, CK-018, CK-021 |
| RQ-014 | T-003, T-009 | BD-021 | CK-008, CK-022 |
| RQ-015 | T-009 | BD-021 | CK-023 |
| RQ-016 | T-012 | BD-024 | CK-028, CK-029 |

## Risks and Rollback

- Risk: 背包增量告知在 T-006 里是**上提既有实现**而不是新建（见 TASK-06 的契约修订 1：
  该帧早已作为 `InventoryDeltaMessage` 实现并接线，入场补给是它现有的调用方）。
  上提会改动一条已在生产路径上的投递。缓解：上提只搬家不改写字节输出，
  并以既有的入场判据逐字节钉住它；入场判据变红即为上提做错了。
- Risk: 翻牌应答中指示牌位归属的那个位置，其读法未经实机确认。
  「四张全部揭晓」整个压在它上面。缓解：该读法集中在编码器一处并具名，
  实机推翻时改动只落在那里。
- Risk: 付费翻牌请求的形状未实证。缓解：解包按现有读法实现并具名；
  免费那一条有抓包互证，两条的解包分开，推翻付费不波及免费。
- Risk: 结算帧体长有自检，卡片尾部填写若改变字段宽度会当场抛。
  缓解：只改写入的值，不改写入的宽度与顺序；CK-012 覆盖。
- Rollback: 任务单元的工作在通过其检查前保持未提交；单个任务的产出可按其
  Boundaries 列出的路径整体还原到基线，不波及其他任务。

## Assumptions

- 出现概率的分母以及各项加成与基础率的合成方式，只有参照服务端一个来源，归档里没有。
  按 Spec 作为具名假设集中在一处实现。
- 类别概率那一段的两个位置按「物品满值、金币为零」读，理由是同一份档案缺少把金币命中
  折算成金额的段落。这是推断，不是归档明写。
- 每张有东西的牌附带的固定一份金币是本服务器的决定，不是归档读数。
- 本服务器没有队伍模型，人数恒为一人，队伍加成恒取第一档。
- 客户端上报的评级只在副本自行声明牌面产出时使用；全局表不按评级分档。
- 判据须设好归档路径，否则读归档的用例会静默跳过，跳过数会由 2 暴涨。
