---
kind: rks-review
verdict: approved
---

# Final Change Review

## Reviewed identity

- 独立 reviewer：`/root/final_change_reviewer`；未参与实现、Task quick review、drift review 或 challenge generation。
- Change scope：`E:\Project\DNF\DNF-JP-86-Server\.worktrees\launcher-auth-login` 中基线 `e567c803b99260786aaddb0d2d4612dc3334b6f4` 之上的完整未提交变更；当前 `HEAD` 仍为该基线。
- 受控输入：`TaskContract.md` revision 1、`spec.md` revision 3、`plan.md`、`reviews/drift-review.md` 与 `reviews/change-challenges.md`。
- 审查前快照：35 个 tracked changed paths；tracked binary patch Git blob `cd004c406433c1bb5cc5bc11b2e7a2c37ab7aac4`。排除本 review 后 354 个 untracked paths，其排序 `path<TAB>SHA256`（UTF-8、LF、末尾 LF）manifest SHA-256 为 `E846755956EFA1CCC7AEAA5F89F16092BC8970E0C38A0FB41D0287EDA53F0D37`。`git diff --check e567c803 -- .` 为 0。

## Verdict

`approved`。实际变更满足 TaskContract revision 1、Spec revision 3 和 Plan 的完整结果边界；未发现会导致认证错误、身份提前发布、票据重放、HTTP→Channel→Game 路由绕过、秘密泄露或现有 Game 身份链回归的阻塞缺陷。视觉证据按 revision 3 的当前边界裁定：四态离屏布局/截图与当前桌面 DPI 的真实窗口 smoke 已具备，不重新要求精确双 DPI 实窗矩阵。

Fresh verification 对当前快照完成：Release solution build 为 0 warning / 0 error；九个测试项目共 2960 Passed、0 Failed，带 PVF 环境下 Infrastructure 的 2 个缺档案反向用例为条件 skip，移除 PVF 后对这两个用例单独复跑为 2 Passed。Host 17/17 包含真实进程的 HTTP login → Channel 握手/Channel Info → Game ticket redeem → 成功 CMD、六条有序通知及后续 AccountId 读取闭环。

## Challenge Dispositions

### CH-1 — needs-evidence（非阻塞后续）

代码事实成立：`AuthenticationDomainService.AuthenticateAndIssueTicketAsync` 在账号读取和真实/dummy PBKDF2 前调用 `AcquireWriteIntentAsync`，SQLite 零行 `UPDATE` 取得的全局 writer 资格保持到命令提交；并发密码登录因此串行，其他写者也会等待。现有交错测试还明确验证第二次登录在第一次密码校验释放前不能进入读取。

但挑战尚未证明一个相对于批准规格可裁定的失败阈值：TaskContract/Spec 没有认证吞吐、并发、抗资源耗尽或 rate-limit 要求；Plan 同时要求高成本 dummy 校验、迁移与签票同事务，当前策略解决了真实的 deferred snapshot 读后升级失败。等待会在持锁请求完成后自动恢复，不产生身份、票据或账号状态损坏；挑战没有提交“错误登录负载使既有 Game 写达到可复现失败/超时”的当前实现测量，也没有批准的可用性 SLO 可据此判失败。因此不把 high-risk 标签本身升级为 change defect。建议后续以明确负载/SLO 验证短事务重读/CAS方案，并在部署入口加入有界并发或限速。

### CH-2 — confirmed（非阻塞后续）

`LauncherViewModel.SubmitAsync` 在 HTTP 登录成功后先等待 `preferences.SaveAsync`，之后才调用 `gameLauncher.Launch`；即使未勾选记住账号也会写空偏好。持久的本地偏好写失败会进入通用错误状态并阻止这次 60 秒票据启动，现有测试没有覆盖该异常。这是真实的低概率本地恢复性缺口，但不破坏认证、票据、秘密生命周期或已规定的正常启动结果，且 Spec 未把偏好存储不可写时的降级列为验收条件，故不阻塞本次交付。后续宜把可选偏好保存降级为非致命提示，并补“保存抛错但游戏仍只启动一次”的测试。

### CH-3 — confirmed（非阻塞后续）

`login_tickets` 当前只插入并标记 `consumed_at`，没有删除、保留期或按过期时间清理的索引，历史行会随成功登录次数增长。一次性、过期和原子兑换语义当前均正确，TaskContract/Spec 未要求 retention、容量上限或维护任务，有限运行期内也无已示失败，因此这是长期运维债而非当前验收缺陷。后续应定义保留期、索引和批量清理测试，确保活跃未消费票据不被删除。

## Decisive evidence

- 事务与身份：SQLite 条件消费绑定 ticket digest、精确 loginId、未消费和严格过期条件；DomainTransaction 先提交唯一可失败的 durable participant，再提交 `CommitCanFail=false` 的 OnlineUser participant。提交前失败保持匿名且票据可重试；提交后目标消失/发布或投递失败会清理连接，票据保持已消费。
- 协议链：端到端 harness 只按 HTTP 与 Channel Info 实际解码出的两级广告 tuple 走测试侧路由，三类公共 endpoint 使用不同非 loopback 值；Game 成功后六条通知顺序和后续 roster 的同一 AccountId 均被真实 socket 结果验证。
- 秘密边界：现代密码为带项目 scheme 的 IdentityV3、生产 220000 次 PBKDF2；旧 MD5 仅逐 AccountId allowlist 且同事务升级；数据库只存 SHA-256 ticket digest。HTTP 远程明文 gate 在 body 读取前拒绝，Launcher 禁止重定向并使用 `ArgumentList` 单参数启动，常规日志只记录稳定类别。
- 回归与 UI：完整 Release 构建和全部项目测试通过；四态离屏图与当前桌面真实窗口图均无阻塞布局，符合 Spec revision 3 的明确验收取舍。
