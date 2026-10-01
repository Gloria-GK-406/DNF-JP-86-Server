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
## Verification
新实例测试保护身份及回滚；现有业务和codec测试保护公式与wire；真实PVF、生产DI副本以及独立结果审查覆盖遗漏；实际数据库备份后更新测试Host；用户R01/R02为最后门。
## Next Planning Boundary
先执行T-001，实际接口和store暂存身份行为经验证后才详细规划T-002；后续样本和部署依据迁移完成的源版本建立。
