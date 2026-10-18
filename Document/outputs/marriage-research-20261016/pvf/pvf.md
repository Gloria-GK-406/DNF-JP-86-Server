# 婚姻系统 PVF 规则、入口与前置链调查

## 1. 调查边界与结论

本报告只回答当前 PVF 中声明了什么、当前服务端能否读取并执行这些声明，以及玩家入口在静态城镇图上是否连通。它不把 PVF 资源存在外推为日服 A21 已启用，也不把代码路径外推为客户端会实际触发。婚姻 CMD/NOTI 字段、原生求婚 UI 的触发条件和实机行为由 A21 专项调查确认。

结论如下：

| 结论 | 证据层级 | 置信度 |
|---|---|---|
| `20737`、`20738` 确实在当前 `n_quest/quest.lst`，并被 `etc/weddingsystem.etc` 作为婚姻任务引用 | ContentStatic | Confirmed |
| 当前 `PvfQuestDefinitionSource` 读取的正是 `n_quest/quest.lst`，会解析 `[wedding]`、等级、前置和 seeking 目标 | CurrentImplementation | Confirmed |
| `20737` 的 subtype `22` / `-1 -1 5` 不会投影成当前服务端的 `DungeonClearRequirement`，初始 trigger 会退回 `1`；“夫妻两人普通图 5 次”不能依赖现有通用任务进度实现 | CurrentImplementation | Confirmed |
| Wedding grade 被通用可接任务列表排除；但收到通用 `0x001F` 接任务请求后，当前完整调用链没有校验等级和前置，`20737/20738` 会进入普通 `QuestLog.AcceptAsync` | CurrentImplementation | Confirmed |
| 原生 A21 客户端是否会直接对 Wedding quest 发送 `0x001F` | ClientVerified | Unknown |
| 当前 town graph 选择的 MirrorArad WestCoast 没有 NPC `20013`；带 `20013` 的旧 HendonMyre town 文件存在，但未被 `town/town.lst` 选入 | ContentStatic + CurrentImplementation | Confirmed |
| 求婚 UI 是否必须先与 NPC `20013` 交互、婚礼教堂是否由 Wedding 协议动态进入 | ClientVerified | Unknown |
| 服务端 PVF 和四份客户端 PVF 中的核心婚姻资源规范化 token dump 完全一致，不是服务端包独有内容 | ContentStatic | Confirmed |

## 2. 证据身份与客户端 PVF 对照

服务端当前配置所指 PVF：

- `artifacts/sao-ticket-20261014/Script.server.pvf`
- SHA-256：`91902D91AA0015024B75F55BE037FE7C2900FD66432459B62578CA0E865DEBF4`

同时对比了：

- `DNFClient/Script.pvf`
- `DNFClient-A21-debug/Script.pvf`
- `DNFClient-A21-protocol2/Script.pvf`
- `DNFClient-A21-upstream/Script.pvf`

五份完整归档的 SHA-256 不同，但下列资源通过同一 `PvfProbe dump` 解码后，UTF-8 规范化输出的 SHA-256 在五份归档中逐项相同：

- `n_quest/Event/chn_2014_wedding_01.qst`
- `n_quest/Event/chn_2014_wedding_02.qst`
- `etc/weddingsystem.etc`
- `etc/weddingsystemavatar.etc`
- `map/town/mirrorarad/westcoast.map`
- `map/church/church.map`
- `town/town.lst`

完整矩阵见 [pvf-resource-equivalence.tsv](evidence/pvf-resource-equivalence.tsv)。这证明这些静态内容同时存在于客户端与服务端归档；它不证明客户端 UI 已开放或协议闭环可用。

## 3. 婚姻主规则

`etc/weddingsystem.etc` 的原始解码见 [weddingsystem.txt](evidence/weddingsystem.txt)。

### 3.1 资格与任务

- `[minimum level] 30`。
- `[quest] 20737 20738`。
- 两个任务均声明 `[job] [all]`，职业不受限。
- 本次检查的主规则、任务和婚礼物品没有性别限制字段。该缺失只能说明静态 PVF 未声明性别门槛；运行时或客户端 UI 是否限制性别仍为 Unknown。
- `weddingsystemavatar.etc` 为 job ID `0..8、11、12、13` 的普通/高级/豪华三档各声明 11 个婚礼 avatar ID，原始表见 [weddingsystemavatar.txt](evidence/weddingsystemavatar.txt)。它是外观映射，不是求婚资格规则。

