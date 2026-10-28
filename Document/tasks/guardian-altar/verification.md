---
kind: rks-verification
decision: manual_required
---

# 守护者祭坛候选验证

服务端玩法与生产接线已实现，候选版本0.2.10已发布到本工作区artifacts。
此记录的manual_required指A21完整玩法实机验收，**不表示仍停留在内容解析**。
玩家已确认首关简单正常，升级／设置等初步正常；死亡流程修复候选尚未ClientVerified，
没有完整WireCaptured、云端审核或CI结果，不关闭功能需求、不创建PR。

## 无可见敌军的诊断边界

死亡修复候选d92e4815仍有无可见敌军反馈：玩家确认主角、召唤阵和友军正常，完全没有敌军或攻击。
`death-fixed-20261028T091059Z/stdout.log`中run `2cf2e051-4bf3-44a6-8ff1-a65e39cf8727`为3511中等／25082，
加载后持续编码13条035D，没有进入Ending或发送失败结果。该证据不能证明客户端创建了敌军，也不能归因为死亡逻辑提前停波。
当前客户端没有加载NetworkHook，已有FrameCapture配置未产生记录；本会话PktMon驱动访问被系统拒绝，未取得线上收包样本。

诊断候选仅增加祭坛ready与spawn元数据：初始／后续actor序号、带符号解释、地图、等级基准、阶段和035D正文。
不记录登录口令或票据。正文由当前同一codec重建，仍不当作客户端收包或执行证据。
原生序号从0顺序分配，而当前实现复用普通房间随机基址；这是待核实的适配差异，尚不能认定为本次无怪根因。

## 死亡流程候选验证

普通失败不再发送空0361；角色死亡原因1、中心被毁原因0。死亡事件在受管写单元中登记Ending状态和事件后1000ms期限，
保留普通0028 ACK及0020通知，沿既有祭坛调度器发送0360，之后再开始50秒回城期限。
Ending阻止继续波次、召唤和能量操作；重复死亡和旧generation不会延长期限或重发结束。
原生依据及字段边界见[0360](../../../protocol/game/server-to-client/NOTI-0x0360.md)。

| 检查 | artifacts/guardian-altar中的证据 | 结果 |
|---|---|---|
| 故障回归RED | death-flow-red.log | 5项按预期失败，复现提前结算和空0361 |
| 死亡／复活／祭坛／退出／CMD与事务边界 | death-flow-application.log | 173通过，0失败0跳过；覆盖暂停中死亡、重复上报、旧回调、50秒回城、持有10个复活币时拒绝且数量不变，以及失败不改进度、奖励次数或资产 |
| 协议全量 | death-flow-protocol.log | 实际服务PVF，1813通过，0失败0跳过 |
| CI同分类Unit | death-flow-unit.log | 37+20+503+726通过，无失败；与以上检查范围有交集 |
| 完整Release | death-flow-build.log | 0警告0错误 |

未验证：死亡后普通复活窗口是否短暂出现、是否残留遮挡，以及新时序的A21实际观感。
原生仍有普通死亡广播，A21对结束原因0／1共用大分支，不将服务端回归通过等同于窗口已修复。
本轮部署必须有新的SQLite backup API快照、精确旧进程身份及19个默认TCP监听核对；动态结果以active-deployment所指readiness为准。

## 敌军数值、连续入场与快捷栏恢复

实机元数据记录见`default-20261028T070017Z/enemy-send-diagnostic-stdout.log`：
同一角色第1关简单三局分别编码2／2／3条035D。首局有角色死亡和0361／0360失败结算；
后两局玩家观察不到敌军，不能把出站编码记录等同于客户端接收或显示。
当前时间线可生成敌军，尚无证据表明后两局后台调度停止。

已修正普通035C的round=0、地图等级硬编码50，以及三种回城路径缺失Main库存快照。
地图第三列由误命名的RequiredStars改为BasisLevel，不再增加额外星数准入；难度仍由实际通关进度控制。
原生字段依据见[035C](../../../protocol/game/server-to-client/NOTI-0x035C.md)。
`entry-state-red.log`复现round=0，`level-inventory-red.log`复现基准50与缺失库存快照。
修复后：`state-fix-application.log`中祭坛与退出链36项通过，`state-fix-protocol-full.log`中协议1813项通过，
`state-fix-content.log`中当前PVF内容2项通过，以上均0失败、0跳过。
`state-fix-unit.log`中CI同分类Unit为37+20+503+726项通过；`state-fix-build.log`完整Release构建0警告0错误。
这些检查范围有交集，不累计为独立覆盖量；自动检查不证明实机三项症状已经消失。
后台诊断测试使用独立测试库及可推进时钟，等待真实托管源产出035D后再停止；它不是实机抓包。

