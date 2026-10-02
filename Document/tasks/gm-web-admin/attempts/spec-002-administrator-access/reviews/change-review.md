---
kind: rks-review
verdict: approved
---

# Spec-002 独立完整结果审查 · Round 1

历史结论为 `changes_required`，以下保留原始发现与证据；当前文件级 verdict 以 Round 2 为准。

审查目标：`feature/gm-web-admin` 基于 `0630348d2bace7a35887b43b8aaa0c145b0b66a2` 的当前未提交管理员访问/审计实现，以及对应测试、中文前端和使用说明。依据为根与 child `TaskContract.md` revision 1、ready `spec.md`、已批准的 `spec-review.md` Round 2、`drift-review.md` 的 approved/aligned、独立 `change-challenges.md`。使用 `reviewing-work/SKILL.md`、`references/code.md` 和 `references/deliverable.md`。审查者独立于生产者、挑战生成者及主控。

实际读取新增 Administration 契约、模型、领域服务/命令、SQLite 实现、GM 中间件/端点/bootstrap/限流及完整 tracked 注册/Schema/架构测试 diff；核对真实 HTTP、限流/共同事务测试、前端/API/交互测试、使用文档和原始检查日志。生产报告只作为证据定位。没有构建共享 .NET 输出，也没有操作源 checkout、现服或在线数据库；额外 UI 时序探针仅在忽略的 `artifacts/gm-review-probe` 中运行当前源文件副本。

## 阻塞发现

**F-1 / CH-1：退出成功后的旧身份回复重新显示已退出管理员。**

`Web/gm-admin/src/AdminApp.tsx:32-36` 的定时身份查询直接应用 `setIdentity`；effect 清理只停止下一次定时器，不忽略已经发出的 Promise。`:42-44` 的退出只清空当前身份。因此旧会话查询先在服务器完成、网络响应迟到时，退出页面能够被旧身份重新覆盖。

独立最小复现已经执行：首次身份查询成功，触发 60 秒查询并挂起响应，完成退出后确认出现“管理员账号”登录表单，再完成挂起查询为旧 operator Identity；页面重新出现“退出登录”与旧权限菜单，登录表单消失。`artifacts/gm-review-probe/src/session-race.test.tsx` 的 1/1 通过是在断言缺陷确实出现。探针副本与产品 `AdminApp.tsx` 的 SHA-256 均为 `74B022C655E42FDFB8C18DBB1DA1C6CA3AAC738137CB1A90548DE64FCDBDB5AF`。

执行目录：任务 worktree 的 `artifacts/gm-review-probe`；命令：`node node_modules/vitest/vitest.mjs run --config vitest.config.ts`。结果：exit 0，Test Files 1 passed，Tests 1 passed，Duration 3.16s。这里的 pass 专门确认缺陷观察成立，**不是 passing 产品验收**；没有编辑产品文件。

后端依然通过持久化撤销拒绝旧 Cookie；本发现不声称服务端授权恢复或资产可发放。实际后果是 RQ-009 所承诺的退出/会话状态反馈不稳定，并且同一缺少世代保护的路径可由旧 401 清掉后来建立的新身份。当前是低风险、局部可修复的结果缺陷，仍应在本 child 验收前处理。

最小充分修复：登录状态切换时，让之前身份世代的在途成功或失败回复不再改变当前身份；增加可控响应顺序的 UI 回归，至少覆盖“旧成功回复晚于退出”和“旧 401 晚于新登录”。无需修改会话协议、服务端持久化或 Spec。修复后独立复核此项及直接回归，再由主控 fresh verification。

## 实现挑战裁决

| ID | 裁决 | 证据与处置 |
|---|---|---|
| CH-1 | confirmed | 见 F-1，已通过 deferred Promise 探针实际复现；需要局部修复，不能用后端安全拒绝代替页面状态正确。 |
| CH-2 | confirmed / non-blocking | `GmAccessDomainService.LoginAsync` 插入新会话、`IdentifyAsync` 对过期返回 null，`SqliteGmAdministrationPersistence` 只有显式退出删除，没有过期回收。现有实现仍严格拒绝过期秘密；限流字典容量独立有界。没有当前性能或授权失败证据，也没有本阶段会话历史容量的强制要求，因此不阻塞。保留明确的后续维护项：制定过期/撤销会话清理策略，沿用事务并保留未到期会话与审计。 |

## 前期 Spec 挑战闭合

| ID | 当前裁决 | 实际闭合依据 |
|---|---|---|
| Spec CH-1 默认封闭 | rejected as current defect / closed | `GmAccessBoundary` 对整个 `/api/gm/v1` 检查，仅显式 `GmPublicEndpoint` 可匿名；私有缺少 `GmRequiredPermission` 返回 403。真实 HTTP 对未注册 API 的匿名 401、已登录 403 与审计权限不足/撤回场景覆盖该接缝，未来业务必须显式标权限。 |
| Spec CH-2 并发/容量绕过 | rejected as current defect / closed | `GmLoginLimiter.TryBegin` 在锁内预留来源与用户名预算，完成成功只扣自己的预留，回调幂等；容量饱和拒绝新来源而不淘汰仍受限来源。`ConcurrentFailuresCannotLoseTheirReservations`、`CapacityIsBoundedAndNewSourcesCannotEvictBlockedSources`、`SuccessDoesNotGrowNameStorageBeyondCapacityOrEraseOtherFailures` 给出对应证据。 |
| Spec CH-3 状态与审计 | rejected as current defect / closed | 登录、退出注册为 Transactional 命令，写入参加同一 ambient SQLite 事务；登录先审计后会话、退出先删会话后审计但均以事务收口。`AuditFailureRollsBackLoginAndLogoutInSameTransaction` 注入审计 append 后异常，分别验证无登录会话/审计残留，以及退出失败保留原有效会话及此前审计；Persistence 共同提交/回滚和事务外拒绝进一步核对未来接缝。 |
| Spec CH-4 Cookie 歧义 | confirmed / already resolved | approved Spec AC-002 已明确安全 Set-Cookie 为受控例外；端点 JSON 只返回 Identity，数据库会话只存摘要，原始日志/审计无密码与会话秘密。实际实现没有再次混淆响应正文与必要 Cookie 交付。 |
| Spec CH-5 开发代理 | rejected as current defect / closed | Vite `changeOrigin: false` 保留浏览器 Host，Origin 校验仍采用服务端请求方案/Host，传输门禁仍采用实际远端 IP/TLS。主控浏览器证据记录 5175 开发代理的真实退出/重新登录，以及发布页面登录/分页/重启/退出；无需信任 Forwarded/X-Forwarded 头。 |

