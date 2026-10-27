# TASK-59 验证

五条消息65994／65995／66790／66791／66792迁入三个唯一whole CMD。Joust三个fine和BeginTournamentRewards的类型、handler、注册消除；SelectTournamentReward保留真实TournamentRewardSweepService来源，EliminateTournament保留真实PartyWipe来源。账只闭五message和四command，后两command只补保留说明；其余行、ID、original与顺序不变。

锦标赛保原预捕state／slot、Status单写提交期限、Select单写两行奖励及CompletionFrames→SelectionResponse。新增三个真实注册decoder→dispatcher→SQLite→encoder业务显示：Status期限提交与重复不延长，金币／物品更新先于选择ACK，重复选择不二奖、非法行拒绝；无run时完整Status／Selection失败体。旧产品task59-old-green-r4三个显示全绿，纯准备补丁在`/private/tmp/task59-old-tests.patch`；首次搭建的off-executor与将delta误判为snapshot两个测试观察错误均在旧产品上纠正，没有据此修改产品。

Joust查询保持ReadOnly及ACK→Bets→Info／history-only，无阶段公告。Bet的槽位映射仍在Main前；唯一Main以WriteResult复用原业务异常确认回滚ACK6，catch只覆盖BetAsync，footprint与request guard不转拒绝。首次成功仍DetailsChanged→独立Write ReadReplayState→当前态sink→current→ACK，精确metadata／physical，未授raw。复用既有持久幂等、同序号冲突、ushort回绕与真实SQL ABORT回滚重试。CWT与Writing断言随真实状态主人迁到whole；判据和反例保持，原单个off-executor记录仍在。

基础设施失败统一VC-B40。原Native测试健康链继续验证同seq不二扣、冲突body、回绕、释放拒绝；其原prepared batch失败段放在同一测试末尾的独立fixture，精确守PostCommitPublicationFailure、原IOException同对象、扣币一次／唯一持久行、Committed原entry／Ordinal、精确actor围栏及physical关闭、无ACK、后续不二扣。没有手工清围栏或新增raw。task59-focused原210显示有208pass和上述旧IOException期望／已删Main名单两项适配失败；完成精确迁移适配后task59-focused-r3 41/41，包含原Native三例与全部架构／注册判据。

仅新增既有IReplayStateProjectionSink与ISelectedSessionSnapshotQuery的具名注入许可，理由和正例在架构判据，伪SnapshotQuery及原持久化／Registry／总线拒绝反例保留。主控裁定在phase2-backlog。无新API、nested bus、临时ambient、Task根／Spec／Plan变更；AGENTS仅清单加名字。

最终构建实际执行`dotnet build Server/86JP.Server.slnx -c Release -m:1`，工具返回exit0，`logs/task59-build/build-final.log`为0警告／0错误。未执行`--no-incremental`。

## 最终证据

三项目full串行自然结束，实际完整argv／start／end／exit、TRX和detailed输出在`logs/task59-final/{Application,Protocol,Networking}`；每条均Release、`-m:1`、原5m blame、原类并行和独立DFO。本批build之后full使用`--no-build`，没有额外运行时选项或并行测试进程。

| 项目 | 完整结果 |
|---|---|
| Application | 5938＝5920 pass＋2原Dimension fail＋16原skip |
| Protocol | 1790 pass |
| Networking | 203 pass |

合计7931＝7913 pass＋2原fail＋16原skip。两项Dimension的名称／重数／完整Message与TASK58精确相同：Dungeon '7100' maze '0': cell (1,0) has no compatible map for entrance 'Left'。16 skip身份／Message／重数保持；登记PVF目标本次pass，未需复跑或改PVF。独立XML对照为`logs/task59-inventory-*.json`，App净增五显示：三Tournament业务＋两个具名架构正例；原真实Query拒绝显示由伪Query拒绝显示替换，原off-executor显示随状态主人改名，其他原资产／帧／重试断言保留。Protocol与Networking显示集合及outcome零变化。

原`violations.py`以未改expected核70＝67反例＋3控制：App62（61＋1），Protocol1（0＋1），Networking7（6＋1），outside／exe／orphan均0。实际输出`logs/task59-dfo-*.log`。账闭九行，另两行只补保留原因；五类ID／original／顺序与非目标行和HEAD逐对象相同。strict及八项坏数据反例通过。Task文件低于3KB，AGENTS仅增一条名字清单。无Git写、外部操作、八项目重复验收、A21实机或专项性能验收主张。