## 入场模式与拒绝码修复验证

实机诊断确认3511请求mode=3，旧准入仅允许0；详细协议证据和证据边界见
[0010](../../../protocol/game/client-to-server/0x0010.md)。本次没有改玩家进度、PVF、版本号或客户端补丁。
当前修复允许祭坛模式0／3，并将通用前置状态拒绝由A21直播专用0x07改为0x13。

| 检查 | 本轮证据（artifacts/guardian-altar内） | 结果 |
|---|---|---|
| mode3故障回归RED | entry-mode-red.log | 原模式0的2项通过；模式3的2项因未创建run失败，准确复现 |
| 拒绝码回归RED／GREEN | entry-refusal-red.log、entry-refusal-green.log | 旧映射2项错误返回7；修复后13项通过，包含重试和实际编码字节 |
| 祭坛／准入意图／事务失败／CMD架构 | entry-application-regression.log | 85通过、0失败、0跳过，使用实际服务PVF；模式0／3完整入场、加载、战斗启动和回城，非法模式、练习及无大厅请求拒绝且不改进度 |
| 协议全量 | entry-protocol-final.log | 1813通过、0失败、0跳过，使用实际服务PVF |
| Unit分类 | entry-unit-regression.log | 37+20+503+726=1286通过、0失败；未运行Host/socket集成，不使用默认端口 |
| 完整Release构建／GM资源 | entry-final-build.log、entry-gm-build.log | 服务端0警告0错误；前端成功，保留既有体积提示 |

最终候选的实际PID、版本、程序集哈希、备份、健康和监听以
`active-deployment.json`所指目录的`running-process.json`及`readiness.json`为准。
初次修复验证使用25001／25011／25080／25081；当前端口授权与实际实例见部署记录。

## 初次默认端口部署验证

用户2026-10-28授权交接默认端口，初次候选二进制为91df1c2f；后续候选身份以当前清单为准。
最新玩家库一致副本在全网络关闭条件下兼容启动成功，未并行写玩家库。
当前PVF哈希为`01F1BE685D46647961DF65207A36304777DB20EF5A3D03C68EFAA92F8B0C5189`，
与初版不同，已保留文件并补核：祭坛相关85项、内容2项全部通过且0跳过。
本轮目录中的`current-pvf-application.log`和`current-pvf-content.log`为原始结果。
当前直接引用主干原始etc，脚本目录开启，原生过滤竞技频道后16条频道对应16个游戏端口；
另有频道7001、HTTP18080／18081，共19个监听、UDP0。公开频道地址也必须为7001，旧值25001已修正。
线上Channel握手／版本／脚本／Info验证中，ASCII版本为66，脚本4760字节与主干文件逐字节一致，
Info的16条名称、IP及端口与主干目录一致。未修改ASCII编码或频道etc。
证据见`default-20261028T070017Z/channel-wire-verification.json`和`readiness.json`；
新备份及核心资产不变证明见`player-before-pve-channel.db`、`live-data-check-main-channel.json`。
竞技端口冲突及现有过滤配置边界见部署记录；客户端频道显示、入图和完整玩法仍待确认。

## 初版玩法实现检查（21c1dfd7）

