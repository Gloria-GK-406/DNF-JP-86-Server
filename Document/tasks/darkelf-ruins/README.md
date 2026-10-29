# 暗精灵遗迹（#75）

范围为 DGN5006–5009、5016–5019、5026–5029，对应 [#75](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/75) 和 [#170](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/170)。
工作区基线 `main@63fb3993`，开发分支 `codex/darkelf-ruins-75`。
候选版本 `0.2.10` 已完成服务端自动验证，并按用户授权在默认端口接管原玩家库。尚未取得本次 A21 实机验收，未创建功能 PR。
连接方式和具体操作见 [实机测试流程](user-test.md)。
具体结果与保留边界见 [验证记录](verification.md)。

## 现行规则与所有权

- 当前 PVF：世界地图 38，40 级，外传任务 2656；艾丽西亚 NPC385、商店165。十二张 DGN 的普通迷宫与守护者迷宫均按档案索引选择，不将随机普通迷宫当作守护者概率。
- 周一/四神殿入口，周二/五虚妄迷宫，周三/六神殿中心，周日全区域。每角色共用每日两次，采用本服统一日切；黑色小晶块费用分别 10/20/40/80。入场、次数、月计数和材料同一事务提交，失败回滚。
- 三个区域共享星级。按用户本次确认的历史日服口径：当前星级先完成 1/3/7 次通关，再单独完成一次未使用复活币的通关后升星；前置累计不要求连续，使用复活币不清空已累计次数。低星回刷不推进更高星级进度。
- 守护者仅在配置指定星级抽取；月入场序号决定万分比出现率，每角色每月最多出场两次。一支队伍共用迷宫，各成员均须尚有次数/守护者额度，出现率取成员可用概率的最低值，防止带队绕过个人上限。这是本服组队裁定，尚未取得原版组队抓包。
- 每人每局最多支付四枚复活币，由副本实例在提交后累计，换房保留，新局清空；给队友复活计入付款人的上限，使用复活或支付复活币均使本次通关不能推进无复活升星。卡洛夫 `61379` 原始地图声明带 `[dummy]`；遗迹规则将其设为房内清场所需的敌对战斗对象，保留原始声明，不修改所有 dummy 的通用含义。
- 守护者迷宫的雾、艾丽西亚门及终点对象按当前 DGN 的 `[randomized object creation]` 冻结，并在 START_MAP 投影；重访按 `[regenerate]` 决定是否再投影。门脚本明确提供清掉前房后绕过守护者房的单向路线，服务端只接受当前迷宫声明的源/目标，未清源房不能传送。绕过不算击杀守护者，不给额外曜日材料或装备。
- 怪物随角色等级调整，入场与移动不耗疲劳，不给普通怪物/结算经验。通关须使用服务器当前局、当前房间及真实死亡清场凭据，客户端成绩不作为发奖依据。
- 通关材料与曜日奖励从 ETC 读取；实际击败守护者后增加一份曜日奖励。按用户本次确认的服规，额外装备采用万分比原权重：三星合计 6400/10000，四星 6700/10000，剩余区间不出额外装备。该分母是本次决定，不能标为原版已证实规则。
- 奖励在 Boss 清场事务内冻结并去重，专用结果流程后领取，背包不足走现有邮件后备。退出/断线后未领取的冻结奖励在重登时交付，不重抽、不重发。NPC 兑换复用现有商店和物品服务，不增设第二套扣材料逻辑。

静态内容唯一来源为 `PvfDarkElfRuinsContentSource`；规则由 `DarkElfRuinsRuleService` 解释；
`DarkElfRuinsDomainService` 编排准入、进度与奖励，持久化只通过 `IDarkElfRuinsDomainStore`。
星级、次数与月状态在 `darkelf_ruins_progress`，冻结奖励与领取/展示状态在 `darkelf_ruins_rewards`。
通用准入、拓扑验证、房间实例、账号材料容器及物品交付继续使用既有 owner。
协议状态、字节布局和证据只维护在 [流程记录](../../../protocol/patterns/flow-darkelf-ruins.md)。

## 来源与适用边界

| 标签 | 来源 | 支持范围 |
|---|---|---|
| ClientStatic | 本任务 `evidence/license-builders.txt`、`license-handlers.txt`、`boss-preamble.txt`；脚本采用 mmap 与有界反汇编，不保留整镜像指令对象 | 构包/读取宽度及调用点；不证明界面操作已通过 |
| ClientStatic | `evidence/pvf-license.txt`、`pvf-worldmap.txt`、`pvf-entry-quest.txt`、`pvf-shop.txt`、`pvf-guardian-map.txt`、`pvf-guardian-monster.txt` | 当前档案的入口绑定、轮换、费用、奖励、守护者声明 |
| 70Reference | `evidence/70-symbols.txt`，70 级 `df_game_r` 的 `.symtab`，按 darkelf/historicsite/temple/license-dungeon 名称有界查询 | 本查询未找到对应符号；不能用旧版本普通副本规则证明该玩法 |
| 上游参考 | `E:/Project/DNF/ServerS4A21/Server/DfoServer/GameWorld/LicensedDungeonCatalog.cs`、`Game/Dungeon/LicensedDungeonService.cs`、`LicensedDungeonRewardService.cs`、`Network/Builders/Dungeon/LicensedDungeonPacketBuilder.cs`、`Network/Handlers/Dungeon/DungeonSettlementHandler.cs` | 业务与专用流程参照；该目录没有可读取的 Git 提交身份，用文件 SHA256 固定输入 |
| 历史说明 | [2016-08-03 日服上线公告转载](https://www.4gamer.net/games/030/G003013/20160803103/)、[当期官方竞速说明](https://arad.nexon.co.jp/campaign/2016duel_tournament_timeattack.aspx) | 历史玩法与三星守护者入口；不替代当前客户端证明 |
| CurrentImplementation | 本工作区源码及本任务测试 | 仅证明本仓实现与自动覆盖范围 |
| WireCaptured / ClientVerified | 本次未新增 | 等待用户实机验收与双向抓包；不引用上游自测为本仓实机证据 |

客户端 EXE SHA256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`。
探查档案：`E:/Project/DNF/DNFClient-A21-protocol3/Script.pvf`，SHA256 `D45CF58B1A2B3D16D6B35ABE0D4E302E052F0B6BC43E9D3E98D815FED54AEF84`。
实际主干进程使用 `E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`，SHA256 `3822A9ED183CD17F74DFEE9C11B8F2E84AF596B23972A737846022148FB05C6B`；遗迹 ETC 表一致，最终真实内容测试与候选包使用此活动档案。`evidence/pvf-guardian-dgn5008.txt`、`pvf-alicia-activation.txt`、`pvf-alicia-portal.txt` 记录对象、源房清场条件及 `(0,0)→(2,0)` 的脚本 MOVE MAP。
静态内存镜像 SHA256：`6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`。
上游四个核心文件依上表顺序的 SHA256：

```text
LicensedDungeonCatalog.cs       823DE79EAAC1A8C208A8FB6FE566928FED2357F304DB08D0A6AA11F13B8C9B8C
LicensedDungeonService.cs       E2C2F97BB96C2A12AA3CF53975E4440017D8070EC700528E3E57573DB6B1E343
LicensedDungeonRewardService.cs FAB0A036CFBC2C926B30AAEDA96A93A9D7A5B62F86958014E03D59DAE1588FA4
LicensedDungeonPacketBuilder.cs C15284EB6195769DB4A89625EBF0A6B931149B2104C4A3DD8C1E5292FF04D5A4
```

## 验证与验收顺序

自动验证使用真实只读 PVF 和测试夹具独立临时库。日志、TRX、监听快照放本工作区 `artifacts/`，
不使用玩家库。首次还原受沙箱限制，没有形成有效 RED 证据；后续测试只能按实际执行结果计数。
验证覆盖目录和所有迷宫、星级规则、材料消费/回滚、日切、领取去重与补领、守护者清房、复活上限、NPC 兑换及专用协议形状。

用户测试部署前重新核实主干实际进程、启动参数、配置覆盖与数据库绝对路径；
SQLite backup API 制作一致副本并验证后，才协调唯一玩家库写入时段。
覆盖共享客户端、PVF、插件不属于服务端部署。默认端口的现行服务已取得用户明确关闭授权。
用户测试使用游戏10011、频道7001、启动器HTTP18080、GM18081，登录端点保持10128/17200。
配置明确关闭 `Channel.UseScriptCatalog`，频道列表只含 ID11/port10011，实际监听已核对；
默认脚本模式会按 `10000 + channel.Id` 替换游戏端口，不能只修改 `Networking.GamePorts`。
UDP discovery 使用固定端口，本候选沿用现行部署将其关闭。独立副本探针仍用21000/21011/21080/21081并在验证后停止。每次启动前均须重新核验。

已从现行进程、启动参数与配置覆盖重新确认实际数据库：
`E:/Project/DNF/DNF-JP-86-Server/Server/src/Dfo.Server.Host/bin/Release/net10.0/Data/dfo-server.db`。
当前遗迹实例、上一服务的产物/配置及恢复身份只在忽略的 `artifacts/darkelf-runtime/cutover-20261029/` 保留，不用主干检出身份代替实际运行产物。候选实现来自 `d2eecd96`，未合入其他活动的候选实现。
该库已通过SQLite backup API制作最新一致快照，副本完成迁移与完整性检查；关闭旧服务后又制作最终快照，再用同一实际库启动遗迹候选，只允许一个Host写入。
恢复时使用记录的原产物和配置启动，保留同一实际库及测试期间正常新增的资产；备份只用于明确的数据恢复决定，不自动回灌旧快照。

1. 使用当前主干库中 40 级以上角色，记录小晶块、疲劳、经验、遗迹材料；未完成外传2656时确认入口/准入限制，再正常完成对应外传。
2. 从暗黑城门进入原生遗迹选图，确认当天区域、三组星级、每日剩余次数；在关闭区域、未解锁星级、材料不足时确认拒绝且不扣费。
3. 正常进入当天一星图，核对费用、怪物等级、机关、逐房移动与专用结算界面；确认不耗疲劳、不增加经验，不出现普通翻牌商店。
4. 完成第一次通关后退出并重登，核对共享进度；第二次无复活通关后确认三组共同升到二星，奖励与材料只增加一次。
5. 当日第三次入场应拒绝；更换角色检查独立次数，06:00 后检查轮换及次数恢复，星级不重置。
6. 分别检查正常退出、未通关断线、通关未领奖断线和重登；入场费用不返还，冻结奖励补发一次，背包不足走现有邮件。
7. 三星/四星出现守护者时检查雾、艾丽西亚门和战斗对象，分别验证进房击杀及清前房后传送绕过；仅击杀后增加额外曜日奖励和装备机会；单局第五次支付复活币拒绝且不扣币。星级与随机边界可在独立副本库验证，不能为夹具修改玩家库。
8. 在艾丽西亚处兑换圣物碎片，核对材料消耗、重复购买拒绝和勇者礼盒兑换；再检查普通副本、组队换房与退出回城没有回归。

组队、跨日/月、实际守护者出现、地图内传送/机关、断线重登及界面字段仍需按以上步骤取真实客户端证据，
自动构造请求或合成房间不能冒充这些场景已经通过。
