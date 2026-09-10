---
kind: rks-verification
decision: pass
---

# 完成验证：WPF 启动器与票据登录链

## 被验证的结果

当前 `task/launcher-auth-login` 工作树相对基线
`e567c803b99260786aaddb0d2d4612dc3334b6f4` 的完整变更，满足 TaskContract revision 1、
Spec revision 3 的 `AC-001`—`AC-018`，并已通过独立 drift review 与 final change review。

本次完成验证使用实现完成后的新鲜构建、逐工程 TRX、真实发布目录冒烟和当前视觉证据；不以任务执行方的
“已完成”陈述代替运行证据。

## Fresh Release 结果

`dotnet build Server/86JP.Server.slnx -c Release --no-restore`：**0 warnings、0 errors**。
日志：`evidence/final-verification/solution-build.log`。

设置有效的 `PVF_ARCHIVE_PATH` 后逐工程执行测试：

| 工程 | 通过 | 失败 | 跳过 | 证据 |
|---|---:|---:|---:|---|
| Dfo.Launcher.Tests | 60 | 0 | 0 | `evidence/final-verification/Dfo.Launcher.Tests.trx` |
| Dfo.Pvf.Tests | 60 | 0 | 0 | `evidence/final-verification/Dfo.Pvf.Tests.trx` |
| Dfo.Server.Application.Tests | 1341 | 0 | 0 | `evidence/final-verification/Dfo.Server.Application.Tests.retry.trx` |
| Dfo.Server.Host.Tests | 17 | 0 | 0 | `evidence/final-verification/Dfo.Server.Host.Tests.trx` |
| Dfo.Server.Infrastructure.Tests | 301 | 0 | 2 | `evidence/final-verification/Dfo.Server.Infrastructure.Tests.trx` |
| Dfo.Server.LauncherApi.Tests | 50 | 0 | 0 | `evidence/final-verification/Dfo.Server.LauncherApi.Tests.trx` |
| Dfo.Server.Networking.Tests | 62 | 0 | 0 | `evidence/final-verification/Dfo.Server.Networking.Tests.trx` |
| Dfo.Server.Persistence.Tests | 355 | 0 | 0 | `evidence/final-verification/Dfo.Server.Persistence.Tests.trx` |
| Dfo.Server.Protocol.Tests | 714 | 0 | 0 | `evidence/final-verification/Dfo.Server.Protocol.Tests.trx` |
| **合计** | **2960** | **0** | **2** | 九份 TRX |

Infrastructure 的两条跳过项只适用于缺失 PVF 的反向场景；清除 `PVF_ARCHIVE_PATH` 后定向复跑为
**2/2 通过**，证据为 `Dfo.Server.Infrastructure.Tests.missing-pvf.trx`。因此全部测试身份都在其所需
环境中取得通过结果。

首次 Application 全量运行曾有 1 条既有背包测试
`DraggedAvatarStaysWholeTests.A_drag_of_one_moves_the_whole_avatar_and_empties_the_slot_it_came_from`
抛出 SQLite `ObjectDisposedException`。该原始失败保留在 `Dfo.Server.Application.Tests.trx`；随后：

- 该精确用例单独连续运行 10 次全部通过；
- 禁用 xUnit collection 并行后全工程 1341/1341 通过，见 `serial-diagnostic.trx`；
- 正常并行配置完整重试 1341/1341 通过，见 `retry.trx`。

异常栈和仓内 18 处 `SqliteConnection.ClearAllPools` 指向既有并行测试夹具间的连接池竞态；它不在本变更
认证/启动器路径，且正常配置新鲜复跑已恢复，因此如实保留为非产品回归诊断，不改动无关测试框架。

## Success Conditions