## 其余结果与证据

独立身份不接入玩家账号；密码沿用框架 PasswordHasher（随机盐与成熟参数），opaque 256 位秘密在库中只存 SHA-256 摘要。每次识别读取当前管理员启用状态与权限；固定 8 小时过期、注销撤销、缺权拒绝、JSON 写入实际 antiforgery/Origin 检查、真实连接远程明文拒绝和实际 TLS Cookie 标志均有相应代码和真实 HTTP 证据。没有发现本阶段其他认证绕过或秘密落库/响应/日志问题。

分层保持 GM HTTP 经 CommandBus/领域契约，Application 无 Persistence/Protocol 引用，新 persistence 类型 internal、新表仅统一 Schema 声明；架构 sweep 扩到 Application + Protocol，没有删掉旧断言。审计分页通过 Transactional 查询得到稳定快照和边界响应。中文说明与实际配置、权限、临时 CSRF 密钥及重启行为相符。

消费原始日志确认 Host 45/45、Persistence 396/396、聚焦 Application 61/61、认证/限流 15/15、UI 8/8，以及 Release solution build 零警告/零错误。前端 build 通过，单包大小提示保留，未将其改写为失败或忽略既有证据限制。主控浏览器检查属于隔离开发验证。上述旧检查不能替代修复后主控 fresh verification。

玩家查询、官方邮件、点券与最终构建/发布集成属于根控制器后续 Spec，本阶段占位页不是当前结果缺漏。本 review 只裁决管理员/审计 accepted target，不批准根任务完成、现服部署、真实资产发放或推送合并。

Round 1 verdict 为 `changes_required`，唯一阻塞为 F-1；CH-2 是保留的非阻塞维护项。

## Round 2 · F-1 有界修复及直接回归复核

```yaml
kind: rks-review
verdict: approved
```

目标仍是同一合同 revision 1、同一 approved Spec、基线 `0630348d` 上的完整管理员 accepted target。本轮仅复核 F-1 引发的 `AdminApp.tsx`、`AdminApp.test.tsx` 和 `implementation-report.md`；原挑战集合与 Round 1 不变的认证/审计/分层证据保留引用。已读取 `drift-review.md` Round 2 的 approved/aligned。当前 `AdminApp.tsx` SHA-256 为 `FA673AC770AC1A46B1CF9B16883BA425C0737B7A69AE1AF363C2FA6B6FE58549`。

实际修复通过身份世代和挂载状态限制异步状态写入：refresh、login、logout 开始切换时递增世代；相应成功、失败及 finally 必须仍属于当前世代。轮询在派发时捕获世代，cleanup 将旧 effect 置为 inactive，切换中与已无身份时不再派发新轮询。审计视图按身份/世代挂载，并将过期回调绑定当时世代；旧视图或切换中的 401 不清除后来建立的身份。退出失败仍保留身份并给出错误，正常登录/权限/审计路径没有被移除。未修改服务端会话协议或既有访问保护。

两个新增生产回归观察消费者行为而不绑定 ref 的实现：旧成功晚于退出时保留登录表单；旧 401 晚于新登录时保留新管理员名称和权限菜单。原始 `ui-generation-red.log` 明确两条行为失败（原 8 条由过滤跳过），`ui-generation-final.log` 为完整 UI 10/10；`ui-generation-build.log` 显示构建完成，并保留之前的包体积提示。没有将 fixture 错误或未执行的检查宣称为 RED/GREEN。

独立正向复核另在 `artifacts/gm-review-r2` 的当前源码副本中执行 `node node_modules/vitest/vitest.mjs run --config vitest.config.ts`：exit 0，2/2，Duration 3.36s。`src/session-order.test.tsx` 分别验证旧成功回复不能恢复退出身份，以及旧 401 不能清除**相同管理员 ID**的新登录（不同登录世代仍隔离）。源码副本 SHA-256 与上述产品文件一致。此次 probe 断言的是正确结果；Round 1 的缺陷 oracle 和旧源副本仍原样保留，没有拿旧缺陷测试的 pass 充当修复通过。

F-1 已解决，实现 CH-1 裁决更新为 `confirmed / resolved`。CH-2 仍为 confirmed、非阻塞维护项；Spec CH-1/2/3/5 的当前闭合和 CH-4 的已解决边界仍按 Round 1 引用，没有新修改推翻这些证据。本轮未发现修复引入的阻塞回归，没有重复共享 .NET 构建、触及在线数据库或修改产品/控制器。

当前 verdict 为 `approved`，允许主控对本 child 进行 fresh verification 和验收提交。该结论只批准当前管理员访问/审计 accepted target，不能替代 fresh verification，也不宣布根任务、真实部署或后续玩家/资产模块完成。
