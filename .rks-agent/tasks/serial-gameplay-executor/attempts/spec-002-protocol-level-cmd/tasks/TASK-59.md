# TASK-59 — 比武与锦标赛五消息

本 Task 承接 T-008 和用户流程 §8。TASK-58 已闭合祭坛三消息与死亡批命令；本批迁比武、锦标赛五消息，沿用已批准的整 CMD 与显式工作单元，不扩大到后续定时来源。

## 合同与计划

迁移 message:65994、65995、66790、66791、66792，消除 Joust 三条旧命令及 BeginTournamentRewards 的最后消费者。SelectTournamentReward、EliminateTournament 有真实定时器和退场消费者时保留挂账。保持比武请求幂等、扣币事务、正常拒绝码和刷新帧顺序；锦标赛保持开始选奖的写事务、选择完成帧顺序和两行奖励语义。

复用现有比武真实分发和锦标赛奖励强测试，仅为未覆盖的协议业务路径新增旧行为测试。罕见基础设施失败引用 VC-B40；边界裁定记 phase2-backlog，结构整理留第二阶段，不派独立评审。编译及受影响项目全量通过，账行随提交更新；新增旧行为测试与迁移分别提交。AGENTS 仅增名字，不推送或建立 PR。

## Result（Outcome: done）

迁移账行：闭合五message 65994／65995／66790／66791／66792及Joust三fine、BeginTournamentRewards共九行；另两行只补Timer与PartyWipe真实消费者保留原因，T-008未收尾。账为144闭／15待消息与128闭／76待fine，其余行、original及顺序不动。
行为变化：保持Joust持久幂等、回绕、业务回滚ACK6与刷新顺序，Tournament原写事务、期限和两行奖励／帧序；基础设施统一VC-B40。原Native健康链和末尾独立故障fixture保资产、entry、Ordinal及精确失败保护断言。
新增能力或注入类别：既有WriteResult增加Joust具名消费者，具名允许IReplayStateProjectionSink与ISelectedSessionSnapshotQuery，无新API；主控已记backlog。Release构建零警告／错误；App5920通过＋2原Dimension失败＋16原跳过，Protocol1790、Networking203通过；DFO67反例＋3正对照且无越界，strict及八个反例自检通过。
