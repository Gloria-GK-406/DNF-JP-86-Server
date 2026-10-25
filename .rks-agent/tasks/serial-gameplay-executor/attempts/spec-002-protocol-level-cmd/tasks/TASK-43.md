# TASK-43 — 副本入场归并

本 Task 承接 Plan P8 T-007，迁移 GameSelectDungeon（65552）与 GameMirrorDungeonEntry（66230），删除 EnterDungeonCommand、EnterBossTowerCommand、EnterIllusionTowerCommand，共五行台账。TASK-42 已通过独立快审并以纯准备 `360d6219`／完整迁移 `647a00ae` 本地提交，留下可复用的城镇离场算法，以及必须在迁移前验证提交后投影／scope 默认失败边界的经验。目标没有偏离；两塔入场是同一 SelectDungeon 入口的真实分支，按允许的局部依赖先处理，不称 T-008 或整套塔机制完成。

## Task Unit Contract

- Revision: 1；Dependencies: TASK-42；覆盖 RQ-001／003～009／013～015、AC-001～005／008。
- Outcome: 两个已注册消息号各唯一映射到完整协议 CMD（允许共用一个 CMD），薄 handler 只交付输入及精确来源；吸收普通／两塔入场编排，删除三条失去真实消费者的 fine。
- Boundary: 入场及其实际共用算法、原领域／registry 接缝、正式测试、五行 ledger、AGENTS清单和人工记录。不得注入旧 Delivery／Coordinator／bus 隐藏编排，不新增影子状态、raw、Self 或通用恢复许可。当前规则优先级、事务及受众边界不因归并而合并。
- Intentional change: 默认保持行为；正式旧新 oracle 证明的 scope／提交后默认失败差异追加 Spec 变化表随 Task 审，不另修 Spec／Plan。纯抽取独立提交，确实不可分的迁移与失败语义在 Result 说明。
- Result review: required；一个 sol6.1 执行与一个 fresh Astra 快审，无 Task 挑战或只取证 Task。

## Detailed Plan

