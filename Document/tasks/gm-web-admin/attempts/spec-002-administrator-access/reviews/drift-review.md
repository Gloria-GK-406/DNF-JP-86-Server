---
kind: rks-review
verdict: approved
disposition: aligned
---

# Spec-002 累计工作对齐审查

目标为 `feature/gm-web-admin` 中基于 `0630348d2bace7a35887b43b8aaa0c145b0b66a2` 的当前未提交管理员实现，包含 tracked diff、新增领域/持久化/HTTP/UI 文件、测试和使用文档。审查依据为根与 child `TaskContract.md` revision 1、ready `spec.md`、`reviews/spec-review.md` Round 2 的批准、`implementation-report.md`、根 `multi-spec-map.md` 和当前执行配置。使用 `reviewing-work/SKILL.md`、`references/drift.md`；独立于生产者与规格审查者。本轮未运行构建、测试或真实服务操作。

本阶段为无 Plan 的 focused delivery，没有 Task Unit registry。比较对象是当前完整结果与已批准范围，未把生产报告当作代码检查的替代。

## 对齐证据

| 已批准结果 | 当前累积实现与证据 |
|---|---|
| 独立管理员身份、首次初始化与可撤销会话 | `Data/Administration/GmAdministration.cs`、`Application/Administration/GmAccessDomainService.cs` 与 `GmBootstrapService.cs` 单独定义管理员及初始化。身份不存在时保持公开启动、私有封闭；初始化不覆盖已存管理员。密码复用既有 `IPasswordCredentialRuleService`，会话由随机秘密、服务器摘要与固定 8 小时到期组成；退出删除会话，每次识别读取当前启用状态与权限。没有把玩家账号或票据变成管理员。 |
| 共同鉴权、跨站与连接传输边界 | `GmAccessBoundary.cs` 统一覆盖 `/api/gm/v1`，仅明确标为公开的端点匿名可达，私有端点须具有显式权限 metadata；未注册 API 也不会因缺少 metadata 自动放行。四权限覆盖后续业务接缝。状态写入实际执行 antiforgery 校验，并检查 Origin/跨站来源；远程明文判据采用连接 IP/TLS，而不是转发头。 |
| 登录失败限制 | `GmLoginLimiter.cs` 在验证前预留来源/用户名尝试，包含固定时间窗口、组合和来源计数及容量边界。HTTP 登录拒绝返回 429，既有身份查询独立于该计数。没有扩展为根合同外的玩家封禁功能。 |
| 可追踪、可与未来业务共同提交的审计 | `IGmAuditDomainService`、`SqliteGmAdministrationPersistence` 的 append 使用现有 `SqliteDatabase` 写入与 ambient transaction；登录与退出由 transactional command 执行。审计记录身份、时间、对象、结果和稳定操作号，并提供有边界的分页。Schema 统一新增三张 GM 表与索引，没有在 HTTP 模块另建数据库或旁路 SQL。 |
| 中文登录、权限菜单及审计页面 | `AdminApp.tsx`、`api.ts` 实现初始化提示、登录错误、按当前权限菜单、审计分页、退出和身份失效返回登录。`vite.config.ts` 的代理保持 Host，以衔接同源检查。未来玩家、邮件和点券页明确显示待后续接入，没有虚构业务数据或资产发放入口。 |
| 分层、验证与隔离 | 新 command handler 只注入领域服务；GmWeb 通过命令/领域契约调用，没有新增 Persistence/Protocol 引用。Persistence 实现 internal，建表在统一 Schema。真实 HTTP fixture 显式设置独立 `%TEMP%/dfo-gm-auth-<UUID>/test.db`，审计事务 fixture 使用独立临时库；没有现服部署逻辑。读取既有 `access-final.log` 的 15/15、Release solution build 的零警告/零错误以及前端交互日志 8/8，与报告所述验证方向一致。 |

实际检查了上述源文件、HTTP/审计事务/UI 测试、注册与 Schema diff、产品文档，而非仅核对文件名或报告。`GmAuthenticationHttpTests` 包含跨请求/重启、注销重放、当前权限撤回、过期、CSRF、实际 TLS、连接事实、限流、审计失败共同回滚等相关观察点；这些与已批准的验收方向一致。本轮只消费已有检查结果，不代替末阶段 fresh verification。

## 剩余范围与结论

根 SC-3（全服角色/账号/在线查询）、SC-4（官方邮件）、SC-5（账号点券）及 SC-6 的最终集成仍归属后续独立 Spec。当前 map 明确保留这些结果的 remaining gap；本 child 合同也明确排除对应业务。因此当前占位页和未实现的未来写入口属于合法分阶段交付，不构成遗漏或漂移。

上个 child 的执行游标、根 map/workspace 变更属于主控维护的任务证据，未混入当前管理员产品职责。当前累积实现保持独立 GM 模块、同一 Host/DI、既有领域事务、中文管理界面和隔离验证的中心路线，没有发现需要改变根授权、拆分边界或当前实现方向的实证阻塞。

结论为 `approved / aligned`，允许主控进入独立实现挑战与最终质量审查。本结论仅证明累计工作仍对齐，不批准产品安全质量、整体完成、真实服务切换或玩家资产操作；主控进行中的隔离浏览器验收也不能被当作现服部署证据。

## Round 2 · F-1 局部返工后的累计对齐复核

```yaml
kind: rks-review
verdict: approved
disposition: aligned
```

当前目标仍为同一合同 revision 1、同一 approved Spec、基线 `0630348d` 上的未提交实现；本轮只复核独立完整审查 F-1 引发的三个 owned 文件：`Web/gm-admin/src/AdminApp.tsx`、`AdminApp.test.tsx`、当前 child 的 `implementation-report.md`。Round 1 的累计对齐证据保留，未因局部返工重新扩大审查或修改任何控制器/产品文件。

实际源码已加入身份世代、挂载状态和切换状态保护。登录、退出及初始化刷新开启新世代，旧异步成功/失败/finally 不能覆盖当前状态；轮询绑定其发出时的世代，退出后的身份引用也阻止旧 effect 再派发查询。审计视图按身份/世代重新挂载，旧视图的 401 不能清除后续登录身份。该修改直接补齐 RQ-009、AC-008 已承诺的退出及会话状态反馈，没有更改认证协议、服务器会话/权限策略、模块边界或授权，也未开放玩家查询及资产操作。

读取两个新增 deferred Promise 用例及原始 `ui-generation-red.log`，确认观察对象分别是“退出后旧成功回复不得恢复旧菜单”与“新登录后旧 401 不得清掉新身份与权限”；RED 记录为 2 failed、原有 8 条被过滤 skipped。`ui-generation-final.log` 为 10/10，`ui-generation-build.log` 显示构建完成并保留既有单包体积提示。本轮仅消费这些证据，未执行测试、共享 .NET 构建或服务操作；上述结果不替代独立质量复核和主控 fresh verification。

局部返工仍在既有管理员页面正确性范围内，未发生目标、授权或实现中心路线漂移。当前累计对齐结论为 `approved / aligned`；允许主控继续同一 child 的独立挑战/最终复核流程。过期会话清理仍作为已记录的非阻塞维护项，不在本次 F-1 修复中扩展实现。