| 条件 | 判定 | 决定性证据 |
|---|---|---|
| SC-1 | 通过 | Release solution build 0/0；九工程 2960 通过、0 失败；两个条件项在缺 PVF 环境 2/2 通过。 |
| SC-2 | 通过 | LauncherApi、Application、Persistence 的注册/输入/错误语义/现代密码测试；密码库只存版本化随机盐 KDF 记录。 |
| SC-3 | 通过 | HTTP login 投影票据、UTC expiry 与公共 Channel endpoint；固定时钟验证默认 60 秒。 |
| SC-4 | 通过 | Persistence/Application/Networking 覆盖首次、过期边界、篡改、错账号、复用、并发兑换及提交/取消故障；至多一次成功。 |
| SC-5 | 通过 | Launcher 配置、HTTP client、ViewModel、偏好与启动测试 60/60；示例 INI 仅一个 BaseUrl。 |
| SC-6 | 通过 | `GameLauncherTests` 逐槽验证 35 槽模板，并验证单 `ArgumentList`、特殊字符不拆槽、参数不含密码/摘要/BaseUrl/固定端点。 |
| SC-7 | 通过 | Host 17/17 包含真实 HTTP → Channel 握手/Channel Info → Game 兑换闭环，成功 CMD、六条有序通知及后续 roster 使用同一 AccountId。 |
| SC-8 | 通过 | 四态离屏布局/截图、当前桌面 DPI 的真实 WPF 窗口，以及本次干净发布目录真实窗口 smoke 均无阻塞。 |

## Acceptance Criteria 映射

| AC | 判定 | 证据 |
|---|---|---|
| AC-001—002 | 通过 | Launcher 60/60；LauncherApi 50/50；Host bootstrap ready/503 与发布 smoke。 |
| AC-003—005 | 通过 | Persistence 355/355、Application 1341/1341、LauncherApi 50/50：注册竞态、输入边界、同形 401、现代 KDF、60 秒票据与端点投影。 |
| AC-006—007 | 通过 | 敏感哨兵覆盖日志/异常/响应/启动失败；逐 AccountId 旧 MD5 allowlist、拒绝与同事务升级测试通过。 |
| AC-008—009 | 通过 | 票据只存 digest；原子消费与并发仅一成功；提交失败/提交前取消无成功帧、保持匿名、票据可重试。 |
| AC-010 | 通过 | Launcher 参数构造逐槽及单参数安全测试通过。 |
| AC-011 | 通过 | `LauncherAuthenticationEndToEndTests` 使用三类不同的非 loopback 公共 host，只消费 HTTP 与 Channel Info 实际解码值，完成真实 socket 链与后续身份读取。 |
| AC-012—013 | 通过 | Application/LauncherApi 架构测试与命令注册通过；400/401/409/503、远程明文前置拒绝和安全 internal_error 测试通过。 |
| AC-014 | 通过 | 同目录 DNF、工作目录、单参数、缺 exe、到期与进程失败分支测试通过；真实发布 WPF 进程 smoke 通过。 |
| AC-015 | 通过 | `task05-{login,register,loading,error}-{96,144}dpi.png` 是离屏回归；`task05-live-login-current-dpi.png` 与发布窗口是当前桌面实窗 smoke。按 revision 3 不声称做过精确双 DPI 实窗矩阵。 |
| AC-016 | 通过 | `Document/LauncherAuthRunbook.md`、单 BaseUrl 示例、`evidence/final-verification/publish-smoke.md`。 |
| AC-017 | 通过 | Fresh Release 构建 0 warning/0 error；九工程 2960/0/2，并为两个条件 skip 补齐缺 PVF 的 2/2。 |
| AC-018 | 通过 | Application/Networking 的 durable commit 后连接消失、身份发布/投递失败与 replay 拒绝测试通过。 |

## Review 与残余项

- `reviews/drift-review.md` 当前 Round 2：`classification: aligned`；首轮双 DPI 实窗证据意见原文保存在
  `reviews/drift-review-round1.md`，Spec revision 3 如实收敛为用户确认的临时 UI 验收边界。
- `reviews/change-review.md`：`verdict: approved`，无阻塞缺陷。
- 三个非阻塞后续：密码 KDF 持有 SQLite writer 的并发/SLO 需按未来负载量化；偏好文件写失败目前会阻止
  已成功登录后的启动；`login_tickets` 尚无历史行保留/清理策略。它们均未违反当前 TaskContract，已在
  `reviews/change-challenges.md` 与 final review 中完整留痕。

## 判定

`pass`。SC-1—SC-8 与 AC-001—AC-018 均有当前工作树上的新鲜证据；可以生成 Spec Result 并创建一次
本地接受提交。该判定不授权 merge、push 或清理 worktree。
