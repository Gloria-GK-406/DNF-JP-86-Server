---
kind: rks-review
verdict: approved
---

# 独立混合结果审查

## 对象与准则

- 工作区：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/monster-declaration-parser`。
- 基线：`a0ef0f150dc8bf9d567177a007cd1766033ad022`；审查冻结的未提交改动及全部新增源码/测试、协议说明、任务文档和部署脚本。
- 依据：TaskContract revision 1、ready Spec/Plan，以及 reviewing-work 的 Code / Deliverable 准则；审查者独立于生产者，未修改生产代码、未运行构建或部署。
- 取证时内容指纹：44 个当前变更文件，SHA-256 `797bbb4c59eb4629d83ba1ad66705921b6178e2d3cbdec4e449e2be36ffe492a`。计算范围为变更的 Server、protocol、AGENTS.md 和本任务文档，排除 reviews、verification 及 sdd-execution.yaml；每项为按路径排序的 `path + NUL + file-sha256 + LF`。此指纹记录审查时点，后续验证/部署回执不是已审部署证据。

## 判定依据

| 范围 | 独立检查与结论 |
|---|---|
| DirectAsync | EventService.cs:39 直接按注册顺序 await runtime 订阅者，继承 ambient transaction，不创建事务/入队，异常及取消传播。ProjectAsync 仍拒绝观察未提交的可写事务。新增测试覆盖等待、顺序、失败停止、取消及事务内外。 |
| 统一写入与回滚 | UsageQuotaDomainService.cs:52 要求当前可写事务，整批先检查地址、范围与预计旧值，再 CAS；失败抛出以交还业务事务回滚。四种扣次事实只注册 runtime；持久化的 UsageQuotaChanged 只做界面刷新，没有第二条扣次路径。 |
| 迁移完整性 | 搜索生产源码的 IConsumableUsageTable、IPremiumServiceUsageTable 与 WriteQuotaAsync：实际次数写表收口于 LegacyUsageQuotaStore，原消费 Effect、ItemUsage、全部 Premium 使用/续期、QuestHelperPuppet 标记及 Mirror 入场/领奖均迁移。每日补发领取标记继续由原补发领域管理，与本任务范围一致。 |
| 原始数据与规则 | 兼容适配器保留原表、键、时间戳与镜像日期行；Mirror 字段更新保留同行其他字段。双倍罐旧 round→total fallback 及续期总量保护保留。消耗品/权益仍由原 daily-reset 解释，镜像仍按 UTC+8 午夜。只读快照不落库重置；两个原纯查询命令改为 ReadOnly。 |
| 提交后投影 | UsageQuotaChanged 使用 LiveOnly，按角色/类别在事务批次中合并。既有 dispatcher 提交消费决定、离开数据库门与事务后调用订阅者；新订阅者经 EventService.ProjectAsync 读取最新快照。真实 SQLite 新回归覆盖提交前不发、回滚不发、合并后读最新数、投影失败不回滚业务。 |
| 会话与恢复 | UsageQuotaViewChangedHandler 在读取前后检查精确会话，登录/选图/加载补发使用 TheseSessions。离线跳过；新一代会话重新读取权威值。镜像日刷新只对在线会话工作，失败不标已投递，保留重试机会。 |
| 协议与加载 | 0279 明确发 8B：首 u32 保守为 0，第二 u32 为已入场数。未添加 CMD0025 ACK，加载继续只用 NOTI001E，之后才发权威镜像快照；普通地下城不走镜像加载补发。021E 清空/分块与 00AC 有效冷却复用原实现并整体验证，036F 保持原 CMD 家族 77B 编码。新增测试检查消息家族、字节、负数/溢出与加载顺序。 |
| 部署与只读审计 | deploy.ps1 在停服前核对基线、源码清单/哈希、旧 PID/路径/监听、程序集版本、配置、PVF 与 GM 静态页面；停服后备份 DB/WAL/SHM 并 quick_check，失败重新启动旧服，不覆盖权威 DB。audit-quota.py 以 mode=ro/query_only 读取全部次数行；部署回执记录前后对照、运行身份与回滚输入。脚本未执行，本审查不声称部署已成功。 |
| 文档一致性 | Spec 明确重构前后的结构与职责；协议说明区分 ClientStatic、CurrentImplementation 和待实机验收。初次检查发现 AGENTS.md:441 仍描述已删除的 PublishAsync；主控已改为 DirectAsync 的实际事务/失败语义，复核差异后此项关闭。 |

## 结论与验收边界

没有未解决的阻塞缺陷，批准当前混合结果进入最终验证和部署。未要求改变既有额度、重置时间、客户端补丁或扩展全部任务系统。

本审查不替代完整 Release 回归、现服数据对照与 SC-5 部署核验；这些由后续 verification 记录。0279 第一字段仍是 Unknown、当前写 0；本次真实客户端的次数显示、加载顺序及消费操作仍须用户按 client-test-set.md 验收，不标记为 ClientVerified。既有 Infrastructure PVF 基线失败不得计作本任务通过或新回归。