### 3.2 婚礼档次、费用与时长

`[ceremony]` 原始三组为：

```text
0 500000 0 10
1 2000000 1 20
2 0       2 30
```

可确认第二列为静态金币数值，普通档 `500,000`、高级档 `2,000,000`、豪华档 `0`；豪华档另由 `[charged wedding ticket] 2 2683401 1` 指向 `豪华婚礼订制函`。第三列与礼包档次 `0/1/2` 对齐。末列 `10/20/30` 的字段名未在 PVF 中给出，不能直接写成时长或宾客上限。

`[minute selection]` 单独声明原始值 `300/600/900`，说明客户端/服务端至少有三档时间选项。把它们解释成秒（5/10/15 分钟）是 High 推断，具体协议中的单位和选择时点仍需 wire 验证。

其他静态规则：

- 礼金范围 `10,000..400,000,000`，`[money gift charge] 50`；`50` 的计价单位/百分比解释需协议或旧服务端行为佐证。
- 离婚费 `[alimony] 500000 500000`，双方各 `500,000`。
- `[once how many days] 1`、`[days after divorce] 1`；二者字段名存在，但具体锚点和重置时刻需运行时确认。
- `[love point] 1`、`[max love point] 999`。
- `[noti end] 60`，其单位和通知语义未由文件自描述。

### 3.3 礼包、戒指与爱心点

档次礼包由 `[wedding gift]` 指向：

| 档次 | 物品 | 静态内容 |
|---|---:|---|
| 普通 | `10002629` 普通婚礼化妆箱 | `10002901` 心意通 ×10，即 10 点爱心点 |
| 高级 | `10002630` 高级婚礼化妆箱 | 9 折婚礼礼包券 `10002911` ×1、心意通 ×30、第一阶段戒指升级券 `10002611` ×1 |
| 豪华 | `10002631` 豪华婚礼化妆箱 | 按职业发婚礼装扮礼包、婚礼武器装扮、心意通 ×100、升级券 `10002611/10002612` 各 1 |

升级券 `10002611..10002615` 依次声明：

| 阶段 | 金币 | 材料 `10002606` | `[upgrade wedding ring]` |
|---:|---:|---:|---:|
| 1 | 500,000 | 50 | 1 |
| 2 | 1,000,000 | 300 | 2 |
| 3 | 3,500,000 | 500 | 3 |
| 4 | 8,000,000 | 800 | 4 |
| 5 | 12,000,000 | 999 | 5 |

券描述明确“一方升级成功，另一方戒指也自动升级”。`weddingsystem.etc` 还声明了 ring name-tag action 和等级 `0..5` 的属性表。这里更像双方共享的婚姻状态等级；本次名称扫描没有找到与这六级名称一一对应的普通装备实体，因此一期模型不应先假定它是两件独立背包装备。

物品原始证据见 [items.txt](evidence/items.txt)。除 `10002627/10002628` 的说明文字声明“次日 06:00 删除”外，婚礼礼包和 avatar 包本身未见静态 `[expiration date]`；运行时创建实例是否附带期限为 Unknown。

## 4. 任务链精确更正

### 4.1 列表映射

`n_quest/quest.lst` 映射为：

```text
20737 -> Event/chn_2014_wedding_01.qst
20738 -> Event/chn_2014_wedding_02.qst
```

当前源码 `Server/src/Dfo.Server.Infrastructure/Content/PvfQuestDefinitionSource.cs:71-73` 将 `ListPath` 固定为 `n_quest/quest.lst`，`QuestDirectory` 为 `n_quest`；`:160-178` 读取列表并解析对应文件；`:98` 映射 `[wedding]` 为 `QuestGrade.Wedding`。因此“当前任务索引中没有 20737/20738”是错误结论，必须撤销。

### 4.2 `20737` 原文与投影

原始完整 dump 见 [quest-20737.txt](evidence/quest-20737.txt)：

- 名称：`准备结婚吧！ (1/2)`。
- Wedding grade，NPC/完成 NPC 均为 `20013`，对 NPC 可见。
- `[job] [all]`，等级 `30..99`。
- objective：`condition under clear`，subtype `22`，int data `-1 -1 5`。
- 玩家文案：与心上人组成 2 人队伍，通关普通地下城 5 次。
- 奖励：`10002616` 真爱考验箱 ×1。

