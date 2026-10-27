---
kind: rks-verification
decision: pass
---

# TASK-46 核验摘要

本判定只覆盖 GetItem 普通／两塔拾取及三 fine 的归并、无新增回归，待唯一独立 Astra 结果快审及主控接受。基线与当前 HEAD 均为 `c20f4878`，产品／正式测试／记录尚未提交。未关闭 T-007、T-008、Timer 或 Spec B。

## 交付和旧新证据

- GetItem 是唯一 `GameGetItemCommand` 与薄 handler。普通 claim／gold／epic／grant／UID／durability／avatar／creature 算法和拾取投影是该 CMD 自身 partial 方法，没有新生产算法助手或旧 Delivery／Coordinator 转发。普通算法 body 逐段比对未改；三 fine 类型与绑定、两塔 adapter 的 Pickup 段和仅此入口消费的两拾取编排及 DI 已删除。Die／Leave／Timer、真共享 Party 与 clear 算法保持。
- 普通、Boss、Illusion 每实际分支一次原 ordinary Write。普通及 Illusion 空 selected，Boss 原全 run selected、仅报告者 physical，原 metadata；三者无 footprint，不套 Kill owners。普通 gold／epic 在 bag 前，NoRoom／Unknown 释放 claim 后普通返回；只 InventoryFull 回 `[00,04]`。Boss 保 lookup→stale，Illusion 保 stale→lookup。普通 PickedUp＋QuestProgress＋匹配 committed change 才以 CancellationToken.None 先 Evaluate clear，再投影；拾取投影 false 不抑制独立 clear 投影。原双 current/run 门与 ACK→pickup→gold／virtual／quest、peer 仅 pickup 保持。
- 最终纯准备在精确旧产品与旧正式测试 `c20f4878` 跑 **24＝22通过＋2原PVF跳过**：`logs/task46-old-final-r10/Application`。其中 **18 新生产链 oracle＋1 原 CAS 事务＋3 原头像／装备资产＋2 原 MarriageCapsule PVF skip**。旧代码确实执行，临时源只存内存并 finally 恢复；无源码快照。补丁 `/private/tmp/task46-pure-preparation.patch` 仅新增 `GroundPickupFailureBoundaryTests.cs`，对应最终旧绿内容。
- 相同最终 oracle 新侧连同上述六例及已适配 public-flow 一例，共 **25＝23通过＋2原skip**：`logs/task46-new-final-r10/Application`。原 Protocol GroundPickup／codec／GetItem／QuestClearVertical 为 `task46-new-focus-r7/Protocol` **44/44**；新侧最终产品未再改，后续只有新增 App oracle 和一处 App fixture 驱动适配。
- 18例生产证据经真实注册 decoder→dispatcher→DomainTx／SQLite→actual encoder→production outbox→受控 physical。scope Completion、cause、typed reason、无 false Fence／Block、根链／来源关闭、真实 secondary transport 与 encoder 分开、query-only／contexts 原新 physical 隔离均直接核。最后一例在真实 GrantAsync 已写 SQLite 后加入 Prepare 失败参与者，证明 ConfirmedRollback、SQL0、floor claim 释放、零成功帧和精确来源关闭；再由新准入 physical 经实际 decoder／dispatcher 重试 SQL1、ACK→pickup，重复再拾取不支付、不增帧。原 CAS 专用命令测试仍保留，未冒充该实际生产回滚链。
- Boss 真满包填满两个装备槽后经 InventoryThenMail，成功 ACK、邮件一次，重复不再领取；未以 fake Boss NoRoom 推断业务。Illusion 实际七种模板填六临时槽，第七件 NoRoom 不 publish clone／claim；拒绝 ACK 编码在 catch 外，失败直接逃出且无新增围栏。两塔成功批次 encoder 仍逐成员 catch、继续、正常完成和原序关闭。
- VC-B19 明列 scope 从 fine 转 whole 的投影时点及默认 typed 差异，普通／Illusion 无新增保护，Boss 保原全 selected 和仅报告者 physical。VC-B20 仅 Pickup cleanup 提前捕获 physical，继续沿原 IsInWorld 与既有 contexts 准入；query-only 仍关原，contexts 已替换则原／新均 precise-drop，replacement 零帧零关闭。共享 closer／F-83／RQ-035 未改，B7／P9、根／子契约及 RQ／AC 均未扩。
- 原孤立 fine／projection fixture 改为受限 subclass 执行 CMD 自身同算法，业务、数量、资产、事务与帧序断言保留；它们不冒生产 inbound scope。QuestClearVertical 测试专用 Transactional command 仍经真实 bus 与 SQLite；CommonExperience 本次只从旧 selected 注册库存移除退休 Boss Pickup 名称，Task45 的不同 TResult 排队强断言未动。

