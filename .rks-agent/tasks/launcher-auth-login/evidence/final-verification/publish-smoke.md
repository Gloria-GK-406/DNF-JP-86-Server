---
kind: rks-runtime-evidence
decision: pass
verified_at: 2026-09-10T19:15:10+08:00
---

# 干净发布目录 Host + Launcher 冒烟

## 边界

- 临时发布根：`C:\Users\Gloria\AppData\Local\Temp\launcher-auth-final-smoke-01a08ab9`。
- Host 与 Launcher 均从当前工作树重新执行 `Release` 发布，不使用仓库内既有发布目录。
- 只验证发布装配、真实 Host 监听、版本化 bootstrap、已发布 WPF 窗口启动及清理；没有启动或修改 DNF 客户端。

## 发布结果

- Host 发布成功。
- Launcher 首次使用 `-r win-x64 --no-restore` 时得到 `NETSDK1047`，原因是当前 assets 文件尚无 `win-x64` target；随后按运行手册允许 restore 的发布命令重新执行并成功。这是发布前置资产诊断，不是最终失败。
- 发布目录中的 `Launcher.ini.example` 与源码示例 SHA-256 均为
  `8AC7476DE578CF7530FC598F2BFE860AB981AEE5F554F08A9ECB8A18641944D1`。
- 发布产物身份：Host exe SHA-256
  `D9CAF2F0F33CED84ADC50BC540ACEBE9B94EFA0771383CD902F173661C06D8C1`；Launcher exe SHA-256
  `2B96FC0A14DBD17A0944E5E8E486645834524A902402C5FF76786ED9E7094EE2`。

## 真实进程检查

Host 以发布目录为 content root 启动，启用模块为
`Infrastructure, Persistence, Application, Networking, Protocol, LauncherApi`，并使用隔离端口：

| 服务 | 实际监听 |
|---|---|
| Launcher HTTP | `127.0.0.1:18080` |
| Channel | `127.0.0.1:17021` |
| Game | `127.0.0.1:11030`、`127.0.0.1:11031` |

手工请求 `GET /api/launcher/v1/bootstrap` 返回 HTTP 200，body 为
`{"apiVersion":"1","status":"ready","registrationEnabled":true}`；四个端口当时均处于监听状态。

把发布目录的运行时 `Launcher.ini` 指向 `http://127.0.0.1:18080/` 后，实际启动已发布
`Dfo.Launcher.exe`。1.5 秒后进程仍存活、`Responding=True`、窗口标题为
`DNF JP 86 Launcher`；Host 又记录了一次 bootstrap HTTP 200，证明该窗口使用发布配置访问了发布 Host。
整个冒烟期间 `DNF` 进程数为 0。

## 清理与末态

- 仅停止本次已核对路径的 Launcher PID 14676，并通过原 Host 会话正常结束 Host。
- 复查 `18080/17021/11030/11031` 均无监听。
- 复查 `Dfo.Launcher` 与 `DNF` 进程数均为 0。
- 2026-09-10T19:20:21+08:00 再次执行末态哨兵，四个端口仍无监听，且未发现
  `Dfo.Launcher`、`DNF` 或本次发布 Host 进程。

## 配置与安全哨兵

- 源码示例严格只有 `[Launcher]` 与 `BaseUrl=http://127.0.0.1:8080/`。
- 生产启动路径使用 `UseShellExecute=false` 和单个 `ArgumentList` 项；HTTP client 明确
  `AllowAutoRedirect=false`。
- `git diff --check e567c803 -- .` 通过，无空白错误。

结论：`pass`。EV-004 所需的干净发布目录、本地 loopback Host + Launcher 联调与单 BaseUrl 示例均已实测。