1. **先钉旧行为。** 跑现有 PartySelectDungeonHandlerTests、DungeonEntry codec、BossTowerEntryOrderTests、IllusionTowerProjectionConcurrencyTests、塔 public flow／真实打线、Cycle／Heras／Hell／Tournament 入场等受影响正式用例。补最小真实 decoder→dispatcher→原领域事务／SQLite→actual encoder→production sender oracle，覆盖成功／真实拒绝、两成员当前性、原提交及回滚事实、普通／Boss全员与Illusion空保护、实际 fine Dispose 和提交后编码／视图失败。特别核失败在旧 fine 外与新 whole 内的差异，不能以旧 helper 静态 catch 或 fake bus 代替旧运行；同ID物理替换、cause、来源链与逐参与者隔离须有直接断言。
2. **保入口裁定。** session／current view 后先判 stale 再 Consume 意图；不匹配只对仍current恢复意图。保 Mirror 与 Directed／Cycle／SAO 配对、任务 facts、队长及全队校验、Boss优先、Illusion的非Mirror且非manual-hell条件；普通路由保 couples→goldSilverHorn→sealedTreasure→southern→heras→tau→altar→tournament→sao→despair→mirror→cycle→normal／hell 的原优先级。随机种子仍在原阶段生成，不用不稳定字面值作判据。
3. **保真实事务。** 三fine各原事务对应一 ordinary Write；普通／Boss保护原完整 attachments，Illusion原空selected，不凭功能名字新增footprint／Main。核普通拒绝的实际 RequireRollback／Committed 状态，不能把无run一概当回滚。领域 admission／临时绑定／票据与疲劳的原结算时点、normal-return回滚与后续内存动作保持；不回写共享runner，也不新增宽松默认保护例外。
4. **保普通入场算法与原registry。** attachments精确账号／角色／wire／连接唯一，原pre/post-currentness与#185保留。run／受众不一致按原 TryAbortEntry(runId, preparedRevision, recipientIDs) 逻辑撤销；SupersededSameRun拒绝但不误abort，不把逻辑撤销称数据库回滚。仅前后run不同才删除旧registry参与者；注册受众仍落同一DungeonRunDeliveryRegistry，可扩现有领域形状接缝，不能造第二份状态或搬无关kill缓存。保receiver-first stats、housing、ACK／state／UDP、教程延迟、各mode payload、物品扣除／地图／fatigue次序与晚读房间版本。XO-1成员先于请求者BestEffort，原失败受众关闭／abort范围及物理发送隔离保持，TownLeaveMany仍在全部入场batch之后。
5. **保两塔入场。** attachments／PartyCurrent、拒绝意图恢复、提交后原全附件隔离与原cause保持；Boss重读当前视图，Illusion只有11000重读临时绑定库存。票据／材料删除先于属性与各自tower info/map（Boss0333／0334、Illusion008E／008F），receiver-first、host槽及support／tower info／map顺序保持；XO-3／XO-4成员先于leader、随后共同城镇离场。入场编排由CMD拥有，同算法供真实共用点使用，删原测试fallback中临时构造Delivery／另一套handler的路径。直调fine的旧测试改实际whole或同算法驱动，保原强业务断言，并明示最小driver不等于生产链oracle。
6. **冻结验收与交接。** Release solution build、相关聚焦与Application／Protocol／Networking full；实际新增领域DI／接缝影响Host时跑Host full。所有正式dotnet首次即require_escalated、原类并行／项目串行／5m／detailed／独立DFO与TRX。逐display／status／重数对TASK-42最终四项目核N/D，只沿用原两Dimension及原skip；新失败保现场定位，不以复跑绿免除解释。五目标之外逐对象与行序保持，original不变；strict／八坏例selftest／diff-check及实际注册核验。Result一页列交付、非常规选择、结果和限制，停写交唯一fresh快审；主控消费后分段本地提交继续T-007。


## Result

- Outcome: done（执行轮R1；未提交，待唯一fresh Astra快审与主控接受）。
- Changed: 两消息65552／66230共用完整GameSelectDungeonCommand；吸收普通及Boss／Illusion入场，删三fine、旧普通Delivery和两塔Enter；保原registry、普通#185、各自事务／保护集合、mode优先级、三XO与共用TownLeaveMany。台账只五目标，AGENTS只增清单；正式旧新oracle与原强断言保留。
- Why: 不增Main／footprint／owners／raw／Self／恢复许可。whole scope覆盖原投影，与删除legacy调用不可分，默认失败差异明列VC-B13；entry捕获原physical修旧两塔误关replacement，现有precise-drop和两塔原IsInWorld保留，单列VC-B14。未将失败围栏等同物理关闭或撤帧。
- Checks: 原路由Protocol135/135；最终同11例oracle旧11/11、新11/11。最终r2 Release build零警告错误、App full5509＝5491pass＋2原Dimension失败＋16skip／focus16，Protocol1781／focus186；Net／Host取r1各203全过。App N12/D1（净增11及一条driver诚实改名）、其余0/0。DFO71＝67反例＋4control，无outside／exe／orphan；runtime两唯一消息路由／三fine注册0，五行保原／preclose／strict／八坏例selftest／diff-check通过。准确轮次、命令与限制见[核验摘要](../verification/task43-verification.md)。
- Remaining: 原Dimension7100缺map不在本Task修；原始TRX/logs留本机。最小driver不证明生产scope，完整oracle另列；CompleteView及受控transport不等于A21。其它塔fine、T007/T008与Spec B尚未闭合；已停止写入交审，不做外部操作。


## Independent Quick Review R1

**approved** — TASK-43 R1 局部结果通过；未发现阻断缺陷。审查对象为 `647a00ae` 加本次未提交实现及最终 r2 修正，契约 revision 1、Plan P8 T-007、Spec B6 的 VC-B13／B14；使用 reviewing-work 的 Task Unit／Code 判据。仅批准两消息与三 fine 的本次迁移，不代表 T-007／T-008 或 Spec B 完成。

