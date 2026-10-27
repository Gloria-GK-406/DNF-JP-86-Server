---
kind: rks-verification
decision: pass
---

# TASK-47 核验摘要

本判定只覆盖 CharacterDeath／UseRevivalCoin 与四 fine 的归并和无新增回归，待唯一 fresh Astra 结果快审及主控接受。基线与当前 HEAD 为 `fff95b5d`，当前修改尚未提交；不关闭 T-007、T-008、Timer 或 Spec B。

## 交付与旧新证据

- 两消息各一个完整协议 CMD 和薄 bus handler。普通死亡保原 Direct 立即内存修改，零事务／零工作单元；两塔 Die 各一次 ordinary Write，原 run members 作为 DeclaredNonRequest owners，Boss 全 selected、仅报告者 physical／原 metadata，Illusion 空 selected。owners 不等同围栏集合，未加 current 门、Main、raw、Self 或共享能力。复活一次 ordinary Write、空 selected、无 footprint，币／life／wipe／限额／Mirror 事实的原事务保持，正常拒绝不改 RequireRollback。
- 普通入口仍先 source session，再 Boss／Illusion，两个塔 helper 均 stale-before-lookup；普通 expected run／registry exact／InWorld、重复死亡不发帧，peer 仍按 frozen participants／当前世界／same-run 筛选。两塔复用原 PartyMutation 的同投影算法，保奖励／邮件／经验、提交后 clone 发布顺序及逐成员 catch 继续。四 fine、旧 MarkDeadResult 和两个塔 Delivery 的 Die 适配段删除；PartyWipe／ClearReturn／Timer／Leave 保持。
- 复活原 Boss 拒绝、缺 run／冻结 payer／InWorld 静默、target wire／当前世界／same-run／payer online 门保持；成功 payer 为 life(state1)→ACK→coin，peer 仅 life，拒绝 `[00,00]`。AckFlag 仍由既有 immutable 启动 options 验证，decoder 只把它作为非 wire 输入交入 CMD；C2S 仍一 u16，默认1、0／1两种实际 ACK 和2／255启动拒绝受正式测试守护，未扩 CMD 配置注入。
- 最终纯准备在精确旧产品和旧测试 `fff95b5d` 上为 **195/195**（24 新 TASK-47 oracle＋171 原死亡／复活／两塔强例），见 `logs/task47-old-final-r7/Application`；临时旧源仅内存保存并 finally 完整恢复，`restore.json` 记录 all_restored=true。补丁 `/private/tmp/task47-pure-preparation.patch` 仅新增 oracle 文件与一个供其共用的测试 AddMember 访问级别，当前 reverse-check 通过。新侧最终同组195例全部包含于完整 App r2；新聚焦 `task47-new-r5/Application` **32/32**，Protocol old-r5 **69/69**、new-r6 **74/74**。
- 24 新例经实际注册 decoder→dispatcher→原真实 DomainTx／SQLite 或普通死亡零 Tx→actual encoder→production outbox→受控 physical。普通死亡 scope 前后及 encoder 均保原异常、无虚构 Committed／typed；scope 迁入 whole 后已有 ACK→life。复活及两塔 scope 的默认 typed 与原 cause、真实 Completion、实际发送／精确关闭、根链、无误 Fence／Block均分支断言。两塔 query-only／contexts 双替换直接核原／新 physical，Boss scope 与 Illusion 的保护集合差异保持。
- 复活在真实 SQLite 扣币后受控 Prepare 失败（新增的是 non-fallible participant，不造第二 fallible）证明币／life／wipe ConfirmedRollback、零成功帧和来源关闭；新准入 physical 经真实 whole 重试只付一次，重复拒绝。队友复活只扣 payer、余额到0仍能现查该槽。两塔实际团灭各消费奖励 owners，原装备满包路径分别形成10／4件候选、库存／邮件并只结算一次。原强用例继续守奖励持久失败回滚、角色／经验／钱包／邮件与重试。
- Tournament 原限额与计数回滚断言保持。新增 Altar 限额计数及 Mirror 目标账号／QuestCharacterRevived 事实用实际 wire／真实 coin 事务验证领域调用边界；其 collaborator 是受控测试实现，不冒充实际 Altar 引擎／Mirror 存储全部验收。原 fine／projection fixtures 改同 CMD 或真共享算法的受限 driver，业务强断言保留，不冒生产 scope。
- VC-B21 具名普通零 Tx scope 时点及复活／两塔 whole 默认生命周期；VC-B22 仅两塔死亡 cleanup 提前捕获 physical，沿原 IsInWorld 与既有 contexts 准入，不扩共享 closer。两变化与 whole 归并不可分；可独立纯准备补丁分开交付。Spec B7／Plan P9、根／子契约、RQ／AC 和共享能力不改；AGENTS 只加清单一句出处。

