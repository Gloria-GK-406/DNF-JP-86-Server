---
kind: rks-verification
decision: pass
---

# Completion Evidence

日期2026-10-14；判定范围是实现/部署完成并交付用户实机验收，非ClientVerified。

| Conditions | Evidence |
|---|---|
| SC-1 / SC-4 | 人物当前身份候选与未知位、配置组合、装备部件回归；application-final.trx 3832/3832，最终语义修正后review-remediation.trx 30/30。独立审查approved，调用矩阵见Document/CharacterAppearance-20261014.md |
| SC-2 / SC-3 | 两Roster使用同一EncodeRosterState，尾部offset24与世界/0165共用处理；新增四阶段行为RED→GREEN，protocol-final.trx 1519/1519，无跳过。实际dump调用和取位由独立审查者复核 |
| SC-4 / SC-5 | 持久化blob与legacy位部署前后相同，数据库结构相同；备份及live PRAGMA integrity_check均ok；无schema修改 |
| SC-5 | PID48240，runtime/Dfo.Server.Host.exe，ProductVersion 0.2.5-appearance-unification.20261014+snapshot.556392de83cbd60e；bootstrap ready、GM HTTP200、预期31监听齐全、stderr空；source-manifest.json与发布源码逐文件SHA相同 |

证据根：`artifacts/appearance-unification-20261014/`；其中verification保留所有TRX，包括此前失败记录；deployment-verification.json记录程序集、配置、PVF SHA与监听身份。运行从已审查源码快照发布，快照ID明确不冒充Git commit；接受后的本地提交由外部部署记录关联同一源码manifest。

原服务artifacts/settings-visibility-20261014/runtime保留；数据库备份rollback/database-before-appearance-unification.db保留，不恢复或覆盖运行数据。

实机待验收：关闭选项后完全重登的角色页/首次进入；返回角色页；开启/关闭即时变化；换装/透明装扮/称号后设置保持。未将自动化或静态分析当作实机效果证明。