```yaml
kind: rks-review
verdict: approved
```

- **实现证据：**逐读五个 CMD 文件、薄 handler／ProtocolModule 与旧 HEAD 的入口、普通 Delivery、两塔 Enter 差分。两消息绑定同一 whole，三 fine 注册删除；stale 先于 Consume、Mirror／Directed／Cycle／SAO配对、Boss 优先与 Illusion 非 manual-hell、普通 mode 路由顺序保持。三路各一次 ordinary Write，普通／Boss 全附件、Illusion 空保护；无嵌套 bus 或新增 raw／Main／Self。普通 pre/post cohort、SupersededSameRun 不 abort、TryAbortEntry 与旧 run 移除保持；Register 经既有接口落回原 singleton registry。receiver-first、材料在塔 info/map 前、Boss 重读与 Illusion 仅11000重读、XO-1/3/4成员先行及最后 TownLeaveMany 均与旧算法相符。
- **失败证据：**读 `DungeonEntryFailureBoundaryTests` 的真实 decoder／dispatcher、事务包装、actual encoder、生产 outbox 与逐 physical 断言。VC-B13 的同 cause、Committed、普通疲劳保留而 run 撤销，以及早拒绝 Committed／late ConfirmedRollback 有直接断言；scope 新三路 source／peer 各1 batch，未将 Fence 写成撤帧。VC-B14 捕获在附件阶段、cleanup 不重新在线查询；双替换 precise drop 与 query-only 原 peer close1 分别断言。两塔恢复的 IsInWorld 与原 PartyCommandPhases.CloseQuietly 一致；普通部分隔离仍无该 guard。共享关闭契约未改。
- **独立核验：**直接解析原始 TRX：旧11通过；r2聚焦 App16／Protocol186通过，full App5491通过＋2个具名 Dimension 原失败＋16 skip，Protocol1781通过；r1 Net／Host 各203通过。只恢复两塔失败清理 guard 后沿用这两项目的证据，范围合理，不能称同一最终轮或八项目全绿。直接比较 HEAD/current ledger 对象，仅 messages[18,134]、commands[81,84,85] 改动，original／其它对象／顺序保持；检查实际注册差分、runtime核验记录与 App N12/D1（11新例＋诚实改名），本次 git diff --check 通过。
- **限制与交接：**未重跑全量；最小 route／handler driver 只证明同算法，不替代独立11例生产链 oracle。受控 layout／CompleteView／physical transport 不证明 A21 或真实PVF全流程。Result 对 whole scope 与默认失败变化不可分的说明成立；纯测试准备是否先提交由主控决定。其余塔流程与簇尾验收继续留待后续。

## Controller Consumption R1

接受 fresh Astra 的 approved 结论及 VC-B13／B14。主控逐读实际实现、旧新故障断言、最终原始 TRX 与 DFO：App／Protocol 取 r2，Net／Host 取 r1，合计7696＝7678通过＋2个原Dimension失败＋16跳过；App N12/D1，其它0/0，DFO71且反例／对照之外为0。独立重跑strict及八坏例selftest通过，五个台账对象之外、全部original与行序保持；runtime原始记录证实两消息唯一映同whole、三fine已无注册，diff-check通过。

接受两塔原IsInWorld守卫恢复后的证据轮次界限；精确cleanup保现有contexts准入，不扩大关闭已移除旧peer的权限，也不将Fence等同撤帧。先将已在旧路由通过的正式故障测试作独立准备提交，再提交完整迁移与人工记录；产品归属迁移与whole生命周期默认失败语义不可分的理由成立。仍只授权本地检查点，不推送／PR／部署；继续T-007的加载完成、换房及实际关联塔阶段入口。

纯测试准备已本地提交为 `7f29f204`，只含正式故障测试；完整实现保持同一最终受审内容。
