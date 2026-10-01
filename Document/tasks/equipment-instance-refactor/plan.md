---
kind: rks-plan
spec:
  path: ./spec.md
mode: task-units
state: ready
---
# 装备实例重构计划
## Planning Choice
先落实身份与事务边界，后续业务迁移依赖其实际接口，分渐进Task并保存检查，避免跨上下文遗漏调用方。
## Approach and Shared Decisions
- IEquipmentDomainService交出事务内IEquipmentCollection/IEquipmentInstance；按CharacterId+UID定位，slot为当前地址；实例读取最新暂存状态，不保留可覆盖更新的独立core缓存。
- 实例按字段组设置：强化level、附魔card、锻造level、异次元type/value、魔法封印选项组、durability、销毁；细节在T-001根据真实store契约落实。
- 规则服务接收快照/保护事实及随机draw，拥有PVF输入和公式；领域服务校验会话/资产并结算，协议/receipt行为保持。
- 已有增幅信息为AmplifyType/AmplifyValue；2026-09-30实机暴露此前强化字段假设错误，强化level实际为Attribute低5位（不改存储布局），EnchantUpgradeCount是独立附魔升级次数。TASK-004修正实际读写与测试oracle。
- 复用本linked worktree与基线6e60c4c6，无任务单元提交；独立结果复核和新鲜证据后，最后用户验收通过才提交完整结果。
## Work and Dependencies
| Work | Outcome | Coverage | Dependency |
|---|---|---|---|
| T-001 | 身份/事务装备实例及行为回归 | RQ-001/002基础 | 当前容器和事务契约 |
| T-002 | 规则分离与全部调用方迁移 | RQ-002/003/004 | 已检查的实例接口 |
| T-003 | 集成回归、文档/协议边界、部署和可执行最终验收 | RQ-004/005/006 | 实际迁移结果 |
| T-004 | 实机发现的普通强化字段映射修复及重新验收 | RQ-001/004/005/006 | 当前用户失败反馈与TASK-003证据 |
| T-005 | 验收资金/材料、普通及超级强化保护闭环和集中人测 | RQ-007/008/009 | TASK-004正确字段、用户发放和保护功能授权 |
| T-006 | 连续成功现场诊断：核对运行概率/随机源并增加每次判定日志，恢复可观察的人测 | RQ-004/005/006/009 | 用户反馈长剑连续成功至+15；原结果未封存 |
| T-007 | 四券去除过期声明、三个客户端/服务内容更新及额外5000万金币 | RQ-010/011 | 用户明确内容与资金授权；各客户端原档案差异 |
| T-008 | 独立失败规则及完整70损毁产物、原等级追加奖励和事务批量结算 | RQ-012、RQ-002/004/005 | 用户接受设计并授权实现；70基础与追加调用证据、rev3已部署 |
## Verification
新实例测试保护身份及回滚；现有业务和codec测试保护公式与wire；真实PVF、生产DI副本以及独立结果审查覆盖遗漏；实际数据库备份后更新测试Host；用户R01/R02为最后门。
## Next Planning Boundary
T-001至007交付保留；T-008及真实内容嵌套字段返修已完成，第2轮完整review批准且新PID40292部署核验ready，根游标verification/manual_required。已发资金、三客户端PVF及历史资产未重新发放或恢复；最终失败材料与重登门仍由用户确认。