## 最终正式检查

`logs/task47-final-r1/build` 为 `dotnet build Server/86JP.Server.slnx -c Release -m:1`，**0警告0错误**。其后只有一处 App test fixture 入口适配及 test-only driver；完整 App r2 重新编译并验证该变更，产品未再改。

各正式项目命令为 `dotnet test Server/tests/Dfo.Server.<项目>.Tests/Dfo.Server.<项目>.Tests.csproj -c Release -m:1 --blame-hang-timeout 5m --logger "console;verbosity=detailed" --logger "trx;LogFileName=results.trx" --results-directory <独立目录>`。各 `run.json` 在运行前后记录 cwd／HEAD／branch／真实 argv／独立 DFO／start／end／exit。类并行保持，项目严格串行；包括诊断 r1、fixture focus 与 r2 的所有轮次均无项目重叠。

| 项目／有效轮次 | 结果 | 对 Task46 的逐display／status／重数 |
|---|---:|---:|
| Application r2 | 5597＝5579通过＋2原Dimension＋16原skip，exit1 | N24／D0（Task46 App r4） |
| Protocol r1 | 1786通过，exit0 | N5／D0（Task46 Protocol r1） |
| Networking r1 | 203通过，exit0 | N0／D0（Task46 Net r3） |
| Host r1 | 203通过，exit0 | N0／D0（Task46 Host r3） |

四 fresh **7789＝7771通过＋2原失败＋16原skip**。失败仅 `DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource` 与 `DimensionDungeonTests.All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`，仍 7100／maze0／(1,0)／Left 缺 compatible map；skip 身份及重数保持。差集为各项目 `nd.json`，总账与无重叠时间在 `logs/task47-final-r2/comparison.json`。未将其它四项目的旧库存当成本 Task fresh 八项目结果。

原 DFO parser 精确 expected 全部通过：**71＝67反例＋4 control**，App62、Protocol1、Net7、Host1；每实际程序集一个 control，outside／exe／orphan 均0。原分类不放宽，见各有效轮次 `dfo-summary.txt`。

`evidence/task47/runtime` 只登记模块，不 build 游戏 provider 或启动服务：两个唯一 ProtocolCommand route，各一个 Direct／Protocol 单 binding，四 fine binding0；`task47-route-check.json` 是断言结果。六目标之外、全部 original 与行序保持；Result 后 strict／八坏例 selftest 及含新增文件 diff-check 通过。迁移账为消息88/159、fine86/204、来源1/73、保护2/5、过渡0/8。

## 诊断与限制

App r1 的额外失败为旧 `IllusionTowerPublicFlowTests.A_sweep_tick_after_the_old_runs_return_was_due_leaves_its_replacement_alone` 在既有 TestGameplay job 内直调新薄 handler，正确触发 GameplaySelfWaitException。四项目全部结束后，仅将该入口适配为 owning whole body＋真实 workunits＋显式 request 的测试 driver；原 replacement／sweep／资产／帧数断言逐字保持，focused1/1，App r2 无新增失败。r1 App不纳最终通过口径，其余三 r1 输入未受 test-only 适配影响。

早期新增 fixture 的入场票证／party／temporary-container准备、零币存储表示以及普通重复死仍调用 fine scope 的预期校准均已在最终旧新正式轮验证，不并入基线失败。受控 Prepare／encoder／physical 不等同自然故障或 A21 实机；未运行真实游戏、部署、修改 index、提交、推送、PR、tag 或清理工作区／服务。最终记录结束后停止全部写入，交唯一 fresh Astra。