| 范围 | 证据 | 结果与边界 |
|---|---|---|
| 完整解决方案编译 | solution-build-final.log | 0警告、0错误；Release候选publish.log成功 |
| 与PR同分类的服务端Unit | unit-tests-final.log | 4个命中项目：37+20+503+726=1286通过，无失败；不等于云端CI已跑 |
| 祭坛与架构／执行器／调度边界 | lifecycle-tests-final.log | 当前实际部署PVF，153通过、0失败、0跳过；其中祭坛生命周期10项、实际协议入口6项 |
| 所有普通难度和生存 | GuardianAltarLifecycleTests | 36个普通难度组合真实PVF生成与结算、72首通星；生存3轮及99轮同中心／roomVersion连续完成，每轮一次奖励次数与防重账本 |
| 资产与请求边界 | GuardianAltarProtocolFlowTests、GuardianAltarLifecycleTests | 原生0392→0010→0025／0027→结果与50秒回城投影；真实洗星金币扣除、旧帧拒绝、成就发物与再次领取拒绝；回滚／重开持久化验证 |
| 协议全量 | regression-protocol-final.log | 1813通过、0失败；紧凑25B结果头、33B配置头加七组数组、地图初始13B记录、38B敌方生成、所有动作长度及注册验证 |
| Persistence全量 | regression-persistence-final.log | 484通过、0失败，包含112张表清单与重复初始化幂等性 |
| 当前部署PVF内容全量 | regression-infrastructure-runtime-final.log | 976通过、0失败、2跳过；跳过项保留原日志，不把它们计入通过 |
| 三份PVF祭坛读取 | content-tests-current-final.log、content-tests-research-final.log、content-tests-runtime-final.log | 三份哈希分别见README；每份2通过、0失败、0跳过 |
| GM前端构建 | npm ci、npm run build；候选manifest逐文件哈希 | 构建成功，发布wwwroot/gm与本次dist一致；依赖锁文件未改动 |
| 玩家数据迁移副本 | artifacts/guardian-altar/compatibility-result.json及启动日志 | SQLite backup API、integrity_check=ok，仅新增祭坛两表；账号、角色、物品、槽位逐行哈希不变；副本Host71项预热0失败，TCP／UDP均0 |
| 最终候选网络冒烟 | artifacts/guardian-altar/network-probe-result.json | 玩家备份副本启动，四个实际TCP端口完全匹配计划、UDP0；Launcher／GM ready、GM静态页200；自有助手已结束，发布默认配置已恢复关闭 |
| 格式与文档覆盖 | git diff --check；protocol-sync-final.log | 格式通过；协议对账仍18项既有缺口，祭坛新增identity均有登记 |

原始日志保留在本地任务目录，由.gitignore排除；候选、玩家备份及含运行身份的私有记录位于artifacts，不提交玩家数据。
测试数不可相加冒充独立覆盖：Unit分类与各项目全量、专项有重叠。

## 保留的广泛回归失败

常用客户端副本`DNFClient/Script.pvf`全量Application记录为6064通过、3失败；
Infrastructure为974通过、2失败、2跳过。失败身份与相关栈：

- `MarriageCapsuleItemUseTests.Corrected_wedding_guardian_ticket_can_be_picked_up_once_through_the_real_grant_pipeline`，
  `PvfEntryLifecycleContentSourceTests.Potions_and_packages_share_the_consumable_admission_category(10002915)`和
  `MarriageCapsuleContentTests.All_declared_couple_dungeon_independent_rewards_have_strict_inventory_metadata`：
  婚礼STK `[stackable type]`重复，栈根`PvfEntryLifecycleContentSource.TryRead`。
  该源文件未被本任务修改。实际部署PVF下婚礼拾取用例通过，Infrastructure全量也通过，
  因此不能将常用副本的错误描述为所有档案均不存在，也没有修改客户端PVF来掩盖。
- `DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`及
  `All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`：
  Dungeon7100 cell(1,0)缺少兼容Left入口地图，栈根`DungeonStructureRuleService.ValidateReachableMapsAsync`。
  实际部署PVF下仍复现。该路径源文件未改，失败发生在祭坛分流前的异次元内容解析，未归因或宣称修复。

证据：regression-application-final.log、regression-infrastructure-final.log、runtime-pvf-tests.log。
广泛回归不是全绿；最后的祭坛接线／过期帧修改由153项专项、协议全量及Unit分类覆盖。

协议同步检查18项为：CollectionBoxAck动态identity未解析；00CF、03CF、03D0、03E5–03E8入站，
00CF、03CF、03D0出站及0105、0333–0336、03EB、042B通知catalog缺口。
与本工作区最初保留的基线失败集合相同，不把对账退出1写成通过，也不修无关协议登记。

## 实机与交付边界

待核实的是当前A21的入口显隐、加载／场景、STK友方召唤、英雄技能、敌友碰撞、结果UI、
继续／选关及重登持久化。执行顺序见[实机验收](acceptance.md)。

当前PVF单位7000末行等级重复、NPC shop76为空，外部商城额外次数／付费星及道具洗星未接入；
具体范围见[实现说明](README.md#当前档案的边界)，不能把这些写成已实机通过。
0392配置头offset28仍为Unknown；035E未虚构推送、035F不作为普通能量同步依赖。

候选和迁移副本已具体准备，已按用户授权[启动实机验收服务](deployment.md)，新备份、迁移及正式数据完整性核验通过；当前等待玩家实机反馈，不并行写玩家库。用户实机明确通过前不创建PR；
用户通过、当前提交云端审核明确通过及CI通过前不合并，不创建tag或Release。
