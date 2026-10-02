---
kind: rks-verification
decision: pass
---

# 管理员访问与审计阶段验收

2026-10-02，工作目录为父控制器唯一 worktree，基线 `0630348d2bace7a35887b43b8aaa0c145b0b66a2`。当前完整结果经独立 drift Round 2 approved/aligned、change review Round 2 approved；F-1 已解决。以下检查针对修复后的同一产品树，验证期间没有产品变化。

| 合同条件 | 当前证据 |
|---|---|
| SC-1 初始化、持久化、独立身份 | Host 全量 45/45（0 failed/skipped），覆盖无/缺项 bootstrap、正确/错误登录、重启不覆盖管理员及玩家票据拒绝。原始 TRX：`artifacts/gm-tests/fresh/spec002-host-fresh.trx`。 |
| SC-2 撤销、过期、权限、跨站、传输 | 同一 Host 运行覆盖旧 Cookie 撤销、8h 到期、禁用/撤权、实际 antiforgery、来源检查、转发头伪造拒绝、真实 HTTPS Secure Cookie、限流及审计故障共同回滚。独立审查已核对安全边界，参见 `../reviews/change-review.md`。 |
| SC-3 中文交互与安全审计 | 修复后 `npm test` 10/10（exit0）；包括两条在途回复乱序回归及原初始化/登录/权限/分页/退出/过期交互。`npm run build` 类型检查与 Vite 发布构建 exit0。保留原真实浏览器证据 `./browser-verification.md`；新乱序行为由当前组件测试和独立正向 probe 2/2验证，未冒充线上验收。 |
| 分层及持久化回归 | Persistence 全量396/396（0 failed/skipped），TRX `artifacts/gm-tests/fresh/spec002-persistence-fresh.trx`；Application Authentication + CommandHandlerArchitectureTests 61/61（0 failed/skipped），TRX `artifacts/gm-tests/fresh/spec002-application-fresh.trx`。包含统一 Schema 和业务/审计同事务。 |
| 构建及完整范围 | `dotnet build Server/86JP.Server.slnx -c Release --no-restore -v minimal` exit0，0 warnings/errors；`git diff --check` exit0。产物均在 worktree，所有测试库隔离。 |

当前 AdminApp SHA-256 `FA673AC770AC1A46B1CF9B16883BA425C0737B7A69AE1AF363C2FA6B6FE58549`，与独立 Round 2 目标一致。前端 JS 1050.14 KB / gzip336.55 KB，Vite 单包大小提示仍存在，最终多页面集成时再评估分包。过期会话回收是非阻塞维护项，不影响当前过期拒绝/注销撤销。

以上 pass 仅声明本 child 的实现、自动验证及开发浏览器检查完成。根角色查询、邮件、点券、CI/发布完整集成及最终人工验收仍待后续阶段；没有切换现服、初始化真实管理员或操作玩家资产。
