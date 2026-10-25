# TASK-60 — 婚礼、房屋与胶囊十二消息

本 Task 承接 T-008 和用户流程 §8。TASK-59 已迁比武、锦标赛五消息；本批迁余下婚礼、房屋与胶囊十二消息，完成 T-008 协议范围，保留真实后续来源的命令。

## 合同与计划

迁移 message:66544、66545、66546、66547、66549、66550、66551、66552、66553、66554、66557、66564。只有最后消费者消除才关闭对应旧命令；InitializeHousing 留选角，AdvanceMarriageWedding 与 CompleteWeddingCleanup 留定时批。保持婚礼失败补偿与各独立提交、双方精确参与者、入教堂与移动顺序、房屋和胶囊的正常拒绝及物品／确认帧顺序。

复用已有婚姻领域、真实网络、持久化和胶囊强测试，只为未覆盖的协议业务路径补旧行为用例。基础设施罕见失败引用 VC-B40；新能力由主控裁定记 phase2-backlog，结构整理留第二阶段，不派独立评审。完成 fresh Release 构建和八项目正式全量，按原 DFO 和已登记失败规则核验。更新账行与 AGENTS 名字，测试与迁移分别提交，不推送或建 PR。

## Result（Outcome: done）

迁移账行：十二 message 与十个最后消费者 fine 闭合；message 为 156 closed／3 pending／14 retained，fine 为 138 closed／66 pending，其余三类不动。InitializeHousing 留选角，AdvanceMarriageWedding 与 CompleteWeddingCleanup 留定时来源。
行为变化：正常拒绝、原独立事务、slots／帧序／精确受众保持；只有首次 Response 的真实 WeddingUnavailable 异常回滚后独立拒绝，正常 Unavailable 不二次调用。基础设施失败沿 VC-B40 默认保护，不补业务失败 ACK。
新增能力或注入类别：仅三个主控批准的 CommittedReplayCurrentStateBatch 消费者，无新注入；Release 构建零警告零错误。require_escalated 八 full 的通过／失败／跳过依次为 App 5946／2／16、Protocol 1790／0／0、Networking 203／0／0、Persistence 484／0／0、Infrastructure 974／0／2、Host 203／0／0、HttpApi 57／0／0、Pvf 80／0／0；两失败均原 exact Dimension，详见 task60-verification。新增净 26 用例。