当前解析存在两个精确缺口：

1. `PvfQuestDefinitionSource.cs:610-621` 只在 objective 为 `condition under clear`、subtype 等于 `6`、且恰有两个操作数时投影 `DungeonClearRequirement`。subtype `22` / 三操作数不会投影。
2. `:486-518` 对 `condition under clear` 用 stride 4 打包 trigger。`[-1,-1,5]` 不足一个完整 stride，因此 `channels.Count == 0`，初始 trigger 返回默认值 `1`，不会得到“剩余 5 次”。

所以此任务虽能被内容源读取，但“与指定另一人组成 2 人队、普通图累计 5 次”的身份绑定和进度都没有被现有通用任务实现表达。

### 4.3 `20738` 原文与投影

原始完整 dump 见 [quest-20738.txt](evidence/quest-20738.txt)：

- 名称：`准备结婚吧！ (2/2)`。
- Wedding grade，NPC/完成 NPC 均为 `20013`，对 NPC 可见。
- `[job] [all]`，等级 `30..99`。
- `[pre required quest] 20737`。
- seeking：`10002610` 心意娃娃 ×1。
- 奖励：`10002901` 心意通 ×5。

`10002616` 真爱考验箱静态掉落：心意娃娃 `10002610` 权重 `30000`、绝情娃娃 `10002617` 权重 `50000`；`10002610` 还声明合成材料 `10002617 ×5`。权重的归一化/保底行为不能仅凭 PVF 数字断言为完整概率分布。

当前 `ProjectSeekingItems`（`PvfQuestDefinitionSource.cs:546-567`）能投影 `10002610 ×1`，库存持有量驱动的 seeking 进度可复用；但资格入口仍有下一节的缺口。

### 4.4 现有普通引导任务

`20743` `爱情的升华` 也存在，完整 dump 见 [quest-20743.txt](evidence/quest-20743.txt)：

- grade 为 `common unique`，NPC `2`，完成 NPC `20013`，等级 `29..99`。
- 目标是拜访大司祭 `20013`。
- 奖励 `10003038` 爱的祝福礼盒。

它可作为普通任务列表中的引导挂钩，但未被 `weddingsystem.etc` 或 `20737/20738` 声明为硬前置。完成NPC20013在当前Underfoot area2中存在，初次调查的“town graph找不到”已撤回；任务显隐与交互仍待实机。旧活动任务 `20739` 标为 event 且 `exposed by npc = 0`，不适合作为当前常驻入口。

## 5. 当前服务端接任务路径

### 5.1 通用任务列表不会展示 Wedding

`QuestAvailabilityRuleService.cs:29-55` 先执行等级、职业、前置等 `MeetsRequirementsAsync`，再调用 `IsOfferableGrade`；`:183-210` 的可展示 grade 列表明确不含 Wedding。因此即使 `20737/20738` 已解析，它们也不会出现在通用可接任务列表中。这是设计过的“其他入口”类别，不是解析失败。

### 5.2 收到 `0x001F` 时可绕过等级/前置

完整调用链已经核对：

1. `GameProtocol.AcceptQuestType = 0x001F`；`ProtocolModule.cs:944-945` 注册 decoder，`:719` 注册 `AcceptQuestHandler`。
2. `AcceptQuestHandler.cs:33-56` 从选中角色会话构造 `AcceptQuestCommand`，没有调用 availability service。
3. `AcceptQuestCommand.cs:112-137` 读取任意 quest definition；只对 `Crack` 和 `Circle` 有专门门控，然后调用 `log.AcceptAsync`。Wedding 走普通路径。
4. `QuestLog.cs:85-129` 检查 Title/Crack、kill objective 可支持性、重复携带、是否已经完成、槽位；它不检查 `RequiredLevel`、`CompletedQuestPrerequisiteGroups`、职业或 NPC 可见性。

因此，**只要某个客户端或手工协议调用对 `20737/20738` 发出通用 `0x001F`，当前服务端会绕过等级和前置资格受理**。这是一条代码路径结论。原生 A21 婚姻 UI 是否会发这种通用任务请求仍是 Unknown，不能把“可手工触发”写成“玩家原生流程可用”。精确代码摘录保存在 [current-code-references.txt](evidence/current-code-references.txt)。

