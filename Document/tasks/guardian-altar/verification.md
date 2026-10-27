---
kind: rks-verification
decision: manual_required
---

# 守护者祭坛候选验证

服务端玩法与生产接线已实现，候选版本0.2.10已发布到本工作区artifacts。
此记录的manual_required指A21实机及唯一玩家库部署协调，**不表示仍停留在内容解析**。
尚无本任务WireCaptured、ClientVerified、云端审核或CI结果，不关闭功能需求、不创建PR。

## 自动检查

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

候选和迁移副本已具体准备，下一步仅协调[部署时段与精确切换目标](deployment.md)，
不擅自停止其他功能的测试服务、不并行写玩家库。用户实机明确通过前不创建PR；
用户通过、当前提交云端审核明确通过及CI通过前不合并，不创建tag或Release。
