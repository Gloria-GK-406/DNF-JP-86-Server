---
kind: rks-completion-evidence
contract:
  path: ../TaskContract.md
  revision: 1
state: ready-for-live-test
---

# Completion Evidence

## Automated verification

- `dotnet build Server/86JP.Server.slnx -c Release --no-restore --verbosity minimal`
  - 结果：0 warning，0 error。
- `dotnet test Server/86JP.Server.slnx -c Release --no-build -m:1 --logger trx --results-directory .rks-agent/tasks/story-party-dungeon/verification/full-round7 --verbosity quiet`
  - 环境：`PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-protocol\Script.pvf`。
  - TRX：9 个文件，3245 个结果；3243 Passed、0 Failed、2 NotExecuted。
  - 两项 NotExecuted 是提供 PVF 时按设计跳过的缺失档案测试。
- `git diff --check`
  - 结果：通过。
- 快进合并后的 `main` 再次执行相同 Release 构建与串行完整测试。
  - 结果：0 warning、0 error；3243 Passed、0 Failed、2 NotExecuted。

## Review

- 完整代码复审 Round 2：`approved`。
- 原七项阻塞全部关闭；没有发现新的阻塞回归。

## Runtime evidence

- 实现提交：`03762701`（`feat: add shared story party dungeon runs`）。
- 集成提交：任务分支快进合并至 `main`，保留 `03762701` 与 `5b92a639` 的提交顺序。
- 运行进程：PID `62336`。
- 运行产物：`E:\Project\DNF\DNF-JP-86-Server\Server\src\Dfo.Server.Host\bin\Release\net10.0\Dfo.Server.Host.exe`。
- Socket 监听：`0.0.0.0:7001`、`0.0.0.0:10010`、`0.0.0.0:10011` 均归属 PID `62336`。
- LauncherApi 监听：`http://127.0.0.1:18080`，`GET /api/launcher/v1/bootstrap` 返回 HTTP 200 和 `status=ready`。
- 静态内容：`E:\Project\DNF\DNFClient-A21-protocol\Script.pvf`，启动日志确认已加载。
- 数据库：继续使用主运行库 `E:\Project\DNF\DNF-JP-86-Server\Server\src\Dfo.Server.Host\bin\Release\net10.0\Data\dfo-server.db`。
- 切换前备份：`dfo-server.before-story-party-20260914-1922.db`。
- 启动模块包含 `Infrastructure, Persistence, Application, Networking, Protocol, LauncherApi`；日志包含 `Application started`，stderr 长度为 0。

## Honest boundary

自动化证据证明服务端已具备双客户端连接与测试条件，但不能替代 A21 实机对队员首发投递顺序和客户端状态转换的确认。实机通过前仍标记为待验证，不标记为 `ClientVerified`。

## First live-test remediation

- 首次双客户端进入地下城选择区时，客户端提示与队友连接失败。
- 运行证据排除 HTTP、组队建立和本机防火墙：两端 Game TCP 会话、UDP 端口上报、队伍通知及 UDP 本地监听均存在。
- 根因：服务端把客户端报告的虚拟网卡 IPv4 原样投影进 `0x000B`；该地址并不存在于当前活动适配器。
- 上游 `E:\Project\86JP@e95cb259` 的 direct path 明确使用 Game TCP remote address，只保留客户端报告的动态 UDP 端口，并输出 `NatType=0`、`MTU=1500`。
- 当前修复保持 PartyService 的传输无关镜像边界：TCP remote address 只进入 Network dispatch metadata，并在 Protocol handler 中规范化为 `PartyEndpoint`。
- 修复后独立复审：`approved`。
- 修复后最终 Debug 全套：9 个 TRX，3249 个结果；3247 Passed、0 Failed、2 NotExecuted。
- 主干修复提交：`4f4bd13e`（`fix: canonicalize party peer endpoints`）。
- Release 构建：0 warning、0 error。
- 新运行进程：PID `64888`，产物来自主干 Release；`7001`、`10010`、`10011`、`18080` 均由该进程监听，Launcher bootstrap 返回 `status=ready`。
- 状态：已部署，等待第二次双客户端实机确认，不标记为 `ClientVerified`。