## 最终正式检查

产品 `task46-final-r1/build` Release solution build **0警告0错误**。最终采用 **Application r4＋Protocol r1＋Networking r3＋Host r3**，均为原类并行、项目串行 `-m:1`、Release、5m hang、`console;verbosity=detailed`、独立 DFO／TRX；无 guard／Skip／期限／并行放宽。每次 runner 先写 cwd／HEAD／branch／完整 argv／DFO／start，完成写 end 和真实 exit。命令形状 `dotnet test Server/tests/Dfo.Server.<project>.Tests/Dfo.Server.<project>.Tests.csproj -c Release -m:1 --blame-hang-timeout 5m --logger "console;verbosity=detailed" --logger "trx;LogFileName=results.trx" --results-directory <独立目录>`；真实 argv 见各 `run.json`。

| 项目／有效轮次 | 结果 | 相对Task45逐display／status／重数 |
|---|---:|---:|
| Application r4 | 5573＝5555通过＋2原Dimension＋16原skip，exit1 | N18／D0 |
| Protocol r1 | 1781通过，exit0 | N0／D0 |
| Networking r3 | 203通过，exit0 | N0／D0 |
| Host r3 | 203通过，exit0 | N0／D0 |

四项 **7760＝7742通过＋2原失败＋16原skip**。仅失败为 `DimensionDungeonTests.All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot` 与 `DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`，仍 7100／maze0／(1,0)／Left 缺 compatible map；skip 身份与重数保持。App 对 Task45 r3，另三项对 Task45 r2；差集在各 `nd.json`，汇总在 `logs/task46-final-r4/comparison.json`。没有把未跑的八项目全量或实机验收写成通过。

原 DFO parser 四次均 exit0：**71＝67原反例＋4 control**（App62、Protocol1、Net7、Host1），每项目实际程序集精确一个 control；原 expected 分类／数量不变，outside／exe／orphan 均0。详见各 `dfo-summary.txt`。

实际模块 capture 在 `evidence/task46/runtime`，只登记模块，不 build 游戏 provider／启动服务：GetItem 唯一 ProtocolCommand 路由、whole 唯一 Direct 与单 binding，三 fine binding0。只有四目标台账对象变化，全部 original、非目标和行序保持；preclose、strict、八坏例 selftest 与含新增文件的 diff-check 通过。迁移账为消息86/159（pending73＋telemetry14）、fine82/204（pending122）、来源1/73、保护2/5、过渡0/8。

## 诊断与限制

早期驱动导入／空 outbox／命名空间准备故障已修，原业务断言没有缩减。r1 App 新失败是旧 public-flow fixture 在 TestGameplay 已有 job 内调新薄 bus，触发正确 GameplaySelfWaitException；只改该调用为同 whole driver，原 ACK→pickup→inventory、临时物品无永久泄漏与 claimed 断言完整，`task46-fixture-focus` 1/1，后续完整 App 无该失败。新增回滚 oracle 初次多预期一个不存在的 quest 帧，按真实无任务结果校准为 ACK→pickup，最终旧新重跑通过。

调度误操作曾使 App r2 与 r1 Net／Host 重叠，均只作诊断；已向精确自有诊断子树发送 SIGTERM，runner 最后实际 exit0，未伪造取消码。这些结果不纳正式验收。此后 r3 App→Net→Host 真实串行结束；新增 test-only 回滚 oracle在该系列全部结束后才补，最终旧恢复／新focus／App r4亦串行，Net／Host r3与Protocol r1输入未受影响。产品 build 后仅两Command文件去尾空行，无语义变化。

受控 physical 不等于真实 A21 客户端；两个 MarriageCapsule PVF 用例因原环境条件跳过。未动 index、提交、push、PR、tag、部署、真实游戏或清理现有工作区／服务。最终记录结束后停止全部写入，交唯一独立 Astra。
