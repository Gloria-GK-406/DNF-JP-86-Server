# #371 调查与实现内容判断

## 1. 证据与本轮边界

调查时间：2026-11-17。范围以 [issue 快照](evidence/issue-371.json) 为准，原 issue 为 [GitHub #371](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/371)。该快照仍为 OPEN，未分配负责人、无评论；前一轮只读查询未发现关联 PR，timeline 只有标签与 #372/#373/#374 的交叉引用。未从 issue 状态推断所有本地工作状态。

| 来源 | 当前身份 | 本轮实际核对 | 证据效力 |
|---|---|---|---|
| 本仓 | main `aae13ba824989b7b2b1d966f4a6fc53d7aff15ce` | CMD、codec、领域契约与实现、内容源、相关测试源码 | CurrentImplementation；不证明客户端效果 |
| dev PVF | `content:0.3.3-dev.1`，SHA-256 `a97132dda0970b3e0ce7c3de8052acf0be2664f52e26c819b1d8cb6d427cfcd2` | 重新解析43041个stackable；1836个有action，117种action；读取所有作用参数、代表文案及相关限制 | PVF内容事实；不能独立证明使用协议或完整玩法支持 |
| A21 内容客户端 | SHA-256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD` | 引用权威协议页的现有实帧和验收边界 | 以各条来源和置信度为准 |
| 当前 b1 | `debug:20261115.3` 的 full exe，SHA-256 `e13d6ea97bf81ae9f15249b639f69a5bbff578e64bb8c7ba64478a9ed93cbdfc` | 复读通用确认构包、动作复制、ACK成功/失败、客户端本地消耗判断窗口；搜索79种动作字符串 | ClientStatic；本轮无运行期读取跟踪或成功实帧 |
| 旧dump | `ClientEvidence/dumps/dnf-dump-20260912-222635-726-46724/DNF-memory-rva.bin`，SHA-256 `52a9d002eb15ab6301069a954d84b77a122b9b6928f8886a1b0653054428ac72` | 检查若干已知动作字符串，未命中 | 有限搜索结果；不作不支持的证据 |
| 上游 | `ServerS4A21` HEAD `1870c85860375f7232a022b6b1b98e78cf840e4f` | 任务券、异界次数券、跨界、伴侣传送等实现，0204与002C注册关系 | UpstreamReference，仅提供线索 |
| 第三方 | `Research/third-party-s4a21-v3.3/decompiled/DfoServer`；相关文件哈希见证据 | 宠物、期限券、幸运星、故事簿、塔券、觉醒冷却、公会与小游戏处理 | ThirdPartyReference，仅提供线索；且多处仅支持少数ID |
| 70参考 | `Research/1995chen-dnf/other/等级补丁/70/df_game_r`，SHA-256 `cf45f4e8514c3bf560dd322c146f8441249f6c28246ec8566dcf9ebc4e1b2eb7` | 本轮读取已有70专项调查，并对79个标签作原文件字符串存在性核对 | 70Reference；不移植16B请求或旧枚举 |

本轮没有启动/attach游戏，没有新抓包，没有读写当前玩家库，没有读取线上日志。当前客户端清单、活动开启情况及数据库路径不能由旧记录推定。原始盘点资料位于主工作区未入库目录，文件身份见 [source-manifest.json](evidence/source-manifest.json)；本任务以重新扫描和独立保存的scope为主要输入。

## 2. 本仓为什么拒绝，以及不能怎样修

[0204切片](../../../Server/src/Dfo.Server.Protocol/Game/QuestClearCoupon/QuestClearCouponSlice.cs) 当前只解19B完整体；动作58/77有独立判断。[CMD](../../../Server/src/Dfo.Server.Protocol/Game/QuestClearCoupon/GameQuestClearCouponCommand.cs) 先做形状、会话及槽位检查，再走遗忘之地、赫拉斯、疲劳或任务券路径；通用情况下只允许零参数，遗忘之地101例外读取当前房间坐标。任务券[规则](../../../Server/src/Dfo.Server.Application/Quests/QuestClearCouponRuleService.cs) 仅认10006007，赫拉斯[规则](../../../Server/src/Dfo.Server.Application/Inventory/EntryAllowanceItemDomainService.cs) 仅认490002847。

失败编码为6B，错误码2；已有A21静态结论是该码绕过提示跳表。这个拒绝保护资产，但用户看不到原因。现有日志有原始body捕获，部分失败有槽/原因，未形成统一的“物品ID+动作+拒绝原因”记录。

由此判断（CurrentImplementation / Confirmed）：

- 增加动作白名单不能自动增加效果；目标装备、宠物或活动参数非零时，还会先被现有形状检查拒绝。
- 不能把所有新增动作都当现有19B零参数变体；协议机器区列出17个构包点且存在路径分化。
- 不能把请求offset7当物品ID。物品必须从当前精确会话的权威槽位取得，再校验PVF动作及目标。
- 不能把未支持类型回成功并扣量；否则会把 #371 的“用不了”变成 #374 的“白扣”。

权威协议事实继续由 [0x0204](../../../protocol/game/client-to-server/0x0204.md) 维护。本轮静态摘录在 [client-static.json](evidence/client-static.json)，没有新增完整动作映射，故本轮未把候选枚举写成协议事实，也未升级 catalog 成熟度。

## 3. 原盘点与验收必须修正的地方

以下为现行PVF直接可复核内容，完整路径、文案和参数在 [scope.json](evidence/scope.json)，不是照标签名称推测：

| 物品/动作 | 观察 | 实现与验收影响 |
|---|---|---|
| 3330 `[open guide book]` | 文案为右键查看各地下城深渊派对所需物品数量 | 首先验证是否客户端本地UI；仅打开指南不能扣邀请函。`need material`也不能不分操作就自动扣 |
| 10006056 `[give goldcoin]` | 勇士徽章拼图活动 | 不能当金币增加；依赖活动、拼图状态与奖品 |
| 1121 `[add mileage]` | 文案为提升1000点决斗经验，action参数却为0 | 不能按参数0给0，也不能当商城积分；需要参数/模板到实际收益的依据，并与PvP工作协调 |
| 10003197 `[quest clear coupon]` | 完成一个已接主线任务，只给经验，需要券界面确认 | 与现有“不发奖的批量Epic清理”完全不同；须先取最终确认协议，验证取消不消费 |
| 10004717/10005343 一二觉券 | 明确有额外材料；二觉还有限制场景、hardcore等声明 | 必须核实职业成长和技能副作用，不能只清任务标记；额外材料不足不得扣券 |
| 2683466 随机疲劳药 | 文案为随机20~50、疲劳不足106；有int data与额外材料10003037×1 | 需证明随机表语法及概率；不得硬写均匀20~50或漏扣材料。文案中的历史删除日期不等于当前实例已过期 |
| 10000704 `[equipment purify]` | 物品名为异界气息清除卷轴，本仓已有0x00CC/0x00CD；同标签12个模板实际跨净化、清除与黄金形状 | 先核实专用路径和各材料子表，不能统一当清除或直接另加0204实现 |

79种动作中有264个模板声明`[need material]`，281个模板名称为空或仍是`name_...`占位；这些是本次819个模板内的计数。额外材料可能对应最终操作而不是打开界面，必须按客户端确认路径解释。占位文本不能作完整业务语义的证据。

本轮另重新读取`etc/amplifyitem.etc`（原始section值保存在pvf-actions.json的AuxiliaryScripts）：上述12个`equipment purify`模板中，4个在清除材料子表、5个在净化材料子表，均属于当前内容源读取范围；1个490001781在当前内容源未读取的gold purify子表；2个10004425/10099331未在上述子表找到。即4+5+1+2=12。这里只证明内容表覆盖9个及其余缺口，尚未证明12个真实客户端路径和成功效果，不能把整族都记为“0204未接”。

`[use action packet]`在部分物品为0、部分为1，部分缺失。已知10006007虽为0仍有0204实帧，因此不得单凭此字段决定CMD；需同时看客户端使用分支与最终发帧。

## 4. 客户端、70与上游的可复用边界

### A21静态结论

当前b1中，通用确认构包VA `0x01603C70` 写槽u16、容器u8、零u32、动作u32、零u32、零u32；VA `0x01605F7D` 读取物品对象byte `+0x528`，复制进确认对象后零扩展发送。成功ACK直接读取槽、容器、动作，失败读取槽、容器、详情。以上只约束已读路径，不覆盖所有动作变体。

成功路径还调用客户端本地消耗判定。当前[协议页](../../../protocol/game/client-to-server/0x0204.md)已更正旧专项文档对77的消耗分支解释，因此本方案不复制旧文“77不本地消耗”的推导。实现需回显正确动作，并发送权威绝对库存/清槽，实测防止客户端再减一次后显示错误。

对79个标签的ASCII与UTF16LE搜索在当前b1均未命中。本轮没有定位其字符串到枚举的完整转换链；不能由搜索结果断言玩法不存在、VMP一定是原因或动作枚举已恢复。下一步必须从定义动作字段到实例`+0x528`的写入者继续反向追踪，或采集运行期PVF加载/实际点击行为，见实现方案P0。

### 候选动作号

| 来源 | 候选 | 可用程度 |
|---|---|---|
| 已有A21协议记录 | 58疲劳、77限定任务券、99/101遗忘之地 | 各自证据边界见权威页；不能从一种物品推广同族全部 |
| 第三方 InventoryHandler | 64宠物转换、81觉醒冷却、88成长装备、105幸运星、106宠物成长、123光环栏、132期限券 | 仅线索；64/88/132的参照解析还对目标容器和槽设条件，不是零参数动作 |
| 第三方故事簿线索 | 135 | 本轮已核对故事簿到任务序列与接受流程，未取得A21实帧，枚举仍是原盘点候选 |
| 第三方 NativeMiniGameService | 150猜数字、154潜水机会、155潜水升级 | 仅该参照服务的分派，不证明现行A21活动入口可用 |
| 70 | bingo、疲劳等旧动作 | 70的0204请求为16B、动作u8；与A2119B/u32路径不兼容，枚举不得沿用 |

第三方多个处理器发送的ACK使用通用UseStackableAckBuilder，与现有A21静态最小读取形状并不完全一致；不能把它们作为黄金帧。其“支持某个动作”也常只认少数物品ID，不能满足本issue全族覆盖。

### 已有领域与真实缺口

| 领域 | 本轮核对的本仓入口 | 判断 |
|---|---|---|
| 任务券 | QuestClearCouponRuleService、QuestClearCouponSettlementService、QuestClearancePlanRuleService | 原子扣券、清理carried/tracked/cleared与投影可复用；当前限定无奖Epic策略不能推广成觉醒/成就/单任务经验券 |
| 宠物 | ICreatureInstanceDomainService、IPetInstance、PetInstance | 已有按UID找实例、加经验、进化；没有因此自动支持任意宠物模板转换、随机变更或跨界 |
| 绝望之塔 | DespairTowerDomainService、IDespairTowerDomainStore | 已有入场、进度与真实通关结算；进度存储TryAdvance要求真实run，不应伪造run给直达/速通券 |
| 异界/次数 | DimensionDungeonDomainService、UsageQuotas.cs | 现有读数/入场扣次可复用；读面不允许随意写计数，新增/重置应由原owner的事务事实与订阅处理。账号级次数不能塞到角色计数中 |
| 技能冷却 | OnlineCharacterRuntimeDomainService | 现存的是物品冷却，不是完整技能冷却账本。应先确定客户端战斗路径，不能拿技能洗点API替代冷却重置 |
| 装备 | IEquipmentInstance、EquipmentGrowthDomainService、Outworld、EquipmentRepair | 身份/模板、耐久与既有异界规则可复用；`change growth equipment`不等同杀怪累计的装备成长；期限转换仍需保留字段矩阵 |
| 光环栏 | AuraSkinSlotDomainService、AuraSkinSlotRuleService、0x0357切片 | 有开栏状态和效果，现规则仅认490700411；四张动作券需扩展内容规则，0204/0357共用一次状态变更 |
| 幸运星 | ItemUseEffectDomainService、AccountLuckyStarDomainService | 002C已按rent point增加账号余额；0204应复用同一能力和上限判断，不能新建第二份余额 |
| 故事簿 | 本仓未找到故事簿标签或对应序列目录；只有普通任务接受/暂停能力 | 需新增书到任务序列、进度与读书流程，不能将StoryPause类名当已支持故事簿的证据 |
| 婚姻传送 | MarriageDomainService、城镇放置/过区能力 | 已有关系、婚房等事实可复用；伴侣目标、区域可达及双人当前身份必须校验 |
| 公会/活动/攻坚 | 本轮检查Application/Abstractions及UsageQuotaFamily、现有玩法种类 | 未找到能完整承接矩阵所列公会、安徒恩和小游戏的owner与消费链路；这是本仓能力缺口，不能推成A21不支持 |

本仓已有测试覆盖0204已知形状与ACK、任务清理事务、光环栏只开一次、幸运星余额与上限等。阅读测试源码只说明这些覆盖存在，本轮未重跑服务端测试，也没有将其推广为新增79种动作的验证。

## 5. 范围与关单判断

- #372专用CMD、#373既有规则补全、#374通用002C白扣仍分开承接。若本轮实帧证明某种动作实际走专用CMD，保留#371矩阵行及证据，并明确移交/交叉验收，不能无记录删除。
- 盘点另显示`[add enter limit dungeonincount]`有两种遗忘之地船票（10093452、10147358，目标4000）以及已接的赫拉斯券；前两种并未列入#371范围表。本轮只记录范围遗漏线索，不悄悄把它们计入819或宣称已覆盖。
- 客户端界面存在但本服缺玩法，结果是“本服未实现”；活动没开启、当前点不出请求或缺资源，都不能单独证明A21完全不支持。
- 暂时拒绝可以作为安全中间态，但不能满足原issue“能用或有证据不支持”的最终条件。最终关单需逐行完成，或由用户明确调整issue范围/验收。

建议采用[实现方案](implementation-plan.md)的分批路线，并将只读UI不消费、额外材料按实际操作扣除、玩法缺口不冒充不支持作为方案评审时首先确认的规则。
