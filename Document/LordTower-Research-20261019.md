# 领主之塔：信息收集与实施边界

日期：2026-10-19。代码基线：`f952ade840b4c32d325a41653ac147db715cae86`，分支 `task/next-round-20261019`。对应 [#165](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/165)，顺序见 [#170](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/170)。本文件记录调查结论，不代表已实现、已部署或已实机验收。

## 结论

当前资源保留 DGN3505 的选图槽和专用塔协议，但真实客户端入口是否可见、可点击尚未确认。应先核定入口，继续遵守“当前客户端有入口的活动地图优先，历史活动另列”的范围。

3505没有普通迷宫布局，需要独立八阶段生命周期；不能只照藏宝地注册入口。建议以当前PVF的地图、门票和奖励声明为内容依据，复用既有准入事务、房间演员、掉落、发奖和商店；以A21静态消费链核定协议，70实现只作行为参考。开放策略、补票日切、塔模式疲劳与退出规则需要在实施设计中明确。

## 当前PVF

档案：`artifacts/scheduled-notice-pvf-20261019/Script.server.pvf`。

SHA-256：`C5CCE5346F60D21128D756A086EEF96E70F75F2F2FE063B6C61C00506C588F06`。以下为当前资源静态事实，不是ClientVerified。

| 项目 | 证据及含义 |
|---|---|
| 入口资源 | `worldmap/towers.wdm`的`[dungeon]`包含`3505 -1`，DGN选图槽为`1 4 WorldMap/SelectdungeonSlot/tower_cn.img 0`。这证明资源保留入口，不证明地区、活动等过滤后实际可点。 |
| 副本定义 | `Dungeon/Towers/TowerOfBoss.dgn`，类型`tower of boss`，最低60级、basis60、推荐60～86、固定难度0、禁止练习，schedule2329。 |
| 入场材料 | `[required item]2749110 1 1`，邀请函每位参与者消耗1张；物品堆叠上限5。 |
| 普通迷宫缺口 | 没有`[maze info]`；当前`PvfDungeonDefinitionSource`在没有maze时返回null，普通Maze路径不能直接运行此图。 |
| 疲劳/退出 | 没有`[no fatigue]`、`[spicies dungeon]`、`[no giveup panalty]`。不能据字段缺失就裁定专用塔模式一定耗疲劳或增加虚弱，也不能直接声称已有PVF豁免。 |
| 特殊字段 | `tower max clear item num=10`、`tower fp cubepiece=0`、`tower limit of stackable item=0`。后两者语义仍待核定，不能把0直接解释为无限或禁止。 |

八阶段加权MAP池，共29张候选；每阶段权重合计10000：

| 阶段 | MAP:权重 |
|---|---|
| 1 | 25044:10000 |
| 2 | 25045:3330，25046:3330，25047:3330，25048:10 |
| 3 | 25049:3330，25050:3330，25051:3330，25052:10 |
| 4 | 25053:3330，25054:3330，25055:3330，25056:10 |
| 5 | 25057:3330，25058:3330，25059:3330，25060:10 |
| 6 | 25061:2000，25062:2000，25064:2000，25065:2000，25066:2000 |
| 7 | 25067:4995，25068:4995，25070:10 |
| 8 | 25071:3330，25072:3330，25073:3330，25074:10 |

候选MAP已逐个提取；多只领主以normal怪物声明。阶段推进应检查整层战斗完成，不能把任一Boss死亡当作整场通关。地图均保留NPC1010；其交互仍需在专用运行态接通。

## 门票补给与奖励

`etc/chn_server_limititemusageinfo.etc`中有邀请函2749110的补给：数量3、Mode0、截止`2030-01-01 06:00:00`。只读探针实际调用现有`PvfEntryLifecycleContentSource.GetDailyRefillRulesAsync`，确认其能解析为补足3张、物品类型4、目标任务栏Space5/Index0～55，不只是文本推断。

当前`DailyRefillDomainService`按持有量及待收邮件中的数量补足，并做每日幂等；但2749110不在`DailyRefillRuleService.UsesIndependentPeriod`的独立刷新名单，现走UTC+8午夜普通批次、选角触发，不是06:00在线刷新。GM可禁用或覆盖规则，本次未读现服DB，未确认实际Effective状态。**补足3张不等于独立硬性每日限3次**，也不等于每日累加3张；不应另加重复额度而不核定。

`etc/global_bosstower.etc`声明：

- 金币权重11；阶段经验权重`1,1,2,2,3,3,4,4`。
- 阶段奖励卡数量`1,2,3,4,6,8,9,10`。
- 怪物经验权重1、APC经验权重3，奖励物品参数`50 49 100`。

这些是配置值，精确公式、概率单位、抽取时机和中途失败奖励仍待核定，不能直接套用金角银角的零经验。

材料2749094有独立掉落，且位于autorooting名单；自动入包还是地面拾取需要核对客户端行为。NPC1010沉默之克雷娅关联商店1118：10碎片换1结晶2749095，5结晶购买亡灵宝箱2749096，5碎片购买密语2749097。宝箱有3～20结晶的加权奖池；通用商店`[need material]`只用于购买，不应开启时再次收费。另有NPC72的25碎片换宝箱日常任务资源，任务可达及组合grade解析尚未核定。

## A21专用协议

ClientStatic：接收器RVA分别为`00D4E590 / 00D5BA20 / 00D47910 / 00D47C00`；字段结构来自静态读包与A21自身加密日志解码。没有本轮WireCaptured或ClientVerified。

| 消息 | 已闭合结构 | 仍需核定 |
|---|---|---|
| NOTI0333 | 固定8B：`u32 dungeonId + u16 maxStages + u8 type + u8 difficulty`。`maxStages`语义得到70的`getRandomTowerMaxStage`交叉支持。 | type具体枚举及实际入场ACK顺序。 |
| NOTI0334 | `u16 stage + u32 seed + u32 mapId + u8 monsterCount`，每怪物14B，再`u8 itemCount`，每初始地图道具18B；总长`12+14M+18I`。第二组是道具，不是APC。 | 初始道具尾部u16语义、完整运行消费约束。 |
| NOTI0335 | `u32 exp`，随后固定8个奖励组，每组`u8 count + count×(u32 itemId + u32 addInfo)`；总长`12+8Σcount`。 | 固定8组的具体业务映射；不能据数组数量认定支持8名玩家。 |
| NOTI0336 | 固定1B，`isRetriable`，影响结果页重试按钮。 | 哪些结局允许重试及对应资格策略。 |

0334怪物项：`u32 listIndex,u16 uniqueId,u32 monsterId,u8 level,u8 type,u8 isBox,u8 boxIndex`。地图道具项：`u8 scriptLine,u16 uniqueId,u32 itemId,u32 data,u16 durability,u8 amplifyType,u16 amplifyValue,u16 unknown`。

**旧命名纠偏**：当前`BossTowerBaselineMessage`实际发送NOTI021F；当前协议注册将021F识别为`CARD_BOOK_INFO`，是数量及数组结构，不是领主塔0333的短包。不能通过改编号直接复用这四字节，也不能据类名认定领主塔初始化已实现。

可复用消息候选包括`0010 SELECT_DUNGEON`、`0025 FINISH_LOADING`、`0027 DIE_MONSTER`、`002D MOVE_MAP`、`0048 EPLP`；最小握手候选为`0010 → 0333 → 0334 → 0025 → 001E`。A21专用002D实帧与ACK顺序仍Unknown。CMD04AE的名称不能证明其属于DGN3505。

## 70参考与地区差异

70输入为`Research/1995chen-dnf/other/等级补丁/70/df_game_r`，SHA-256为`CF45F4E8514C3BF560DD322C146F8441249F6C28246EC8566DCF9EBC4E1B2EB7`。以下为70Reference，不直接采用其opcode或数值：

- `CBossTower::checkStartGameCondition` `08142FA6`检查开放、每位有效成员等级及材料，不能只检查队长。
- `handleKillMonster` `08143EBC`在整层怪物清空后记录阶段完成；非最终层不会直接切层。
- `CBossStage::handleMoveMap` `0814F66A`调用`moveNextStage` `0814D1B6`，检查怪物表为空、未达最大阶段后加一，再随机下层MAP并发送。故推进候选是“清怪→MoveMap→下一阶段”。
- 加载按全员Ready聚合；90/120秒是Ready超时，不是已证明的战斗限时。
- 最终层或全灭进入专用收尾；`onClearBossTower` `08144508`包含生成奖励、发送结果、实际发经验/物品及EPLP。不能只发送空结果包。

日服历史对应“深淵の闘技場”，其开放日期、人数和兑换奖励与国服资料不同；[2011年日服官方公告](https://jp.nexon.com/news/120318?device=pc)仅作JP历史参考。[2014年日服官方大转移说明](https://arad.nexon.co.jp/campaign/2014/great_transition/sub04_4.aspx)明确该玩法当时暂时结束；这也不能直接证明当前混合资源的3505无入口。

[2012年国服媒体专题](https://dnf.17173.com/act/2012chap2/lz01.shtml)和[同期报道](https://games.sina.com.cn/o/n/2012-01-20/1512576152.shtml)支持无疲劳、碎片兑换等历史玩法，只标CN Reference；不能拿日服旧开放时段、人数或国服报道替代当前PVF和A21证据。

## 实施方向与下一步

1. 先验证亡者峡谷3505选图入口及活动过滤，再采一次正常入场、第一层加载和清怪后走门。若入口不可见，先查地区与schedule gate，不扩大为恢复历史活动。
2. 建立专用BossTower内容与运行状态机：入场、加载、战斗、整层完成、推进、第8阶段终局、失败/退出；冻结运行种子与抽图结果，并保证重复死亡/移动/领奖不重复推进或发奖。
3. 复用既有准入扣材料事务、房间演员/掉落、奖励发放、NPC购买和包裹开启；专用定义必须保留等级、活动和每人扣票约束，不能为绕过普通maze解析而放宽资格。
4. 借鉴死亡/迷妄塔的会话代际与房间事务模型；绝望塔每日一层的持久进度不能直接当单局八阶段模板。
5. 实施前明确常驻开放策略、补票周期与在线刷新、组队范围、疲劳/零疲劳进入、退出虚弱、复活/消耗品限制，以及奖励公式和固定八组的映射。保留未知，不从名字或默认值推导规则。

## 原始证据

- [当前PVF探针与摘要](../artifacts/lord-tower-research-20261019/pvf/research.md)，同目录有DGN、29个MAP、全局奖励、补给实际加载结果和NPC转储。
- [A21协议报告](../artifacts/lord-tower-research-20261019/protocol/report.md)，同目录有`client-strings.json`、接收器与module进入链反汇编。
- [历史及70调查](../artifacts/lord-tower-research-20261019/reference/findings.md)、[70有界反汇编](../artifacts/lord-tower-research-20261019/reference/70-boss-tower.txt)。

本次三个子代理只读调查；未改玩法源码、客户端、PVF、现服或数据库。以上原始探针目录按既有artifacts策略不进Git，需与研究文档一起保留。
