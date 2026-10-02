---
kind: rks-review
verdict: approved
disposition: aligned
---

# Spec003 累计工作 drift review

2026-10-02。审查者独立于实现生产者及挑战作者；采用 `reviewing-work/SKILL.md` 与 `references/drift.md`。本结论仅判断累计目标与路线对齐，不替代完整产品审查或 fresh completion verification。

## 审查目标

- 实际代码根：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/gm-web-admin`；分支 `feature/gm-web-admin`。
- HEAD / 接受基线：`416f26c9919383b58b0bd0e7179a99734fe53b48`。对象为相对该基线的冻结工作树累计改动，包含 untracked 新增产品、测试及子任务证据。
- 根与 child TaskContract 均为 revision 1；child 贡献根 SC-3 / SC-6。规格 SHA-256 为 `4D4349E0C10F6C90C4A1ACB13A082EEDB4896BDCC25BAA007B50891B74695169`，与已批准规格一致。
- 27 个已修改或新增的 `Server/`、`Web/` 路径清单指纹：`98016BD0667F676E724FD252C1F2E27DF42FA0F3937539199E9AA127E736A124`。算法：两类路径合并、排序去重，逐行 `SHA256  path`、LF 连接后取 UTF-8 SHA-256；不含工作流文件。
- 已读取适用 AGENTS、根合同与 multi-spec-map、child 合同/规格/执行配置/批准记录、implementation-report、browser-verification，并实际检查全部新增产品/测试源码及五张页面截图。planless focused 路线未配置 Plan / Task Unit，drift-check 未要求挑战集。

## 对齐证据

| 要求与路线 | 当前实际结果 |
|---|---|
| 根 SC-3：全服查询与详情 | `GmPlayerDirectoryEndpoints.cs:17-43` 五类 GET 均绑定 QueryPlayers，经命令总线取得白名单投影。`SqliteGmPlayerDirectoryPersistence.cs:15-55` 用同一 captured 在线集合与筛选表达式计数、按稳定 ID 分页，直接读取原始 name_bytes；无角色账号、三币与异常姓名均有查询路径。 |
| owner sampling / 当前事实 | `OnlineStateRegistry.cs:160-180` 由原所有者提供已认证 Game 账号快照与精确归属核验。`GmPlayerRuntimeQuery.cs:18-40` 组合当前会话、精确发布、在线归属，读位置前后复核，复用频道 policy；不建立第二份全服运行态缓存。current / saved / unavailable 与 NetworkEnabled / IsReady 保留各自语义。 |
| typed / 只读领域路线 | 新 IGmPlayerDirectoryPersistence 与 IGmPlayerDirectoryDomainService 提供窄 typed 接缝；五个命令 handler 只注入领域服务。持久化实现仅执行 SELECT，事务复用现有一致读取边界；没有新增玩家写入、GetOrCreate、选角恢复或通知路径，也未修改项目引用或建立第二 Host/DI。 |
| 中文交互与失权清除 | OverviewView / PlayerDirectoryView 提供搜索、筛选、分页、详情、ID关联、刷新、诊断及错误反馈。useGmRead 丢弃旧查询；`AdminApp.tsx:30-36,61,108-109` 的身份 generation 与撤权卸载防止旧 poll 恢复已失权资料。该局部访问回归修正在 RQ-009 范围内，不是重做认证。 |
| 根 SC-6：验证与材料 | 实际新增 HTTP/采样测试覆盖生命周期、分页前在线过滤、读取交错、字面姓名及只读前后对比；前端测试覆盖错误、旧响应与权限撤回。Document/gm-web-admin.md 提供查询语义及隔离页面入口。浏览器记录与五张截图展示真实 bundle 的概览、后页在线角色、关联详情、异常姓名和无角色账号余额。 |

没有发现应停止推进的遗漏结果、额外业务范围、破坏依赖或路线假设。新增 readiness 接缝与旧测试 fake 的调整用于准确区分游戏启用/就绪；opt-in browser fixture 限定 fresh artifacts 子目录与独立端口，不把隔离种子操作纳入生产功能。

根 SC-4 官方邮件、SC-5 点券发放、SC-1/SC-6 最终配置/CI/完整验收仍由后继阶段承担。multi-spec-map 保留这些缺口；SC-3 未提前写成 satisfied，符合当前尚未接受的 child 状态。未来包尚未实现不构成本轮 drift。

## 证据边界

implementation-report 中已通过检查是生产者已有证据指针，本审查未重跑共享 .NET 或任何 Host。browser-verification 明确区分实际页面成功观察与被应用中断、不可恢复的 runner：未声称其退出码为 0，主控最终 fresh VERIFY_ONLY 仍待执行。PVF 条件跳过、真实客户端和根任务完成未被升级为已验证。

本轮仅写本审查文件；未改产品、测试、cursor/root、未提交，未碰现服、真实数据库或客户端。批准允许主控消费 drift-check gate；后续独立 change-adversarial / final-review / fresh verification 仍按执行配置进行。
