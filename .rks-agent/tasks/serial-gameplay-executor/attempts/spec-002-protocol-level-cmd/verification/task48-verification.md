---
kind: rks-verification
decision: pass
---

# TASK-48 核验摘要

本判定仅覆盖五消息、两旧 fine 与 source:card-delay 八行及无新增回归，待同一 Astra 结果快审和主控接受。产品基线 `d0dd43d2`；当前 `58004de8` 只增加已批准 B8／P10 metadata。不关闭 T-007／T-010 或 Spec B。

## 交付与旧新证据

- 五条唯一 Protocol／Direct whole 与薄 handler、两条 Internal／Direct Timer。Grant 只有一次 ordinary Write，精确 actor／原 physical／显式 metadata 与 NonRequest；原 Despair→Mirror→luckyStar→经验→attendance→difficulty 不变。cards／claim 保留在事务外，CommitSettlement、attendance 投影、当前进度／技能／任务刷新、原响应及成功后的调度仍按原序。结算规则由真实 run owner 调原规则，不引入伪领域转发层。
- Card 同家族共享选择、布局与单份投影算法；原 plain SelectCard 变 ordinary `[]`／None，保费用／gold／normal＋blackdiamond／InventoryThenMail／预约同事务。Free index0、published／run／InWorld／frozen 门与共享 reveal／private 顺序保持；旧两个 fine、旧业务 coordinator 内容及无生产消费者的旧 delivery／DI 已删除，直接 fixture 只驱动这些同一算法。
- 原 singleton producer 只拥有固定2s／4s、pending／token 替换取消、外部 typed 提交及 drain；窄 control 与 producer 为同一对象，完整 producer／相似接口反例仍拒绝。真实到期经 production bus 使用 Timer 新根、无 source、None 入队、输入 token 控制 body；queued cancel／FIFO／drain 和 token 身份不误删新 auto 保持。未迁 GiveUp／Eplp／Wipe 仍同步取消同 owner，不推进其来源。
- 21 个新正式 oracle 在旧、新均通过。实际 decoder→dispatcher／typed Internal→原 bus／SQLite→actual encoder→production outbox→受控 physical，覆盖 Grant scope 提交后资产／claim／重试、encoder 无调度与 scope 前已调度、精确声明、实际 Free／Paid／拒绝、Cancel-before-projection、原生第二奖励 SQL 回滚、付款及 item SQL 后 Prepare 回滚与新 physical 一次重试、真实 auto2s＋4s 与重复无多付、布局／Boss、来源／取消／drain。scope 注错来自真实 DI scope Dispose，非 body 手抛。事务完成／请求关闭／原 cause 与帧序直接核；受控物理替换不冒充实际重登录或 A21。
- 旧 Grant scope 在 SQL durable 后、caller durableCommitted 之前抛原 cause，claim 被释放，受控 dispatcher 同 run 重试经验30→60；新 whole scope 在 claim 已提交／响应已登记／已调度后失败，经验仍30且 claim 不可再领。默认 whole、即时 encoder 与 Timer 旧吞错边界写 VC-B23；F-86 单执行器三作业证明已排队的释放不能在当前三次尝试间运行，删除无业务来源重试并写 VC-B24；两项与 whole 归并不可分，未授予 raw／Self。
- 最终旧 `logs/task48-old-final-r1` 为 App **334/334＝313原强例＋21新 oracle**、Protocol **270/270**。当前改动完整 recovery patch 和固定46项 manifest 保护后，由主控执行同一获准脚本；`restore.json` 为 all_restored=true、errors0、mismatches0。旧源只临时放在授权工作区，无源码快照。纯准备 `/private/tmp/task48-pure-preparation.patch` 仅21例和共享 probe 两行，当前 reverse-check 通过。恢复后的 `task48-restored-current-r1` 同组 **334/334、270/270** 重新编译为当前 DLL，21例也全部包含在最终 App full。

## 正式检查

`dotnet build Server/86JP.Server.slnx -c Release -m:1` 的最终 `logs/task48-final-r3/build` 为 **0警告／0错误**。正式各项目均为 `dotnet test ... -c Release -m:1 --blame-hang-timeout 5m --logger "console;verbosity=detailed" --logger "trx;LogFileName=results.trx" --results-directory <独立目录>`，各自 DFO。`run.json` 记录实际 argv／cwd／HEAD／branch／UTC start-end／exit；全部 runner 严格串行，原类并行与 skip 不变。

| 项目／有效轮次 | 结果 | 对47有效库存／未影响四项目45库存的 display＋status＋重数 |
|---|---:|---:|
| Application r4 | 5621＝5603 pass＋2原 Dimension＋16 skip | N24／D0（21 oracle＋3类型正反例） |
| Protocol r3 | 1786 pass | N1／D1（仅 F-86 fake 冲突测试有意替换） |
| Networking r2 | 203 pass | N0／D0 |
| Host r2 | 203 pass | N0／D0 |
| Persistence r2 | 484 pass | N0／D0 |
| Infrastructure r2 | 976＝974 pass＋2 skip | N0／D0 |
| HttpApi r2 | 57 pass | N0／D0 |
| Pvf r2 | 80 pass | N0／D0 |

八有效全量 **9410＝9390 pass＋2原失败＋18 skip**。两失败仅 `DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource` 和 `All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`，仍7100／maze0／(1,0)／Left缺图。逐用例差集及所有轮次无重叠时间在 `logs/task48-final-r4/comparison.json`。原 DFO parser 精确通过：**74＝67反例＋7 control**，App62／Protocol1／Net7／Host1／Persistence1／Infrastructure1／HttpApi1／Pvf0，outside／exe／orphan0。

实际 DI capture 仅登记模块，不 build 游戏 provider：`evidence/task48/runtime/task48-route-check.json` 为五唯一 Protocol＋两 Internal／两 fine0；源码与 runtime 双向对应。八行外、全部 original 与行序保持。Result 后 strict、八坏例与含新增文件 diff-check 通过；迁移账为消息93/159、fine88/204、来源2/73、保护2/5、过渡0/8。

## 诊断与限制

r1 build 发现 Host 既有 fine fixture 的同算法 driver 无 internal 访问，最小增加 Host 测试友元，保原断言。r2 App 新增读写分类失败由新增查询／固定阶段及旧方法残行引起：更新分类，两个 public 同步 wrapper 各在原首位置直接断言，未改时序／状态／接口；聚焦30/30，r3重新 build＋App／Protocol full。r4只修 auto oracle 等同 producer pending／inflight 清空再 Drain 的 test-only 窗口，App 再 full；Protocol r3 与其余六 r2按主控裁定有效，不把已修架构失败并入原 Dimension。

第一次临时旧源执行被自动审批以“未提交成果恢复风险及缺少脚本／精确授权证据”拒绝，未执行。主控核原授权后，增加完整 binary 恢复 patch、固定 manifest／HEAD／branch、新鲜 patch 等价与 reverse-check、逐项 finally 恢复检查，同一精确命令安全重审获准后执行并完整恢复。恢复 patch 仍留本机，不进 git。 最后含新文件 diff-check 只发现并删除 BossDieCheck 新文件末尾一个多余空行；该纯空白修正不改变代码，历史恢复 patch 保留原样，不冒称其与这一个空行修正后的文件仍完全匹配。

受控 Prepare、SQL trigger、encoder、scope 与 physical 不是自然故障或 A21 实机；未运行真实游戏、部署、提交、推送、PR、tag 或清理分支／工作区／服务。当前源码已恢复、全部 runner 已结束，交唯一 Astra 结果快审。
