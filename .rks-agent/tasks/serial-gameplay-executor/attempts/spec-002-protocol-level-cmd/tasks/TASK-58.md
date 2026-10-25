# TASK-58 — 两塔收尾与祭坛协议迁移

本 Task 承接 T-008 和用户流程 §8。TASK-57 已消除两塔五条旧命令及两条内部来源；本批迁祭坛三消息，核清剩余两塔依赖，不重复已闭合行。目标是保持业务语义并推进迁移账，结构整理留第二阶段。

## 合同与计划

迁移 message:65791、message:65794、message:65853，消除 AltarDeathBatchCommand 的最后消费者。RefillOnlineBossTowerTicket 与 AltarActionCommand 仍有真实后续来源时继续挂账，不提前关闭。三个协议整 CMD 复用实质阶段，保持死亡批次单事务、怪物 actor 序号去重和原精确参与者，准备后的配额投递、难度确认帧顺序及唤醒位置。

先读已有祭坛协议、生命周期、投影、房间门与死亡唤醒测试，只为未覆盖业务补旧行为用例。罕见基础设施失败统一引用 VC-B40，不重建逐协议故障矩阵。新能力或边界由主控裁定后记 phase2-backlog；不另修 Spec／Plan，不派独立评审。

编译通过，完整运行受影响测试项目，精确核对已知失败与 DFO。账行随本 Task 更新；AGENTS 只增清单名字。旧行为新增测试单独提交，再提交迁移及 §8、统一 VC、backlog；无新增旧行为测试只作迁移提交。原始证据留本机，不推送或建立 PR。

## Result（Outcome: done）

迁移账行：闭合 message:65791、65794、65853 与 AltarDeathBatchCommand 四行；AltarAction 和 RefillOnlineBossTowerTicket 因调度／Wipe、选角／定时真实消费者继续挂账，T-008 未收尾。
行为变化：保持批次单事务、actor 去重、精确参与者、准备后配额、难度 ACK 帧序及原 Wake；罕见基础设施失败统一 VC-B40。另修复测试对无关 JSON 热重载和可被优化省略的栈帧的依赖，未改变生产默认或资产／帧／重试断言。
新增能力或注入类别：没有新增。Release 编译零警告／错误；App 为 5914 通过＋2 原 Dimension 失败＋1 已登记 PVF 偶发＋16 跳过，目标独立复验 5/5；Protocol 1790、Networking 203、Infrastructure 974＋2 跳过、HttpApi 57。正式 DFO 为 67 反例＋5 正对照且无越界，账行 strict 与八反例自检通过。