一期实施不能通过预先把 `20737/20738` 标记完成来绕过。可选策略是：

- 新增专用婚姻申请资格服务，明确检查双方等级、关系状态、组队身份和任务事实；
- 若沿用 PVF quest，则补 subtype 22 的双方绑定与 5 次通关进度，并让 Wedding 入口显式调用同一资格服务；
- 在通用 `0x001F` 入口补统一 availability/专用 grade 校验，避免手工 ID 绕过。

### 5.3 可沿用的挂钩与不能误称为实现的内容

| 挂钩 | 当前能沿用什么 | 缺口 |
|---|---|---|
| `PvfQuestDefinitionSource` | 能加载 Wedding grade、20737/20738、前置、seeking 与奖励 | subtype 22、双方身份绑定和专用资格缺失 |
| `SeekingQuestProgressDomainService` | 20738 接取后可按库存重算心意娃娃数量 | 接取资格仍可被通用 `0x001F` 绕过 |
| `20743` 普通引导任务 | 可从现有 CommonUnique 任务体系起步；NPC20013在Underfoot area2 | 任务原生显隐待实机；它不是硬前置 |
| `npc/chn_father.npc` 的 `[wedding] 0` role | 客户端内容保留婚姻 NPC 动作入口 | 当前服务端没有 Wedding NPC handler，原生 UI 是否依赖它待 A21 确认 |
| 物品 `[upgrade wedding ring]` / `[love point coupon]` | 客户端物品类型与规则数据可复用 | 当前服务端没有婚戒/爱心点状态机和 action handler |

仓内 `Wedding` 关键词在产品源码中只落到 `QuestGrade`、PVF grade 映射和可接列表排除；没有专用婚姻领域服务、协议 handler 或持久化关系。上述均是接入点，不是现成婚姻实现。

## 6. NPC、城镇与教堂的可达性

当前 `PvfTownDefinitionSource.cs:39` 从 `town/town.lst` 建目录，`:95-107` 只加载该列表的 town 文件。原始列表见 [town-list.txt](evidence/town-list.txt)。

### 6.1 当前 WestCoast

`town/town.lst` 选择 `21 -> WestCoast.twn`；其 area 1 指向 `Town/MirrorArad/WestCoast.map`。当前地图 `[NPC]` 只有 `355、1206、356、40`，没有 `20013`。地图中的若干数字 `20013` 全在 `[attacked monster info]`，不能当成 NPC 放置。

### 6.2 未选入的旧 HendonMyre

归档中仍有 `town/hendonmyre.twn`，它的 area 1 指向 `HendonMyre/Hendon_City.map`；该旧 map 的 `[NPC]` 确实放置 `20013 [left] 1547 201 0`。但 `town/hendonmyre.twn` 不在当前 `town/town.lst`，所以不进入当前服务端 town catalog。

因此准确表述是：**大司祭资源和旧地图放置仍在 PVF，但当前选择的 town graph 未选入该旧 town，MirrorArad WestCoast 也未放置他。**这不等于证明客户端求婚 UI 必须经 NPC，也不排除协议驱动的动态入口。

### 6.3 教堂与婚房

- **2026-10-16更正：**`map/church/church.map` 存在，内部放置 `20014` `chn_father_inside.npc`，其角色含 `[exit church]`。虽然没有独立教堂town条目，但当前`town/underfoot.twn`的area12..63引用`Church/Church.map`。初次报告将“没有独立town条目”误写为“不在普通town graph的area”，该推导错误。实际进入门控、仪式实例选择仍需A21取证。
- `101 -> CoupleRoom.twn` 确实在 `town/town.lst`；它指向 `CoupleRoom/CoupleRoom.map`，并声明 `[couple room] 474 234`。房间资源被当前 town catalog 读取，但进入资格、状态同步和协议尚未实现，不能把“资源被列入”写成婚房可玩。

### 6.4 当前Underfoot原生入口（2026-10-16补查）

初次调查遗漏了`town/town.lst`中`2 -> Underfoot.twn`的可达路径。当前服务端PVF复核：

