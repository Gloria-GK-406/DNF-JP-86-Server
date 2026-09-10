---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result：WPF 启动器与票据登录链

## Delivered

- 新增 .NET 10 WPF 启动器：严格单 BaseUrl 配置、bootstrap、注册、登录、记住账号、加载/错误状态及
  同目录 A21 DNF 单参数启动；密码不写入配置、日志、Game 协议或进程参数。
- Host 新增版本化 Launcher HTTP API；账号使用版本化随机盐 PBKDF2 记录，旧 MD5 只允许逐 AccountId
  显式迁移；登录签发 60 秒、一次性、只持久化摘要的 CSPRNG 票据。
- Game `0x0001 LOGIN` 兑换票据并绑定既有 AccountId；失败关闭连接，成功仅在 durable commit 后发布
  OnlineUser，再沿用现有成功 CMD、六条通知与后续选角/入场身份链。
- 三类公共端点分别配置并投影；端到端测试从 HTTP Channel bootstrap 开始，消费 Channel Info 后再进入
  Game，不以监听地址或测试已知端口替代客户端可见路由。
- 新增同仓运行手册、单 BaseUrl 示例及 WPF 四态视觉回归证据。

## Verification

- `verification/completion-evidence.md`：`decision: pass`。
- `reviews/drift-review.md` Round 2：`classification: aligned`。
- `reviews/change-review.md`：`verdict: approved`。
- Fresh Release build：0 warnings、0 errors。
- 九个测试工程：2960 passed、0 failed、2 conditional skipped；两个条件项在缺 PVF 环境另行 2/2 passed。
- 干净发布目录 Host + Launcher 真实 loopback 冒烟：`evidence/final-verification/publish-smoke.md`，`decision: pass`。

## Repository Result

- 基线：`e567c803b99260786aaddb0d2d4612dc3334b6f4`。
- 结果位于 `task/launcher-auth-login` 独立 worktree；接受动作只创建一次本地提交。
- 未 merge、未 push、未清理 worktree，未修改任何客户端二进制或只读取证目录。

## Successor Guidance

- 当前密码认证在 PBKDF2 期间持有 SQLite writer 资格；未来若定义并发/SLO，应量化错误登录负载并评估
  短事务重读/CAS 或入口限速，不能在没有事务证据的情况下直接缩短锁范围。
- Launcher 偏好文件写失败会阻止已取得票据后的启动；后续可把可选偏好保存降级为非致命提示并补回归。
- `login_tickets` 没有历史行保留与清理策略；后续需定义 retention、索引和不误删活跃票据的批处理测试。
