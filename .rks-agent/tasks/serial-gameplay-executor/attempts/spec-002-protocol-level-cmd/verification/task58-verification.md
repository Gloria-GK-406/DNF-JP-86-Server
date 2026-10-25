# TASK-58 验证

只闭合 message:65791／65794／65853 和旧 AltarDeathBatchCommand 四行。三入口各派唯一 whole CMD；旧 Coordinator、deathbatch 类型／handler／注册删除。AltarCommandPhases 保实质动作与死亡循环，Scheduler 及 DungeonPartyWipeService→Delivery.Fail 仍消费 AltarAction；RefillOnlineBossTowerTicket 仍由 SelectCharacter 和 BossTowerDailyRefill 消费，二者继续 pending，不提前结束 T-008。

原 AltarLifecycle／CellGate／Rule／ProjectionFailure／MonsterDeathAltarWake 强例不重复。只补六个真实 decoder→dispatcher→事务／encoder 业务显示：准备成功／拒绝后的当前配额；难度成功／拒绝的 ACK 内容、先 ACK 后 D7／D2、无重复0129；死亡批次 Distinct 同一真实事务、经验／死亡帧序、重报不重复收益及非 Fighting 拒绝。旧产品 `task58-old-r2/Application` 六例全绿，迁后正式 App 的相同六例全绿。死亡用一个 ordinary Write＋cohort DeclaredNonRequest owners，无 Main／账本；Ready／Difficulty 原独立写段与 null 正常提交、直接配额投影保持。完整 selected cohort 只捕报告者原 physical／metadata，其余不盲捕。原 Wake 时点和投影收件人不变；基础设施失败统一 VC-B40，无失败 ACK，没有新增能力或注入类别。

## 两项真实验证故障与修复边界

首次 `task58-final-r2/Application` 在原 5m blame 后自然中止，Sequence 明确两个 Host 构造测试未完成，createdump 因本机 task_for_pid 权限失败而无转储。隔离标记均停 Host.CreateApplicationBuilder，专用空 ContentRoot 仍停；仅诊断进程关闭 JSON reload 后 2/2 在完整生命周期自然通过。后续 `task58-final-r3/Protocol` 的同形构造挂起已按主控授权受控停止，不作验收。各诊断 flag、临时源码与停止时间均在原 run.json 标明，原证据未覆盖。

最终只在内存配置测试中关闭无关 JSON 热重载：32 个即时 Host 构造为 App3／Protocol2／Networking26／Infrastructure1，另 HttpApi 一个 WebApplicationOptions.Args；共五项目。26 个 Networking 调用逐点核 AddInMemoryCollection，22 源文件无 JSON reload 断言，明细 `/private/tmp/task58-networking-host-scope.json`。保真实 Host／DI／Start／Stop／TCP／通知启动顺序／排空及频道脚本快照断言。生产默认、原类并行和正式 5m 不变；Host.Tests 两个延迟 CreateDefaultBuilder 已清配置源，未改。

native Commit 字符串失败随进程改变。冷运行、TieredCompilation=0、同步 Commit 预热未复现，未据猜测删断言；`task58-native-asyncwarm-diagnostic` 在默认环境预热真实 await DbTransaction.CommitAsync 路径后，8 显示有 3 个原栈断言失败。它们仍是原 SQL19/787、body returned、children、ConfirmedRollback、FallibleCommit／KnownStorage／实际 SqliteDatabaseParticipant／同一 cause；栈从 ExecuteReader 跳到 DbTransaction.CommitAsync，随后真实 participant.CompleteAsync→DomainTransaction.CommitAsync，优化省略了被判断的 Commit 帧。同热路径用精确 failure fact 断言后 `task58-native-asyncwarm-fixed` 8/8，通过原资产／ACK／同 entry 重试全部断言且仍观察到省略帧。

最终25个现有测试文件的38处栈判断改为精确 participant-recorded fact，检查 Phase／Kind／实际 Source 类型／Same cause；原 action 反判断要求 CommitFailure null，原 SQL码、外键、return／children、原子资产、帧及重试不删。四个未持 Tx 的 CreatureAndEffect／Rental／Teleport／TownMovement 用集中测试观察器，仅在原 ordinary Write／WriteResult action 已进入后读原事务，完整透传声明／委托／token／结果／异常；不开事务，ReadOnly／SelfSettled 原样转发。临时预热及标记均已移除，只保留有用的原异常诊断。

## 最终证据

`task58-final-r3/build` 与后续扩展测试修复的 `task58-final-r4/build` 均为 fresh Release solution、零警告／错误。App 源在 r3 之后未改；组合下列五项目完整 TRX，不复制或重跑 App，中断的 Protocol 不混入验收。

| 项目 | 实际 logs 目录 | 完整结果 |
|---|---|---|
| Application | task58-final-r3/Application | 5933＝5914 pass＋2 原 Dimension fail＋1 已登记 PVF fail＋16 skip |
| Protocol | task58-final-r4/Protocol | 1790 pass |
| Networking | task58-final-r4/Networking | 203 pass |
| Infrastructure | task58-final-r4/Infrastructure | 974 pass＋2 skip |
| HttpApi | task58-final-r4/HttpApi | 57 pass |

两项 Dimension 名称／重数／完整 Message 保 TASK-57：Dungeon '7100' maze '0': cell (1,0) has no compatible map for entrance 'Left'。PVF 那项精确为 IllusionTowerPublicFlowTests.Public_handlers_complete_real_pvf_tower_return_to_town_and_reenter，InvalidDataException 含 '[minimum level]'／'required field is missing'，栈根 PvfEquipmentDefinitionSource.cs:160，装备64502。依 AGENTS 已登记三身份规则，`task58-pvf-repeat-1` 至 `-5` 五个独立进程均仅该例 1/1 green、exit0，完整记录 `logs/task58-pvf-repeats.json`；未改 PVF 或奖励算法，也不称此内容缺口已修复。过滤复跑未执行控制测试，不冒称有正对照。

组合为8959＝8938 pass＋2原 Dimension fail＋1已登记 PVF fail＋18 skip。逐 display／outcome／重数见 `logs/task58-composite-main-inventory.json`：只新增六个祭坛业务显示，原用例仅登记 PVF 从 pass 转 fail，无删除／改名。原 DFO parser 五个 `logs/task58-composite-main-dfo-*.log` 核72记录＝67原反例＋5正式正对照，outside／exe／orphan均0；未改 expected。各 full 原类并行、5m、detailed、独立 DFO／TRX，五项目串行自然结束，保实际 argv／start／end／child exit。

原六例纯准备在 `/private/tmp/task58-pure-tests.patch`；独立稳定性补丁 `/private/tmp/task58-test-stability.patch` 与 paths 清单共55路径，排除三个迁移适配文件（两个 legacy 注册名单和未使用 coordinator 局部构造）。稳定性补丁保25测试、一个 helper 和五项目测试 Host 变更；裁定在 phase2-backlog。迁移账仅本四行改变，original／顺序／其余行恒等，strict 与八个坏数据反例全拒。AGENTS 只加清单名字。无 Git 写、推送、PR、部署、A21 或专项性能验收主张。