- `town/underfoot.twn` area2指向`Town/Underfoot/Underfoot_Event.map`，该area等级门为17。
- 地图`[NPC]`明确放置`20013 [left] 2016 182 0`，不是attacked monster中的同号数值。
- `npc/chn_father.npc`名为“大司祭”，角色为`[item shop] 1208 [wedding] 0`。
- `etc/popupmenu.etc`包含`1013 申请订婚 npc 10`；另一个`26 求婚 event 0`条目不能直接当作始终可见的玩家右键菜单。
- protocol2客户端与服务端该地图的规范化dump逐行一致。此结论为ContentStatic/Confirmed，未证明NPC可见、菜单显隐或求婚已实机成功。

证据：[Underfoot town](../../marriage-proposal-20261016/town-underfoot.txt)、[广场map](../../marriage-proposal-20261016/map-underfoot-event.txt)、[大司祭NPC](../../marriage-proposal-20261016/npc-father.txt)。此前“当前town graph中找不到大司祭”的整体结论撤回；第6.1/6.2仅证明各自地图的局部情况。

历史操作参考：2015-02-05[游民转载官网指引](https://ol.gamersky.com/z/dnf/201502/520041.shtml)描述点击大司祭申请订婚，再输入名字或点选对象；2016-06-01[17173同期文章](https://dnf.17173.com/content/06012016/095012719_all.shtml)注明暗黑城广场。两者属于国服历史玩法参考，不升级为日服A21实机证据。

## 7. 爱之摩伽陀的下游前置

Dungeon `3541` `爱之摩伽陀` 在 `dungeon/dungeon.lst` 中存在，静态规则为：

- dungeon type：wedding couple；
- 最低等级 `30`；
- party count `2`；
- required item `10002609 ×3`；
- 不消耗疲劳；
- difficulty `2`。

`10002609` 文案进一步写明“只有夫妻 2 人组队才可进入”。这是 ContentStatic，不是当前服务端已有配偶校验。婚姻一期至少要向副本准入提供持久化、只读的双方配偶关系查询；不能只验证“两个人”或“都有邀请函”。

### 7.1 不存在婚前任务与 3541 的循环依赖

`20737` 的全部 objective 只有 subtype `22` 和 `-1 -1 5`，任务文件没有 `3541` 或其他具体 dungeon ID；原文明确说“普通地下城 5 次”。`20738` 只有前置 `20737` 与 seeking `10002610 ×1`。全量数值扫描也没有任何 `n_quest` 文件命中 `3541`。

反向检查 `3541` 的完整定义也没有 `[need quest]`、`20737` 或 `20738`，只有 wedding couple 类型、邀请函、等级和人数规则。因此静态 PVF 的依赖方向是：

```text
婚前普通图试炼 20737 -> 收集娃娃 20738 -> 建立婚姻关系
                                              |
                                              v
                           已婚双方 + 邀请函 -> 3541 爱之摩伽陀
```

`3541` 是婚后下游玩法，不参与完成婚前任务，不存在“必须先结婚才能打 3541，同时必须先打 3541 才能结婚”的环。完整副本 dump 见 [dungeon-3541.txt](evidence/dungeon-3541.txt)。

## 8. 未闭合问题

- A21 求婚 UI 从哪里打开，是否要求看到/点击 NPC `20013`。
- Wedding UI 是否会自动接受 `20737/20738`，或只把它们当资格事实查询。
- subtype `22` 的精确客户端/原服进度语义，尤其“心上人”是否必须等于最终求婚对象。
- ceremony tuple 末列 `10/20/30`、`noti end 60`、money gift charge `50` 的精确单位。
- 教堂进入、婚礼实例、掉线恢复和宾客列表对应的 wire 时序。
- 婚礼礼包与 avatar 的实例过期时间是否由协议/数据库动态附加。
- 性别限制是否存在于客户端 UI 或服务端协议规则；PVF 未声明不能作最终答案。

## 9. 可复现操作

以下命令从仓库根目录 `E:\Project\DNF\DNF-JP-86-Server` 执行。

### 9.1 构建探针与扫描器

扫描器源码引用 `Tool/PvfProbe/bin/Release/net10.0/Dfo.Pvf.dll`，因此必须先构建仓内 `PvfProbe`。它是最小调查工具，不是完全自包含的独立程序。

```powershell
dotnet build Tool/PvfProbe/PvfProbe.csproj -c Release
dotnet build Document/outputs/marriage-research-20261016/pvf/scanner/PvfMarriageScanner.csproj -c Release
```

`bin/`、`obj/` 是本地生成物，不需要提交；需要保留的是 `Program.cs` 与 `PvfMarriageScanner.csproj`。

### 9.2 运行全量婚姻扫描

```powershell
$pvf = (Resolve-Path 'artifacts/sao-ticket-20261014/Script.server.pvf').Path
dotnet run --project Document/outputs/marriage-research-20261016/pvf/scanner/PvfMarriageScanner.csproj `
  -c Release --no-build -- $pvf |
  Set-Content -LiteralPath Document/outputs/marriage-research-20261016/pvf/marriage-scan.tsv -Encoding utf8
```

扫描器会遍历整个 PVF 文件表，记录数值 `20737/20738/3541/20011/20012/20013` 与 wedding/couple/婚礼等关键词的上下文。

### 9.3 复核任务、主表和副本

```powershell
$env:PVF_ARCHIVE_PATH = (Resolve-Path 'artifacts/sao-ticket-20261014/Script.server.pvf').Path
$probe = 'Tool/PvfProbe/bin/Release/net10.0/PvfProbe.dll'

dotnet $probe lst n_quest/quest.lst 20737 1000
dotnet $probe lst n_quest/quest.lst 20738 1000
dotnet $probe dump etc/weddingsystem.etc 5000
dotnet $probe lst dungeon/dungeon.lst 3541 5000
dotnet $probe dump town/town.lst 2000
dotnet $probe dump town/WestCoast.twn 2000
dotnet $probe dump map/town/mirrorarad/westcoast.map 10000
dotnet $probe dump town/hendonmyre.twn 3000
dotnet $probe dump map/hendonmyre/hendon_city.map 10000
```

### 9.4 复算五份 PVF 的规范化资源哈希

```powershell
$probe = (Resolve-Path 'Tool/PvfProbe/bin/Release/net10.0/PvfProbe.dll').Path
$pvfs = @(
  'artifacts/sao-ticket-20261014/Script.server.pvf',
  '../DNFClient/Script.pvf',
  '../DNFClient-A21-debug/Script.pvf',
  '../DNFClient-A21-protocol2/Script.pvf',
  '../DNFClient-A21-upstream/Script.pvf'
)
$resources = @(
  'n_quest/Event/chn_2014_wedding_01.qst',
  'n_quest/Event/chn_2014_wedding_02.qst',
  'etc/weddingsystem.etc',
  'etc/weddingsystemavatar.etc',
  'map/town/mirrorarad/westcoast.map',
  'map/church/church.map',
  'town/town.lst'
)
$sha = [System.Security.Cryptography.SHA256]::Create()
foreach ($archive in $pvfs) {
  $env:PVF_ARCHIVE_PATH = (Resolve-Path $archive).Path
  foreach ($resource in $resources) {
    $dump = (& dotnet $probe dump $resource 20000 2>&1) -join "`n"
    $bytes = [Text.Encoding]::UTF8.GetBytes($dump)
    [pscustomobject]@{
      Archive = $env:PVF_ARCHIVE_PATH
      Resource = $resource
      DumpSha256 = [Convert]::ToHexString($sha.ComputeHash($bytes))
    }
  }
}
```

## 10. 证据文件

- 全量关键词/数值扫描：[marriage-scan.tsv](marriage-scan.tsv)
- 扫描器源码：[scanner/Program.cs](scanner/Program.cs) 与 [scanner/PvfMarriageScanner.csproj](scanner/PvfMarriageScanner.csproj)
- 20737 原文：[evidence/quest-20737.txt](evidence/quest-20737.txt)
- 20738 原文：[evidence/quest-20738.txt](evidence/quest-20738.txt)
- 婚姻主表：[evidence/weddingsystem.txt](evidence/weddingsystem.txt)
- 物品定义：[evidence/items.txt](evidence/items.txt)
- 爱之摩伽陀定义：[evidence/dungeon-3541.txt](evidence/dungeon-3541.txt)
- 城镇图原文：[evidence/town-list.txt](evidence/town-list.txt)、[evidence/town-westcoast.txt](evidence/town-westcoast.txt)、[evidence/town-hendonmyre-unlisted.txt](evidence/town-hendonmyre-unlisted.txt)
- 代码精确引用：[evidence/current-code-references.txt](evidence/current-code-references.txt)
- 客户端 PVF 对照：[evidence/pvf-resource-equivalence.tsv](evidence/pvf-resource-equivalence.tsv)
